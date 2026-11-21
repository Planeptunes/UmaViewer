using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

//(Probe capture) The viewer's "Momentum" capture: ONE frame of the live, written as a folder whose files and columns
//follow a fixed, documented reference capture format, for comparison with reference data (a folder-to-folder diff
//lists the differences).
//
//Use: pause the live at the frame to inspect (the same frame as the reference data), then press the "Probe capture"
//button (shown with the frame overlay, Ctrl+Shift+F9) or Ctrl+Shift+F10. Output:
//<project or exe folder>/LiveTrace/momentum_<n>_live<id>_r1_f<frame>/
//   render_<track>.csv   timeline values of one tick (LiveTraceExporter + LiveTraceExporter.Capture.cs)
//   missing_fields.csv   game fields the viewer's UpdateInfo does not have (or has under another name)
//   scene_nodes.csv / scene_changes.csv   every node under the Director: active + local transform
//   materials.csv        per renderer: material values (P) and MaterialPropertyBlock values (B) of the property catalog
//   camera_trace.csv     every enabled camera: depth, fov, aspect, rect, world position / rotation, culling mask
//   globals.csv          global shader values read back by name (Shader.GetGlobal*), kind Read<Type>
//   settings.txt         Time / Screen / RenderSettings / QualitySettings values (labels of the reference format)
//   capture.png          the screen
//   manifest.txt         frame, time, rows per track, tracks the viewer does not apply
//Read-only: it reads the scene and the shader state, it changes nothing that is rendered.
public class LiveProbeCapture : MonoBehaviour
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const string BuiltinFontName = "LegacyRuntime.ttf";
    private const int MaxDepth = 24;
    private const int MaxNodes = 60000;

    public static LiveProbeCapture Instance;

    private GameObject _panel;
    private Text _status;
    private bool _running;
    private string _statusText = "";
    private float _statusUntil;

    //Created by LiveViewerUI.Awake, like the frame overlay. Idempotent.
    public static void Ensure()
    {
        if (Instance != null) return;
        try
        {
            var go = new GameObject("LiveProbeCapture");
            Scene liveScene = SceneManager.GetSceneByName("LiveScene");
            if (liveScene.IsValid() && liveScene.isLoaded) SceneManager.MoveGameObjectToScene(go, liveScene);
            Instance = go.AddComponent<LiveProbeCapture>();
            Instance.Build();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LiveProbeCapture] Could not create the capture button: " + ex.Message);
        }
    }

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30001;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        //Below the frame overlay (top right, 480x232 at -16,-16).
        _panel = new GameObject("ProbeCapturePanel", typeof(RectTransform));
        _panel.transform.SetParent(transform, false);
        var pr = _panel.GetComponent<RectTransform>();
        pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(1f, 1f);
        pr.anchoredPosition = new Vector2(-16f, -256f);
        pr.sizeDelta = new Vector2(480f, 96f);

        //Two buttons side by side (240 px each): Momentum (one frame, Ctrl+Shift+F10) and Motion (stepped frames, Ctrl+Shift+F11).
        MakeButton("Momentum Probe (Ctrl+Shift+F10)", 0f, new Color(0.15f, 0.35f, 0.6f, 0.9f), StartCapture);
        MakeButton("Motion Probe (Ctrl+Shift+F11)", 244f, new Color(0.45f, 0.25f, 0.6f, 0.9f), StartMotion);

        var statusGo = new GameObject("Status", typeof(RectTransform));
        statusGo.transform.SetParent(_panel.transform, false);
        var sr = statusGo.GetComponent<RectTransform>();
        sr.anchorMin = new Vector2(0f, 0f); sr.anchorMax = new Vector2(1f, 0f); sr.pivot = new Vector2(1f, 0f);
        sr.anchoredPosition = Vector2.zero; sr.sizeDelta = new Vector2(0f, 46f);
        _status = MakeText(statusGo.transform, "", TextAnchor.UpperRight, Vector2.zero, Vector2.one);
        _status.fontSize = 16;
        _panel.SetActive(false);
    }

    private void MakeButton(string label, float x, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(_panel.transform, false);
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2(x, 0f);
        r.sizeDelta = new Vector2(236f, 44f);
        go.GetComponent<Image>().color = color;
        go.GetComponent<Button>().onClick.AddListener(onClick);
        MakeText(go.transform, label, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one).fontSize = 16;
    }

    private static Text MakeText(Transform parent, string text, TextAnchor anchor, Vector2 min, Vector2 max)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = min; r.anchorMax = max; r.offsetMin = new Vector2(8f, 2f); r.offsetMax = new Vector2(-8f, -2f);
        var t = go.GetComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
        t.fontSize = 20;
        t.alignment = anchor;
        t.color = new Color(0.95f, 1f, 0.85f, 1f);
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.text = text;
        return t;
    }

    private void Update()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (ctrl && shift && Input.GetKeyDown(KeyCode.F10)) StartCapture();
        if (ctrl && shift && Input.GetKeyDown(KeyCode.F11)) StartMotion();

        //Shown with the frame overlay (a debug tool), or while a status message is up.
        bool overlayShown = LiveFrameDebugOverlay.Instance != null && LiveFrameDebugOverlay.Instance.transform.childCount > 0 &&
                            LiveFrameDebugOverlay.Instance.transform.GetChild(0).gameObject.activeSelf;
        bool show = !UmaSceneController.IsTransitionActive && (overlayShown || Time.unscaledTime < _statusUntil || _running || LiveProbeMotion.Running);
        if (_panel != null && _panel.activeSelf != show) _panel.SetActive(show);
        if (_status != null) _status.text = _statusText;
    }

    private void SetStatus(string text, float seconds = 8f)
    {
        _statusText = text;
        _statusUntil = Time.unscaledTime + seconds;
        Debug.Log("[LiveProbeCapture] " + text);
    }

    public void StartCapture()
    {
        if (_running) return;
        if (LiveProbeMotion.Running) { SetStatus("A Motion capture is in progress."); return; }
        if (!LiveTraceExporter.CaptureAvailable) { SetStatus("No live is running."); return; }
        if (!LiveTraceExporter.ArmCapture()) { SetStatus("A capture is already in progress."); return; }
        LiveProbeTraces.CySpringArm();
        StartCoroutine(Run());
    }

    //(Motion Probe, viewer tool) Ctrl+Shift+F11 / button: starts a stepped capture of the paused show, or cancels a running one.
    public void StartMotion()
    {
        if (LiveProbeMotion.Running)
        {
            LiveProbeMotion.RequestCancel();
            SetStatus("Motion cancelled");
            return;
        }
        if (_running) { SetStatus("A Momentum capture is in progress."); return; }
        if (!LiveTraceExporter.CaptureAvailable) { SetStatus("No live is running."); return; }
        if (!Gallop.Live.Director.IsPlaybackPaused) { SetStatus("Pause the live first (the Motion Probe steps a paused show)."); return; }
        StartCoroutine(LiveProbeMotion.Run(s => SetStatus(s, 30f)));
    }

    private IEnumerator Run()
    {
        _running = true;
        SetStatus("Capturing...", 30f);
        int waited = 0;
        while (!LiveTraceExporter.CaptureReady && waited < 240) { waited++; yield return null; }
        if (!LiveTraceExporter.CaptureReady)
        {
            SetStatus("No timeline update happened (is the live loaded?). Nothing written.");
            LiveProbeTraces.CySpringDisarm();
            _running = false;
            yield break;
        }
        yield return new WaitForEndOfFrame();
        string folder = null;
        try { folder = Write(); SetStatus("Saved: " + folder, 15f); }
        catch (Exception ex) { SetStatus("Capture failed: " + ex.Message); Debug.LogException(ex); }
        LiveTraceExporter.ClearCapture();
        LiveProbeTraces.CySpringDisarm();
        _running = false;
    }

    // ---- output ----

    private static string F5(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "nan" : v.ToString("F5", Inv);
    private static string F6(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "" : v.ToString("F6", Inv);
    private static string V4(float a, float b, float c, float d) => "(" + F5(a) + "," + F5(b) + "," + F5(c) + "," + F5(d) + ")";
    private static string Q(string s) => "\"" + (s ?? "").Replace('"', '\'') + "\"";
    private static string Name(UnityEngine.Object o) => o == null ? "null" : o.name;

    private string Write()
    {
        float time = LiveTraceExporter.CaptureTime;
        int frame = Mathf.FloorToInt(time * 60f + 0.001f);
        int live = LiveTraceExporter.CaptureLiveId;
        string baseDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "LiveTrace"));
        Directory.CreateDirectory(baseDir);
        int n = Directory.GetDirectories(baseDir, "momentum_*").Length + 1;
        string dir = Path.Combine(baseDir, string.Format(Inv, "momentum_{0:000}_live{1}_r1_f{2}", n, live, frame));
        Directory.CreateDirectory(dir);
        var notes = new List<string>();

        ScreenCapture.CaptureScreenshot(Path.Combine(dir, "capture.png"));
        notes.Add("[image] capture.png requested (written by Unity at the end of the frame)");

        // Timeline tracks.
        foreach (var pair in LiveTraceExporter.CapturedRows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(LiveTraceExporter.CaptureFullHeader(pair.Key));
            foreach (var row in pair.Value) sb.AppendLine(row);
            File.WriteAllText(Path.Combine(dir, "render_" + pair.Key + ".csv"), sb.ToString(), new UTF8Encoding(false));
            notes.Add("[tracks] " + pair.Key + ": " + pair.Value.Count + " rows");
        }
        foreach (string t in LiveTraceExporter.CapturedNotApplied) notes.Add("[tracks] not applied by the viewer (no subscriber): " + t);
        {
            var sb = new StringBuilder("track,column,reason\n");
            foreach (var pair in LiveTraceExporter.CapturedMissing)
            {
                int dot = pair.Key.IndexOf('.');
                sb.Append(pair.Key.Substring(0, dot)).Append(',').Append(pair.Key.Substring(dot + 1)).Append(',').Append(Q(pair.Value)).Append('\n');
            }
            File.WriteAllText(Path.Combine(dir, "missing_fields.csv"), sb.ToString(), new UTF8Encoding(false));
            notes.Add("[tracks] missing_fields.csv: " + LiveTraceExporter.CapturedMissing.Count + " game fields not found as such in the viewer");
        }

        notes.Add(WriteScene(dir, live, frame));
        notes.Add(WriteCameras(dir, live, frame));
        notes.Add(LiveProbeTraces.WriteStateTrace(dir, live, frame, LiveTraceExporter.CaptureRootTransform));
        notes.Add(LiveProbeTraces.WriteMonitorTrace(dir, live, frame, LiveTraceExporter.CaptureRootTransform));
        notes.Add(LiveProbeTraces.WriteParticleTrace(dir, live, frame, LiveTraceExporter.CaptureRootTransform));
        notes.Add(LiveProbeTraces.WriteCySpring(dir, frame));
        notes.Add(WriteGlobals(dir, frame));
        WriteSettings(dir, n, live, frame, time);
        notes.Add("[globals] settings.txt written");

        var m = new StringBuilder();
        m.AppendLine("viewer probe capture " + n);
        m.AppendLine("live = " + live);
        m.AppendLine("run = 1");
        m.AppendLine("frame = " + frame);
        m.AppendLine("time = " + time.ToString("F4", Inv));
        m.AppendLine();
        foreach (string s in notes) m.AppendLine(s);
        File.WriteAllText(Path.Combine(dir, "manifest.txt"), m.ToString(), new UTF8Encoding(false));
        return dir;
    }

    // scene_nodes.csv / scene_changes.csv / materials.csv: path rules of the reference format ("#N" for
    // siblings with the same name, root first) and its columns.
    private static void Collect(Transform t, string parentPath, int depth, Dictionary<string, int> seen, List<KeyValuePair<Transform, string>> output)
    {
        if (t == null || depth > MaxDepth || output.Count >= MaxNodes) return;
        string baseName = parentPath.Length == 0 ? t.name : parentPath + "/" + t.name;
        seen.TryGetValue(baseName, out int count);
        seen[baseName] = count + 1;
        string path = count > 0 ? baseName + "#" + count : baseName;
        output.Add(new KeyValuePair<Transform, string>(t, path));
        for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i), path, depth + 1, seen, output);
    }

    private static string MatValue(Material mat, string name, string type)
    {
        if (!mat.HasProperty(name)) return null;
        switch (type)
        {
            case "Color": { Color c = mat.GetColor(name); return V4(c.r, c.g, c.b, c.a); }
            case "Vector": { Vector4 v = mat.GetVector(name); return V4(v.x, v.y, v.z, v.w); }
            case "Int": return ((int)mat.GetFloat(name)).ToString(Inv);
            case "Tex": return Name(mat.GetTexture(name));
            case "Float": return F5(mat.GetFloat(name));
            default: return null;
        }
    }

    private static string BlockValue(MaterialPropertyBlock b, string name, string type)
    {
        if (!b.HasProperty(name)) return null;
        switch (type)
        {
            case "Color": { Color c = b.GetColor(name); return "(" + F6(c.r) + "," + F6(c.g) + "," + F6(c.b) + "," + F6(c.a) + ")"; }
            case "Vector": { Vector4 v = b.GetVector(name); return "(" + F6(v.x) + "," + F6(v.y) + "," + F6(v.z) + "," + F6(v.w) + ")"; }
            case "Float": case "Int": return F6(b.GetFloat(name));
            case "Tex": { var t = b.GetTexture(name); return t == null ? null : t.name; }
            default: return null;
        }
    }

    private static string WriteScene(string dir, int live, int frame)
    {
        Transform root = LiveTraceExporter.CaptureRootTransform;
        if (root == null) return "[scene] skipped: no Director root";
        var list = new List<KeyValuePair<Transform, string>>();
        Collect(root, "", 0, new Dictionary<string, int>(), list);
        var nodes = new StringBuilder("id,path\n");
        var changes = new StringBuilder("live,run,frame,id,active,lposX,lposY,lposZ,lrotX,lrotY,lrotZ,lrotW,lscaleX,lscaleY,lscaleZ\n");
        var mats = new StringBuilder("path,renderer,enabled,slot,material,shader,queue,source,prop,type,value\n");
        var block = new MaterialPropertyBlock();
        string[,] catalog = LiveProbeSpecs.Catalog;
        int renderers = 0, values = 0;
        for (int id = 0; id < list.Count; id++)
        {
            Transform t = list[id].Key;
            string path = list[id].Value;
            nodes.Append(id).Append(',').Append(Q(path)).Append('\n');
            if (t == null) continue;
            Vector3 p = t.localPosition; Quaternion r = t.localRotation; Vector3 s = t.localScale;
            changes.Append(live).Append(",1,").Append(frame).Append(',').Append(id).Append(',').Append(t.gameObject.activeSelf ? 1 : 0)
                .Append(',').Append(F5(p.x)).Append(',').Append(F5(p.y)).Append(',').Append(F5(p.z))
                .Append(',').Append(F5(r.x)).Append(',').Append(F5(r.y)).Append(',').Append(F5(r.z)).Append(',').Append(F5(r.w))
                .Append(',').Append(F5(s.x)).Append(',').Append(F5(s.y)).Append(',').Append(F5(s.z)).Append('\n');
            foreach (Renderer rend in t.GetComponents<Renderer>())
            {
                if (rend == null) continue;
                renderers++;
                string head = Q(path) + "," + rend.GetType().Name + "," + (rend.enabled ? 1 : 0);
                Material[] shared = rend.sharedMaterials;
                for (int slot = 0; slot < shared.Length; slot++)
                {
                    Material mat = shared[slot];
                    if (mat == null) continue;
                    string mhead = head + "," + slot + "," + Q(mat.name) + "," + Q(mat.shader != null ? mat.shader.name : "null") + "," + mat.renderQueue;
                    mats.Append(mhead).Append(",M,,,\n");
                    for (int i = 0; i < catalog.GetLength(0); i++)
                    {
                        string v = MatValue(mat, catalog[i, 0], catalog[i, 1]);
                        if (v == null) continue;
                        mats.Append(mhead).Append(",P,").Append(catalog[i, 0]).Append(',').Append(catalog[i, 1]).Append(',').Append(Q(v)).Append('\n');
                        values++;
                    }
                }
                if (!rend.HasPropertyBlock()) continue;
                rend.GetPropertyBlock(block);
                for (int i = 0; i < catalog.GetLength(0); i++)
                {
                    string v = BlockValue(block, catalog[i, 0], catalog[i, 1]);
                    if (v == null) continue;
                    mats.Append(head).Append(",-1,,,,B,").Append(catalog[i, 0]).Append(',').Append(catalog[i, 1]).Append(',').Append(Q(v)).Append('\n');
                    values++;
                }
            }
        }
        File.WriteAllText(Path.Combine(dir, "scene_nodes.csv"), nodes.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "scene_changes.csv"), changes.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "materials.csv"), mats.ToString(), new UTF8Encoding(false));
        return "[scene] scene_nodes.csv + scene_changes.csv (" + list.Count + " nodes), materials.csv (" + renderers + " renderers, " + values + " values)";
    }

    // camera_trace.csv: header and camera rows of the reference format.
    internal static string WriteCameras(string dir, int live, int frame)
    {
        var sb = new StringBuilder("live,run,frame,kind,name,depth,enabled,fov,aspect,rectX,rectY,rectW,rectH,posX,posY,posZ,rotX,rotY,rotZ,rotW,cullingMask,extra\n");
        int count = 0;
        foreach (Camera cam in Camera.allCameras)
        {
            if (cam == null) continue;
            Rect r = cam.rect;
            Vector3 p = cam.transform.position;
            Quaternion q = cam.transform.rotation;
            sb.Append(live).Append(",1,").Append(frame).Append(",camera,").Append(Q(cam.transform.name)).Append(',')
              .Append(F5(cam.depth)).Append(',').Append(cam.enabled ? 1 : 0).Append(',').Append(F5(cam.fieldOfView)).Append(',').Append(F5(cam.aspect))
              .Append(',').Append(F5(r.x)).Append(',').Append(F5(r.y)).Append(',').Append(F5(r.width)).Append(',').Append(F5(r.height))
              .Append(',').Append(F5(p.x)).Append(',').Append(F5(p.y)).Append(',').Append(F5(p.z))
              .Append(',').Append(LiveProbeTraces.QuatCols(q))
              .Append(",0x").Append(cam.cullingMask.ToString("x", Inv)).Append(",\n");
            count++;
        }
        File.WriteAllText(Path.Combine(dir, "camera_trace.csv"), sb.ToString(), new UTF8Encoding(false));
        return "[cameras] camera_trace.csv (" + count + " enabled cameras)";
    }

    // globals.csv: the Read<Type> rows of the reference format (Shader.GetGlobal* by name, LiveProbeSpecs.Globals). The
    // reference format also has SET rows (Float/Vector/Cmd*), which the viewer cannot list.
    private static string WriteGlobals(string dir, int frame)
    {
        var sb = new StringBuilder("frame,kind,name,value\n");
        string[,] g = LiveProbeSpecs.Globals;
        for (int i = 0; i < g.GetLength(0); i++)
        {
            string type = g[i, 0], name = g[i, 1];
            string v;
            switch (type)
            {
                case "Float": case "Int": v = F5(Shader.GetGlobalFloat(name)); break;
                case "Vector": case "Color": { Vector4 x = Shader.GetGlobalVector(name); v = V4(x.x, x.y, x.z, x.w); break; }
                case "Tex": v = Name(Shader.GetGlobalTexture(name)); break;
                default: continue;
            }
            sb.Append(frame).Append(",Read").Append(type).Append(',').Append(name).Append(',').Append(Q(v)).Append('\n');
        }
        File.WriteAllText(Path.Combine(dir, "globals.csv"), sb.ToString(), new UTF8Encoding(false));
        return "[globals] globals.csv (" + g.GetLength(0) + " names read back)";
    }

    private static void WriteSettings(string dir, int n, int live, int frame, float time)
    {
        string C(Color c) => "(" + F6(c.r) + "," + F6(c.g) + "," + F6(c.b) + "," + F6(c.a) + ")";
        var sb = new StringBuilder();
        sb.AppendLine("viewer capture = " + n);
        sb.AppendLine("live = " + live);
        sb.AppendLine("run = 1");
        sb.AppendLine("frame = " + frame);
        sb.AppendLine("time = " + time.ToString("F4", Inv));
        void L(string label, string value) => sb.Append(label).Append(" = ").Append(value).Append('\n');
        L("Time.timeScale", F5(Time.timeScale));
        L("Time.frameCount", Time.frameCount.ToString(Inv));
        L("Time.deltaTime", F5(Time.deltaTime));
        L("Time.fixedDeltaTime", F5(Time.fixedDeltaTime));
        L("Screen.width", Screen.width.ToString(Inv));
        L("Screen.height", Screen.height.ToString(Inv));
        L("RenderSettings.fog", RenderSettings.fog ? "1" : "0");
        L("RenderSettings.fogMode", ((int)RenderSettings.fogMode).ToString(Inv));
        L("RenderSettings.fogColor", C(RenderSettings.fogColor));
        L("RenderSettings.fogDensity", F5(RenderSettings.fogDensity));
        L("RenderSettings.fogStartDistance", F5(RenderSettings.fogStartDistance));
        L("RenderSettings.fogEndDistance", F5(RenderSettings.fogEndDistance));
        L("RenderSettings.ambientMode", ((int)RenderSettings.ambientMode).ToString(Inv));
        L("RenderSettings.ambientSkyColor", C(RenderSettings.ambientSkyColor));
        L("RenderSettings.ambientEquatorColor", C(RenderSettings.ambientEquatorColor));
        L("RenderSettings.ambientGroundColor", C(RenderSettings.ambientGroundColor));
        L("RenderSettings.ambientLight", C(RenderSettings.ambientLight));
        L("RenderSettings.ambientIntensity", F5(RenderSettings.ambientIntensity));
        L("RenderSettings.subtractiveShadowColor", C(RenderSettings.subtractiveShadowColor));
        L("RenderSettings.skybox", Name(RenderSettings.skybox));
        L("RenderSettings.sun", Name(RenderSettings.sun));
        L("RenderSettings.reflectionIntensity", F5(RenderSettings.reflectionIntensity));
        L("RenderSettings.reflectionBounces", RenderSettings.reflectionBounces.ToString(Inv));
        L("RenderSettings.defaultReflectionMode", ((int)RenderSettings.defaultReflectionMode).ToString(Inv));
        L("RenderSettings.defaultReflectionResolution", RenderSettings.defaultReflectionResolution.ToString(Inv));
        L("RenderSettings.haloStrength", F5(RenderSettings.haloStrength));
        L("RenderSettings.flareStrength", F5(RenderSettings.flareStrength));
        L("RenderSettings.flareFadeSpeed", F5(RenderSettings.flareFadeSpeed));
        L("QualitySettings.pixelLightCount", QualitySettings.pixelLightCount.ToString(Inv));
        L("QualitySettings.shadowDistance", F5(QualitySettings.shadowDistance));
        L("QualitySettings.antiAliasing", QualitySettings.antiAliasing.ToString(Inv));
        L("QualitySettings.vSyncCount", QualitySettings.vSyncCount.ToString(Inv));
        L("QualitySettings.activeColorSpace", ((int)QualitySettings.activeColorSpace).ToString(Inv));
        L("QualitySettings.lodBias", F5(QualitySettings.lodBias));
        L("QualitySettings.anisotropicFiltering", ((int)QualitySettings.anisotropicFiltering).ToString(Inv));
        File.WriteAllText(Path.Combine(dir, "settings.txt"), sb.ToString(), new UTF8Encoding(false));
    }

    private void OnDestroy()
    {
        LiveProbeMotion.Abort();
        if (Instance == this) Instance = null;
    }
}
