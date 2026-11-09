using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

//(Grabación del live · diagnóstico) Perfil del bucle de grabación offline.
//
//QUÉ HACE
//-------
//Mide, frame a frame, en qué se va el tiempo del bucle de grabación: actualización de la escena y
//envío de las cámaras, espera hasta el fin de frame, composición de la letra, lectura de píxeles
//(que sincroniza la GPU y suele ser el sospechoso), copia del búfer y espera a FFmpeg.
//
//Además, antes de grabar (sólo con el perfil activo) se hacen dos SONDAS para separar los costes:
//  · 'p' render sin leer píxeles (la GPU se vacía cada 8 frames): coste del bucle, sin captura.
//  · 'q' el frame en que toca vaciar (render + composición + lectura de píxeles).
//  · 'b' render + lectura de píxeles EN CADA frame, sin enviar nada a FFmpeg.
//  · 'c' la grabación de verdad (render + lectura + copia + FFmpeg).
//Comparando 'p'/'b'/'c' se ve cuánto cuesta la lectura de píxeles y cuánto el codificador.
//
//ACTIVACIÓN (si está apagado no cuesta nada ni ensucia la salida)
//---------------------------------------------------------------
//  · variable de entorno `UMA_LIVE_RECORD_PROFILE=1`, o
//  · fichero `record-profile.enable` en la carpeta de grabaciones (o junto al ejecutable/proyecto), o
//  · `LiveRecordProfile.Enabled = true` desde código.
//
//The output is two files in Recordings/Profiles (never next to the video): `<name>_<yyMMdd_HHmmss>.profile.csv`
//(one row per frame) and `<name>_<yyMMdd_HHmmss>.profile.txt` (summary). See LiveRecordDirector.cs.
//
//CPU MARKER WINDOW
//-----------------
//The recording loop is CPU-bound (render time barely changes from 1080p to 8K), so the profile also
//breaks the frame down by profiler marker: for a few seconds in the middle of the recording (frames
//tagged 'm') a ProfilerRecorder is attached to every timing marker the engine has registered (player
//loop stages, URP cameras and passes, animation, particles, GC...) plus the 'LiveRecord.*' markers
//placed in the live's own update code (see LiveRecordMarkers). The summary lists the most expensive
//ones per frame. It only runs with the profile enabled.
public static class LiveRecordProfile
{
    public const string MarkerFileName = "record-profile.enable";
    private const string EnvVar = "UMA_LIVE_RECORD_PROFILE";
    private const int MaxSamples = 500000;

    //Muestra de UN frame del bucle.
    public struct Sample
    {
        public char phase;      //'p'/'q'/'b' sonda, 'c' captura real
        public int frame;       //índice dentro del tramo
        public double totalMs;  //del inicio de este frame al inicio del siguiente
        public double submitMs; //Update/LateUpdate de la escena + envío de las cámaras
        public double waitMs;   //del final de las cámaras al fin de frame
        public double resolveMs;//reducción del supermuestreo en GPU (8K -> tamaño de salida)
        public double blitMs;   //composición de la letra
        public double readMs;   //ReadPixels (incluye esperar a que la GPU termine)
        public double copyMs;   //copia a un búfer del pool
        public double writeMs;  //espera a FFmpeg (contrapresión del codificador)
        public double gapMs;    //del final de la captura al inicio del frame siguiente
        public int pending;     //frames encolados hacia FFmpeg al escribirse este frame
    }

    public static bool Enabled { get; private set; }
    public static string EnabledBy { get; private set; } = "";

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly List<Sample> Samples = new List<Sample>(8192);
    private static readonly List<string> Notes = new List<string>();
    private static string _folder;
    private static string _name;
    private static bool _dumped;

    public static double NowMs => Clock.Elapsed.TotalMilliseconds;
    public static int Count => Samples.Count;

    //Prepara la salida del perfil. 'folder'/'name' son los del vídeo (los .profile.* van al lado).
    public static void Configure(string folder, string name)
    {
        _folder = folder;
        _name = name;
        Samples.Clear();
        Notes.Clear();
        _dumped = false;
        ResetMarkers();
    }

    private static void ResetMarkers()
    {
        StopMarkers(false);
        _markerStart = -1;
        _markerDone = false;
        _markerFrames = 0;
        _markerReport = null;
    }

    //Decide si el perfil está activo. Se llama una vez al empezar la grabación.
    public static bool Resolve()
    {
        if (Enabled) return true;

        string env = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(env) && env.Trim() != "0")
        {
            Enabled = true;
            EnabledBy = EnvVar + "=" + env.Trim();
            return true;
        }

        foreach (string dir in new[] { LiveRecordSettings.DefaultFolder, Path.Combine(Application.dataPath, "..") })
        {
            try
            {
                string marker = Path.Combine(dir, MarkerFileName);
                if (File.Exists(marker))
                {
                    Enabled = true;
                    EnabledBy = marker;
                    return true;
                }
            }
            catch { }
        }

        return false;
    }

    public static void Reset()
    {
        Enabled = false;
        EnabledBy = "";
        Samples.Clear();
        Notes.Clear();
        _dumped = false;
        ResetMarkers();
    }

    public static void Note(string line)
    {
        Notes.Add(line);
        UnityEngine.Debug.Log("[LiveRecord/Profile] " + line);
    }

    public static void Push(Sample sample)
    {
        if (Samples.Count < MaxSamples) Samples.Add(sample);
    }

    //======================================================================
    // CPU marker window
    //======================================================================

    private sealed class MarkerStat
    {
        public string Name;
        public string Category;
        public ProfilerRecorder Recorder;
        public double SumNs;
        public double MaxNs;
        public int ActiveFrames;
    }

    //Categories left out: loading/IO/network noise and subsystems the live does not use.
    private static readonly HashSet<string> SkippedCategories = new HashSet<string>
    {
        "Loading", "FileIO", "Network", "Build Interface", "Vr", "Virtual Texturing", "Physics2D"
    };

    private const int MarkerReportTop = 50;

    private static int _markerStart = -1;
    private static int _markerLength;
    private static bool _markerRunning;
    private static bool _markerDone;
    private static int _markerFrames;
    private static int _markerFirstFrame;
    private static double _markerSetupMs;
    private static List<MarkerStat> _markers;
    private static string _markerReport;

    //Places the window (2 s of video, 60-240 frames) in the middle of the recording, where the show
    //is usually busier than in the intro. Called when the real recording starts.
    public static void PlanMarkerWindow(int totalFrames, int fps)
    {
        StopMarkers(false);
        _markerDone = false;
        _markerFrames = 0;
        _markerReport = null;
        _markerStart = -1;
        if (!Enabled || totalFrames < 3) return;

        int length = Mathf.Clamp(fps * 2, 60, 240);
        length = Math.Max(1, Math.Min(length, totalFrames / 3));
        _markerLength = length;
        _markerStart = Math.Max(0, (totalFrames - length) / 2);
    }

    public static bool InMarkerWindow(int frame)
    {
        return Enabled && _markerStart >= 0 && _markerRunning &&
               frame >= _markerStart && frame < _markerStart + _markerLength;
    }

    //Called at the start of every recording Update with the index of the frame about to be rendered.
    //The recorders' LastValue then holds the previous (complete) frame.
    public static void TickMarkers(int frame)
    {
        if (!Enabled || _markerStart < 0 || _markerDone) return;

        if (!_markerRunning)
        {
            if (frame >= _markerStart && frame < _markerStart + _markerLength) StartMarkers(frame);
            return;
        }

        if (_markers != null)
        {
            foreach (var stat in _markers)
            {
                double ns = stat.Recorder.LastValue;
                if (ns <= 0) continue;
                stat.SumNs += ns;
                if (ns > stat.MaxNs) stat.MaxNs = ns;
                stat.ActiveFrames++;
            }
        }
        _markerFrames++;

        if (frame >= _markerStart + _markerLength) StopMarkers(true);
    }

    private static void StartMarkers(int frame)
    {
        double t0 = NowMs;
        _markerRunning = true;
        _markerFirstFrame = frame;
        _markerLength = Math.Max(1, _markerStart + _markerLength - frame);
        _markerStart = frame;
        _markers = new List<MarkerStat>(2048);

        try
        {
            LiveRecordMarkers.Touch();   //make sure the live's own markers are registered

            var handles = new List<ProfilerRecorderHandle>(4096);
            ProfilerRecorderHandle.GetAvailable(handles);
            const ProfilerRecorderOptions options = ProfilerRecorderOptions.SumAllSamplesInFrame |
                                                    ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
                                                    ProfilerRecorderOptions.StartImmediately;
            var seen = new HashSet<string>();
            foreach (var handle in handles)
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                if (description.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                string name = description.Name;
                if (string.IsNullOrEmpty(name) || name.StartsWith("Inl_", StringComparison.Ordinal)) continue;
                string category = description.Category.Name;
                if (SkippedCategories.Contains(category)) continue;
                if (!seen.Add(category + "|" + name)) continue;

                var recorder = new ProfilerRecorder(handle, 1, options);
                if (!recorder.Valid) { recorder.Dispose(); continue; }
                _markers.Add(new MarkerStat { Name = name, Category = category, Recorder = recorder });
            }
        }
        catch (Exception e)
        {
            Note("marcadores de CPU: no se pudieron preparar (" + e.Message + ")");
            StopMarkers(false);
            _markerDone = true;
            return;
        }

        _markerSetupMs = NowMs - t0;
        Note(string.Format("marcadores de CPU: {0} grabadores activos desde el frame {1} ({2} frames; preparados en {3:0} ms)",
            _markers.Count, frame, _markerLength, _markerSetupMs));
    }

    //Disposes the recorders. With 'report' the per-marker summary is built for the profile.
    private static void StopMarkers(bool report)
    {
        if (_markers == null)
        {
            _markerRunning = false;
            return;
        }

        foreach (var stat in _markers)
        {
            try { stat.Recorder.Dispose(); } catch { }
        }

        if (report) _markerReport = BuildMarkerReport();
        _markers = null;
        _markerRunning = false;
        _markerDone = true;
    }

    private static string BuildMarkerReport()
    {
        var sb = new StringBuilder();
        int frames = Math.Max(1, _markerFrames);
        sb.AppendLine(string.Format(
            "CPU por marcador (fase 'm': frames {0}-{1}, {2} frames medidos, {3} marcadores; ms por frame):",
            _markerFirstFrame, _markerFirstFrame + _markerFrames - 1, _markerFrames, _markers.Count));
        sb.AppendLine("  · Los marcadores se anidan: un padre incluye a sus hijos (PlayerLoop lo incluye casi todo).");
        sb.AppendLine("  · Los de hilos de trabajo/render se suman entre hilos y pueden superar el tiempo del frame.");
        sb.AppendLine("  · Los grabadores encarecen algo esos frames: por eso van aparte de 'c'.");

        var sorted = new List<MarkerStat>(_markers);
        sorted.Sort((a, b) => b.SumNs.CompareTo(a.SumNs));

        sb.AppendLine("     avg      max  %frames  categoría      marcador");
        int shown = 0;
        foreach (var stat in sorted)
        {
            if (stat.SumNs <= 0 || shown >= MarkerReportTop) break;
            AppendMarkerLine(sb, stat, frames);
            shown++;
        }

        var own = sorted.FindAll(s => s.Name.StartsWith(LiveRecordMarkers.Prefix, StringComparison.Ordinal));
        if (own.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Desglose del código del live (" + LiveRecordMarkers.Prefix + "*):");
            sb.AppendLine("     avg      max  %frames  categoría      marcador");
            foreach (var stat in own) AppendMarkerLine(sb, stat, frames);
        }
        return sb.ToString();
    }

    private static void AppendMarkerLine(StringBuilder sb, MarkerStat stat, int frames)
    {
        sb.AppendLine(string.Format("  {0,6:0.00}  {1,7:0.00}  {2,6:0}%  {3,-13}  {4}",
            stat.SumNs / frames / 1e6, stat.MaxNs / 1e6, 100.0 * stat.ActiveFrames / frames,
            stat.Category.Length > 13 ? stat.Category.Substring(0, 13) : stat.Category, stat.Name));
    }

    //Cuánta memoria de vídeo piden los adjuntos de UN frame (color + profundidad con MSAA) contando la
    //escala de render de URP. Si el total no cabe en la tarjeta, el bucle pasa a depender de la memoria
    //y aparecen errores de URP del tipo "color surface null": eso NO es un límite de hilos, es la
    //configuración de render.
    public static string FootprintLine(int renderWidth, int renderHeight, int msaa, float renderScale)
    {
        float scale = Mathf.Max(0.01f, renderScale);
        int w = Mathf.RoundToInt(renderWidth * scale);
        int h = Mathf.RoundToInt(renderHeight * scale);
        int samples = Mathf.Max(1, msaa);
        long bytesPerTarget = (long)w * h * 4L * samples;
        long total = bytesPerTarget * 2L;   //color + profundidad
        int vramMb = SystemInfo.graphicsMemorySize;
        bool tooBig = vramMb > 0 && total > vramMb * 1024L * 1024L / 2L;
        return string.Format(
            "huella por frame: RT {0}x{1} × escala de render {2:0.##} = {3}x{4} con MSAA x{5} → color + profundidad ≈ {6:0.0} GB (VRAM {7} MB){8}",
            renderWidth, renderHeight, scale, w, h, samples, total / 1073741824.0, vramMb,
            tooBig ? "   ← NO CABE: la VRAM se queda corta" : "");
    }

    //Estado del motor y de los ajustes que pueden limitar el bucle por su cuenta. Se registra al
    //empezar para poder descartar (o confirmar) causas ajenas al grabador.
    public static string EnvironmentLine()
    {
        try
        {
            return string.Format(
                "engine: gpu={0} vram={1}MB cpu={2} editor={3} batch={4} focused={5} screen={6}x{7} " +
                "targetFps={8} vSync={9} captureDelta={10:0.#####} maxQueuedFrames={11} cfgTargetFps={12} cfgUiFps={13}",
                SystemInfo.graphicsDeviceName, SystemInfo.graphicsMemorySize, SystemInfo.processorCount,
                Application.isEditor, Application.isBatchMode, Application.isFocused,
                Screen.width, Screen.height, Application.targetFrameRate, QualitySettings.vSyncCount,
                Time.captureDeltaTime, QualitySettings.maxQueuedFrames,
                Config.Instance != null ? Config.Instance.GetTargetFrameRate() : -1,
                Config.Instance != null ? Config.Instance.GetUIFrameRate() : -1);
        }
        catch (Exception e)
        {
            return "engine: (no se pudo leer) " + e.Message;
        }
    }

    //Resumen en una línea de los últimos frames de una fase (para el cuadro de progreso).
    public static string LiveLine(char phase, int tail)
    {
        if (Samples.Count == 0) return "";
        int from = Math.Max(0, Samples.Count - tail);
        double total = 0, read = 0, write = 0, copy = 0, resolve = 0;
        int count = 0;
        for (int i = from; i < Samples.Count; i++)
        {
            if (Samples[i].phase != phase) continue;
            total += Samples[i].totalMs;
            read += Samples[i].readMs;
            write += Samples[i].writeMs;
            copy += Samples[i].copyMs;
            resolve += Samples[i].resolveMs;
            count++;
        }
        if (count == 0) return "";
        return string.Format("Profile  frame {0:0} ms  (resolve {1:0} · readback {2:0} · copy {3:0} · ffmpeg {4:0})",
            total / count, resolve / count, read / count, copy / count, write / count);
    }

    //Resumen completo: una tabla por fase con media/mediana/p95/mín/máx y el desglose por etapas.
    public static string Summary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("LiveRecord profile  (" + (Enabled ? "activo: " + EnabledBy : "no activo") + ")");
        foreach (string note in Notes) sb.AppendLine(note);
        if (Samples.Count == 0) return sb.AppendLine("(sin muestras)").ToString();

        sb.AppendLine();
        sb.AppendLine("fase  frames    totalMs(avg/p50/p95/min/max)        submit  wait resolv  blit  read  copy  write   gap   pend");
        foreach (char phase in new[] { 'p', 'q', 'b', 'c', 'm' })
        {
            var group = new List<Sample>();
            foreach (var s in Samples) if (s.phase == phase) group.Add(s);
            if (group.Count == 0) continue;

            sb.AppendLine(string.Format("{0,-4}  {1,6}   {2,6:0.0}/{3,6:0.0}/{4,6:0.0}/{5,6:0.0}/{6,6:0.0}   " +
                                        "{7,6:0.0} {8,5:0.0} {9,5:0.0} {10,5:0.0} {11,5:0.0} {12,6:0.0} {13,5:0.0} {14,5:0.0} {15,5:0.0}",
                Describe(phase), group.Count,
                Avg(group, s => s.totalMs), Percentile(group, s => s.totalMs, 0.50),
                Percentile(group, s => s.totalMs, 0.95), Min(group, s => s.totalMs), Max(group, s => s.totalMs),
                Avg(group, s => s.submitMs), Avg(group, s => s.waitMs), Avg(group, s => s.resolveMs),
                Avg(group, s => s.blitMs), Avg(group, s => s.readMs), Avg(group, s => s.copyMs),
                Avg(group, s => s.writeMs), Avg(group, s => s.gapMs), Avg(group, s => s.pending)));
        }

        //Comparativa directa de las sondas con la grabación real: es lo que decide el siguiente paso.
        var p = Group('p');
        var b = Group('b');
        var c = Group('c');
        if (p.Count > 0 && b.Count > 0)
            sb.AppendLine(string.Format("\nlectura de píxeles por frame ≈ {0:0.0} ms ('b' - 'p')",
                Avg(b, s => s.totalMs) - Avg(p, s => s.totalMs)));
        if (p.Count > 0 && c.Count > 0)
            sb.AppendLine(string.Format("coste total de la captura ≈ {0:0.0} ms ('c' - 'p')", Avg(c, s => s.totalMs) - Avg(p, s => s.totalMs)));
        if (b.Count > 0 && c.Count > 0)
            sb.AppendLine(string.Format("codificador/copia ≈ {0:0.0} ms ('c' - 'b')", Avg(c, s => s.totalMs) - Avg(b, s => s.totalMs)));
        if (c.Count > 0)
            sb.AppendLine(string.Format("grabación: {0:0.0} ms/frame → {1:0.0} fps de vídeo muestreado",
                Avg(c, s => s.totalMs), 1000.0 / Math.Max(0.001, Avg(c, s => s.totalMs))));

        if (!string.IsNullOrEmpty(_markerReport))
        {
            sb.AppendLine();
            sb.Append(_markerReport);
        }
        return sb.ToString();
    }

    //Escribe el CSV (una fila por frame) y el resumen junto al vídeo. Se puede llamar varias veces.
    public static void Dump()
    {
        if (!Enabled || _dumped || string.IsNullOrEmpty(_folder) || string.IsNullOrEmpty(_name)) return;
        _dumped = true;
        //Recording cut short inside the marker window: report what was measured so far.
        if (_markerRunning) StopMarkers(true);
        try
        {
            Directory.CreateDirectory(_folder);

            var csv = new StringBuilder();
            csv.AppendLine("phase,frame,totalMs,submitMs,waitMs,resolveMs,blitMs,readMs,copyMs,writeMs,gapMs,pending");
            foreach (var s in Samples)
                csv.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0},{1},{2:0.###},{3:0.###},{4:0.###},{5:0.###},{6:0.###},{7:0.###},{8:0.###},{9:0.###},{10:0.###},{11}",
                    s.phase, s.frame, s.totalMs, s.submitMs, s.waitMs, s.resolveMs, s.blitMs, s.readMs, s.copyMs,
                    s.writeMs, s.gapMs, s.pending));
            File.WriteAllText(Path.Combine(_folder, _name + ".profile.csv"), csv.ToString());

            string summary = Summary();
            File.WriteAllText(Path.Combine(_folder, _name + ".profile.txt"), summary);
            UnityEngine.Debug.Log("[LiveRecord/Profile] Resumen escrito en " + Path.Combine(_folder, _name + ".profile.txt") + "\n" + summary);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("[LiveRecord/Profile] No se pudo escribir el perfil: " + e.Message);
        }
    }

    //======================================================================
    // Agregación
    //======================================================================

    private static string Describe(char phase)
    {
        switch (phase)
        {
            case 'p': return "p-render";
            case 'q': return "q-drain";
            case 'b': return "b-read";
            case 'c': return "c-capture";
            case 'm': return "m-markers";
            default: return phase.ToString();
        }
    }

    private static List<Sample> Group(char phase)
    {
        var group = new List<Sample>();
        foreach (var s in Samples) if (s.phase == phase) group.Add(s);
        return group;
    }

    private static double Avg(List<Sample> group, Func<Sample, double> pick)
    {
        if (group.Count == 0) return 0.0;
        double sum = 0;
        foreach (var s in group) sum += pick(s);
        return sum / group.Count;
    }

    private static double Min(List<Sample> group, Func<Sample, double> pick)
    {
        if (group.Count == 0) return 0.0;
        double best = double.MaxValue;
        foreach (var s in group) best = Math.Min(best, pick(s));
        return best;
    }

    private static double Max(List<Sample> group, Func<Sample, double> pick)
    {
        if (group.Count == 0) return 0.0;
        double best = double.MinValue;
        foreach (var s in group) best = Math.Max(best, pick(s));
        return best;
    }

    private static double Percentile(List<Sample> group, Func<Sample, double> pick, double fraction)
    {
        if (group.Count == 0) return 0.0;
        var values = new List<double>(group.Count);
        foreach (var s in group) values.Add(pick(s));
        values.Sort();
        int index = (int)Math.Round((values.Count - 1) * fraction);
        return values[Math.Max(0, Math.Min(values.Count - 1, index))];
    }
}

//(Live recording · diagnostics) Profiler markers around the live's own per-frame work, so the CPU
//marker window of LiveRecordProfile can tell which part of the show update costs what. A ProfilerMarker
//costs next to nothing when nobody records it, so they stay in the code permanently; they also show
//up in Unity's Profiler window.
public static class LiveRecordMarkers
{
    public const string Prefix = "LiveRecord.";

    //Director.Update / LateUpdate
    public static readonly ProfilerMarker TimelineUpdate = New("Timeline.AlterUpdate");
    public static readonly ProfilerMarker AudioUpdate = New("Audio.AlterUpdate");
    public static readonly ProfilerMarker TimelineLateUpdate = New("Timeline.AlterLateUpdate");
    public static readonly ProfilerMarker Props = New("Props.Evaluate");
    public static readonly ProfilerMarker MiniChara = New("MiniChara.Evaluate");
    public static readonly ProfilerMarker FootLight = New("FootLight.AlterLateUpdate");
    public static readonly ProfilerMarker Spotlights = New("Spotlight.AlterLateUpdate");
    public static readonly ProfilerMarker StageFlares = New("StageFlares.AlterUpdate");
    public static readonly ProfilerMarker StageFovShift = New("Stage.ApplyFovShifts");
    public static readonly ProfilerMarker VolumeLight = New("VolumeLight.SunShafts");
    public static readonly ProfilerMarker DofFocal = New("Dof.UpdateMaxFocalSize");
    public static readonly ProfilerMarker Lasers = New("Stage.AlterUpdateLasers");
    public static readonly ProfilerMarker ModelRenderQueue = New("ModelRenderQueue.Update");
    public static readonly ProfilerMarker MirrorReflections = New("Mirror.UpdateReflections");
    public static readonly ProfilerMarker RuntimeDiagnostics = New("Diagnostics.RecordFrame");

    //UmaContainerCharacter.LateUpdate
    public static readonly ProfilerMarker CharacterLateUpdate = New("Character.LateUpdate");
    public static readonly ProfilerMarker CharacterCySpring = New("Character.CySpring");
    public static readonly ProfilerMarker CharacterPostRuntime = New("Character.PostRuntime");

    private static ProfilerMarker New(string name)
    {
        return new ProfilerMarker(ProfilerCategory.Scripts, Prefix + name);
    }

    //Explicit static constructor (no 'beforefieldinit'): calling Touch() is then guaranteed to create
    //and register every marker before the recorders are attached.
    static LiveRecordMarkers() { }
    public static void Touch() { }
}
