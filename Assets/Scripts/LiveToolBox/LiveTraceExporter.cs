using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Gallop.Live.Cutt;
using UnityEngine;

//(Live trace export) Writes the viewer's own timeline parameters, frame by frame, in a fixed, documented CSV format
//(tracks render_<name>.csv), for comparison with reference data.
//Comparing a file with the reference by `frame` shows which timeline value differs from the viewer's.
//
//How it works: it only SUBSCRIBES to the events LiveTimelineControl already raises (the same ones the Director
//binds to apply the values), so it never changes what the viewer renders. It is off unless Config.LiveTraceExport
//is true (Config.json). Files go to <project or exe folder>/LiveTrace/<timestamp>_live<id>/.
//
//Columns: t,frame,time,sheet,cam,liveId,<track columns>. `frame` = floor(time * 60), the game's timeline frame;
//`time` = Director._liveCurrentTime; `sheet` is always 0 (the viewer does not track it). The track columns are
//the ones of the reference format (struct layouts from dump_jp.cs). A field the viewer does not keep is written EMPTY so the
//columns stay aligned (see post_film below).
//exposure: the game's ExposureUpdateInfo is `IsEnable, ExposureParameter (Vector4), DepthMask`; the viewer's port is
//`Gain, Lift, MaskGain, MaskLift`. They are written as expX..expW in that order (an ASSUMED mapping; compare the
//values to confirm it).
//
//The one-frame Momentum capture (button / Ctrl+Shift+F10) lives in
//LiveTraceExporter.Capture.cs and LiveProbeCapture.cs: it reuses these handlers for one timeline tick.
public static partial class LiveTraceExporter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static LiveTimelineControl _ltc;
    private static Func<float> _time;
    private static Func<int> _camera;
    private static int _liveId;
    private static double _t0;
    private static string _folder;
    private static readonly Dictionary<string, StreamWriter> Writers = new Dictionary<string, StreamWriter>();

    //Track headers of the reference format. Keep in sync when the reference layouts change.
    private static readonly Dictionary<string, string> Headers = new Dictionary<string, string>
    {
        { "global_light", "rotX,rotY,rotZ,rotW,globalRimShadowRate,rimR,rimG,rimB,rimA,rimStep,rimFeather,rimSpecRate,flags,RimHorizonOffset,RimVerticalOffset,RimHorizonOffset2,RimVerticalOffset2,rim2R,rim2G,rim2B,rim2A,rimStep2,rimFeather2,rimSpecRate2,globalRimShadowRate2" },
        { "foot_light", "charIdx,hightMax,colR,colG,colB,colA,blendMode,easing" },
        { "facial_toon", "charPos,cheekPret,nosePret,cylBlend,hairNormalBlend,useOriginalDirLight,dirX,dirY,dirZ,eyeToonStep,eyeToonFeather,eyeSaturation" },
        { "spotlight3d", "idx,charFlag,isActive,isBillboard,enableLookAt,positionType,colR,colG,colB,colA,colorPower,localHeight,posX,posY,posZ,rotX,rotY,rotZ,scaleX,scaleY,scaleZ" },
        { "exposure", "isEnable,expX,expY,expZ,expW,depthMask" },
        { "tone_curve", "isEnable,depthMask,minR,minG,minB,minA,maxR,maxG,maxB,maxA,maskMinR,maskMinG,maskMinB,maskMinA,maskMaxR,maskMaxG,maskMaxB,maskMaxA" },
        { "bloom_diffusion", "isEnabledBloom,bloomDofWeight,threshold,intensity,BloomBlurSize,BloomBlendMode,isEnabledDiffusion,diffusionBlurSize,diffusionBright,diffusionThreshold,diffusionSaturation,diffusionContrast" },
        { "additional_light", "idx,posX,posY,posZ,rotX,rotY,rotZ,isEnable,type,range,spotAngle,indirectMult,shadowType,strength,bias,normalBias,nearPlane" },
        { "additional_light_count", "count" },
        { "lens_flare", "hash,offX,offY,offZ,colR,colG,colB,colA,brightness,fadeSpeed,enable,enableParameter,isAutoBright,isOverridePos,isScreenFit" },
        { "mirror_reflection", "hash,baseCamType,baseCamIdx,enableMirror,enableBgLayer,enable3dLayer,isToonMirror,mirrorRate,targetChara,enableCharaHead,targetCharaHead" },
        { "dof", "isEnableDOF,forcalSize,blurSpread,focalPosX,focalPosY,focalPosZ,dofQuality,dofBlurType,dofForegroundSize,dofFocalPoint,dofSmoothness,isUseFocalPoint,BallBlurCurveFactor,BallBlurBrightnessThreshhold,BallBlurBrightnessIntensity,BallBlurSpread,IsPointBallBlur" },
        { "global_fog", "isDistance,startDistance,isHeight,height,heightDensity,colR,colG,colB,colA,fogMode,expDensity,start,end,useRadialDistance" },
        { "post_film", "filmMode,colorType,filmPower,filmOffX,filmOffY,filmOptX,filmOptY,filmOptZ,filmOptW,c0r,c0g,c0b,c0a,c1r,c1g,c1b,c1a,c2r,c2g,c2b,c2a,c3r,c3g,c3b,c3a,depthPower,DepthClip,layerMode,colorBlend,inverseVignette,colorBlendFactor,movieResId,movieFrameOffset,movieTime,movieReverse,RollAngle,FilmScaleX,FilmScaleY" },
    };

    //Header (after the context columns) of any track, the hand-written ones above or the table-driven ones of the capture.
    //post_film2 / post_film3 use the generated header (named from dump_jp.cs); their values are in
    //the same order as post_film's.
    private static string HeaderOf(string track)
    {
        if (Headers.TryGetValue(track, out string h)) return h;
        return CaptureHeader(track);
    }

    public static bool Enabled => Config.Instance != null && Config.Instance.LiveTraceExport;

    // ---- scene transform log (reference format group I: scene_nodes.csv + scene_changes.csv) ----
    //Every node under the Director root: active state + local position / rotation / scale, written only when it
    //changes (epsilon 1e-4), every Config.LiveTraceTransformStep frames (default 30). Node ids are stable per path;
    //sibling objects with the same name get a "#N" suffix, as in the reference format. The node path starts at the
    //Director root, so a comparison ignores its first component (the game's is "Director(Clone)").
    private const int MaxDepth = 24;
    private const int MaxNodes = 60000;
    private const float Epsilon = 1e-4f;
    private static Transform _root;
    private static int _step = 30;
    private static int _builtFrame = int.MinValue;
    private static int _hierarchyCount;   // nodes under the Director root at the last refresh (rebuild trigger)
    private static int _lastSampleFrame = int.MinValue;
    private static int _listed;
    private static readonly Dictionary<string, int> NodeIds = new Dictionary<string, int>();
    private static readonly List<string> NodeNames = new List<string>();
    private static readonly List<float[]> NodeLast = new List<float[]>();
    private static readonly List<KeyValuePair<Transform, int>> Nodes = new List<KeyValuePair<Transform, int>>();
    private static StreamWriter _nodesWriter;
    private static StreamWriter _changesWriter;

    // ---- blend shapes / animators / particle systems (reference format group K) ----
    //scene_morphs.csv: live,run,frame,id,shape,weight (one row per blend shape whose weight changed);
    //scene_animators.csv: ...,stateNameHash,fullPathHash,normalizedTime,length,speed,speedMultiplier,tag,loop;
    //scene_particles.csv: ...,isPlaying,time,particleCount. `id` is the node id of scene_nodes.csv. The viewer's
    //animator controllers differ from the game's, so the state hashes are informational; compare normalizedTime.
    private static readonly List<KeyValuePair<int, SkinnedMeshRenderer>> Smrs = new List<KeyValuePair<int, SkinnedMeshRenderer>>();
    private static readonly List<KeyValuePair<int, Animator>> Animators = new List<KeyValuePair<int, Animator>>();
    private static readonly List<KeyValuePair<int, ParticleSystem>> ParticleSystems = new List<KeyValuePair<int, ParticleSystem>>();
    private static readonly Dictionary<long, float> SmrLast = new Dictionary<long, float>();
    private static readonly Dictionary<int, float[]> AnimLast = new Dictionary<int, float[]>();
    private static readonly Dictionary<int, float[]> PsLast = new Dictionary<int, float[]>();
    private static StreamWriter _morphWriter;
    private static StreamWriter _animWriter;
    private static StreamWriter _psWriter;

    //Called by the Director once the timeline events are bound. `time` / `camera` give the current live time and
    //the active camera index; `root` is the Director transform (scene log). Does nothing when the export is off.
    public static void Attach(LiveTimelineControl ltc, Func<float> time, Func<int> camera, int liveId, Transform root = null)
    {
        Detach();
        if (ltc == null) return;
        //The Momentum capture (button) works whatever Config says: keep the context, subscribe nothing yet.
        _captureLtc = ltc;
        _time = time;
        _camera = camera;
        _liveId = liveId;
        _captureRoot = root;
        _t0 = Time.realtimeSinceStartupAsDouble;
        if (!Enabled) return;
        try
        {
            _ltc = ltc;
            _root = root;
            _step = Mathf.Max(1, Config.Instance.LiveTraceTransformStep);
            _builtFrame = int.MinValue;
            _lastSampleFrame = int.MinValue;
            _listed = 0;
            NodeIds.Clear(); NodeNames.Clear(); NodeLast.Clear(); Nodes.Clear();
            _folder = Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", "LiveTrace")),
                DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv) + "_live" + liveId);
            Directory.CreateDirectory(_folder);

            ltc.OnUpdateGlobalLight += OnGlobalLight;
            ltc.OnUpdateCharaFootLight += OnFootLight;
            ltc.OnUpdateFacialToon += OnFacialToon;
            ltc.OnUpdateSpotlight3d += OnSpotlight3d;
            ltc.OnUpdateExposure += OnExposure;
            ltc.OnUpdateToneCurve += OnToneCurve;
            ltc.OnUpdatePostEffect_BloomDiffusion += OnBloomDiffusion;
            ltc.OnUpdateAdditionalLight += OnAdditionalLight;
            ltc.OnUpdateAdditionalLightCount += OnAdditionalLightCount;
            ltc.OnUpdateLensFlare += OnLensFlare;
            ltc.OnUpdateMirrorReflection += OnMirrorReflection;
            ltc.OnUpdatePostEffect_Dof += OnDof;
            ltc.OnUpdateGlobalFog += OnFog;
            ltc.OnUpdatePostFilm += OnPostFilm;
            Debug.Log("[LiveTrace] Exporting timeline parameters to " + _folder);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LiveTrace] Could not start the export: " + ex.Message);
            Detach();
        }
    }

    //Unsubscribes and closes the files. Safe to call when nothing is attached.
    public static void Detach()
    {
        CaptureDetach();
        if (_ltc != null)
        {
            _ltc.OnUpdateGlobalLight -= OnGlobalLight;
            _ltc.OnUpdateCharaFootLight -= OnFootLight;
            _ltc.OnUpdateFacialToon -= OnFacialToon;
            _ltc.OnUpdateSpotlight3d -= OnSpotlight3d;
            _ltc.OnUpdateExposure -= OnExposure;
            _ltc.OnUpdateToneCurve -= OnToneCurve;
            _ltc.OnUpdatePostEffect_BloomDiffusion -= OnBloomDiffusion;
            _ltc.OnUpdateAdditionalLight -= OnAdditionalLight;
            _ltc.OnUpdateAdditionalLightCount -= OnAdditionalLightCount;
            _ltc.OnUpdateLensFlare -= OnLensFlare;
            _ltc.OnUpdateMirrorReflection -= OnMirrorReflection;
            _ltc.OnUpdatePostEffect_Dof -= OnDof;
            _ltc.OnUpdateGlobalFog -= OnFog;
            _ltc.OnUpdatePostFilm -= OnPostFilm;
            _ltc = null;
        }
        try { _morphWriter?.Flush(); _morphWriter?.Dispose(); } catch { }
        try { _animWriter?.Flush(); _animWriter?.Dispose(); } catch { }
        try { _psWriter?.Flush(); _psWriter?.Dispose(); } catch { }
        _morphWriter = _animWriter = _psWriter = null;
        Smrs.Clear(); Animators.Clear(); ParticleSystems.Clear();
        SmrLast.Clear(); AnimLast.Clear(); PsLast.Clear();
        foreach (var w in Writers.Values)
        {
            try { w.Flush(); w.Dispose(); } catch { }
        }
        Writers.Clear();
        try { _nodesWriter?.Flush(); _nodesWriter?.Dispose(); } catch { }
        try { _changesWriter?.Flush(); _changesWriter?.Dispose(); } catch { }
        _nodesWriter = null;
        _changesWriter = null;
        _root = null;
    }

    //Called by the Director at the end of every timeline update (after the events above fired). Writes the scene
    //transform log when the frame is a sample frame. Cheap no-op when the export is off.
    public static void Tick(float time)
    {
        CaptureTick(time);
        if (_ltc == null || _root == null) return;
        int frame = Mathf.FloorToInt(time * 60f + 0.001f);
        if (frame == _lastSampleFrame || frame % _step != 0) return;
        _lastSampleFrame = frame;
        try
        {
            // Also rebuilt when the hierarchy size changed (the character models are created after the first samples),
            // otherwise the skeletons would never enter the log.
            if (Nodes.Count == 0 || frame < _builtFrame || frame - _builtFrame >= 600
                || _root.GetComponentsInChildren<Transform>(true).Length != _hierarchyCount) RefreshNodes(frame);
            WriteChanges(frame);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LiveTrace] Scene log failed, export stopped: " + ex.Message);
            Detach();
        }
    }

    private static void Collect(Transform t, string parentPath, int depth, Dictionary<string, int> seen,
        List<KeyValuePair<Transform, string>> output)
    {
        if (t == null || depth > MaxDepth || output.Count >= MaxNodes) return;
        string baseName = parentPath.Length == 0 ? t.name : parentPath + "/" + t.name;
        seen.TryGetValue(baseName, out int count);
        seen[baseName] = count + 1;
        string path = count > 0 ? baseName + "#" + count : baseName;
        output.Add(new KeyValuePair<Transform, string>(t, path));
        for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i), path, depth + 1, seen, output);
    }

    private static void RefreshNodes(int frame)
    {
        var list = new List<KeyValuePair<Transform, string>>();
        var seenPaths = new Dictionary<string, int>();
        Collect(_root, "", 0, seenPaths, list);
        _hierarchyCount = list.Count;
        // The viewer's LiveTimelineControl (character position key without ParentObjectName) detaches each character
        // container from its CharacterObjectN, so the skeletons are not under the Director. The game keeps them at
        // <Director>/CharacterObjectN/chara_*, so log them under that virtual path.
        var dir = Gallop.Live.Director.instance;
        if (dir != null && dir.CharaContainerScript != null)
        {
            for (int i = 0; i < dir.CharaContainerScript.Count; i++)
            {
                var c = dir.CharaContainerScript[i];
                if (c == null || c.transform.IsChildOf(_root)) continue;
                Collect(c.transform, _root.name + "/CharacterObject" + i, 1, seenPaths, list);
            }
        }
        Nodes.Clear();
        foreach (var node in list)
        {
            if (!NodeIds.TryGetValue(node.Value, out int id))
            {
                id = NodeNames.Count;
                NodeIds[node.Value] = id;
                NodeNames.Add(node.Value);
                NodeLast.Add(null);
            }
            Nodes.Add(new KeyValuePair<Transform, int>(node.Key, id));
        }
        _builtFrame = frame;

        // Components worth logging (blend shapes, animators, particle systems), classified once per refresh.
        Smrs.Clear(); Animators.Clear(); ParticleSystems.Clear();
        foreach (var node in Nodes)
        {
            Transform t = node.Key;
            if (t == null) continue;
            var smr = t.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
                Smrs.Add(new KeyValuePair<int, SkinnedMeshRenderer>(node.Value, smr));
            var animator = t.GetComponent<Animator>();
            if (animator != null) Animators.Add(new KeyValuePair<int, Animator>(node.Value, animator));
            var ps = t.GetComponent<ParticleSystem>();
            if (ps != null) ParticleSystems.Add(new KeyValuePair<int, ParticleSystem>(node.Value, ps));
        }
        if (_listed < NodeNames.Count)
        {
            if (_nodesWriter == null)
            {
                _nodesWriter = new StreamWriter(Path.Combine(_folder, "scene_nodes.csv"), false, new UTF8Encoding(false));
                _nodesWriter.WriteLine("id,path");
            }
            for (int id = _listed; id < NodeNames.Count; id++)
                _nodesWriter.WriteLine(id + ",\"" + NodeNames[id].Replace('"', '\'') + "\"");
            _nodesWriter.Flush();
            _listed = NodeNames.Count;
        }
    }

    private static void WriteChanges(int frame)
    {
        if (_changesWriter == null)
        {
            _changesWriter = new StreamWriter(Path.Combine(_folder, "scene_changes.csv"), false, new UTF8Encoding(false));
            _changesWriter.WriteLine("live,run,frame,id,active,lposX,lposY,lposZ,lrotX,lrotY,lrotZ,lrotW,lscaleX,lscaleY,lscaleZ");
        }
        var v = new float[11];
        foreach (var node in Nodes)
        {
            Transform t = node.Key;
            if (t == null) continue;   //destroyed since the node list was built
            Vector3 p = t.localPosition;
            Quaternion r = t.localRotation;
            Vector3 s = t.localScale;
            v[0] = t.gameObject.activeSelf ? 1f : 0f;
            v[1] = p.x; v[2] = p.y; v[3] = p.z;
            v[4] = r.x; v[5] = r.y; v[6] = r.z; v[7] = r.w;
            v[8] = s.x; v[9] = s.y; v[10] = s.z;
            float[] old = NodeLast[node.Value];
            bool differs = old == null;
            if (!differs)
                for (int k = 0; k < 11; k++)
                    if (Mathf.Abs(old[k] - v[k]) > Epsilon) { differs = true; break; }
            if (!differs) continue;
            if (old == null) NodeLast[node.Value] = old = new float[11];
            Array.Copy(v, old, 11);
            _changesWriter.Write(_liveId); _changesWriter.Write(",1,");
            _changesWriter.Write(frame); _changesWriter.Write(',');
            _changesWriter.Write(node.Value); _changesWriter.Write(',');
            _changesWriter.Write((int)v[0]);
            for (int k = 1; k < 11; k++) { _changesWriter.Write(','); _changesWriter.Write(F(v[k])); }
            _changesWriter.WriteLine();
        }
        WriteExtras(frame);
    }

    private static StreamWriter OpenExtra(ref StreamWriter writer, string file, string header)
    {
        if (writer == null)
        {
            writer = new StreamWriter(Path.Combine(_folder, file), false, new UTF8Encoding(false));
            writer.WriteLine(header);
        }
        return writer;
    }

    //Blend shape weights, animator state and particle systems (reference format group K), same sample frames as the transforms.
    private static void WriteExtras(int frame)
    {
        string head = _liveId + ",1," + frame + ",";

        foreach (var pair in Smrs)
        {
            var smr = pair.Value;
            if (smr == null || smr.sharedMesh == null) continue;
            int shapes = Mathf.Min(smr.sharedMesh.blendShapeCount, 512);
            for (int shape = 0; shape < shapes; shape++)
            {
                float w = smr.GetBlendShapeWeight(shape);
                long key = ((long)pair.Key << 10) | (uint)shape;
                if (SmrLast.TryGetValue(key, out float old) && Mathf.Abs(old - w) <= 1e-3f) continue;
                SmrLast[key] = w;
                OpenExtra(ref _morphWriter, "scene_morphs.csv", "live,run,frame,id,shape,weight")
                    .WriteLine(head + pair.Key + "," + shape + "," + F(w));
            }
        }

        foreach (var pair in Animators)
        {
            var a = pair.Value;
            if (a == null || !a.isActiveAndEnabled) continue;
            AnimatorStateInfo s = a.GetCurrentAnimatorStateInfo(0);
            var cur = new[] { (float)s.fullPathHash, s.normalizedTime, s.length, a.speed };
            if (AnimLast.TryGetValue(pair.Key, out float[] old) &&
                Mathf.Abs(old[0] - cur[0]) <= 1e-4f && Mathf.Abs(old[1] - cur[1]) <= 1e-4f &&
                Mathf.Abs(old[2] - cur[2]) <= 1e-4f && Mathf.Abs(old[3] - cur[3]) <= 1e-4f) continue;
            AnimLast[pair.Key] = cur;
            OpenExtra(ref _animWriter, "scene_animators.csv",
                "live,run,frame,id,stateNameHash,fullPathHash,normalizedTime,length,speed,speedMultiplier,tag,loop")
                .WriteLine(head + pair.Key + "," + s.shortNameHash + "," + s.fullPathHash + "," + F(s.normalizedTime) + "," +
                           F(s.length) + "," + F(s.speed) + "," + F(s.speedMultiplier) + "," + s.tagHash + "," + (s.loop ? 1 : 0));
        }

        foreach (var pair in ParticleSystems)
        {
            var ps = pair.Value;
            if (ps == null) continue;
            var cur = new[] { ps.isPlaying ? 1f : 0f, ps.time, ps.particleCount };
            if (PsLast.TryGetValue(pair.Key, out float[] old) &&
                old[0] == cur[0] && old[2] == cur[2] && Mathf.Abs(old[1] - cur[1]) <= 1e-3f) continue;
            PsLast[pair.Key] = cur;
            OpenExtra(ref _psWriter, "scene_particles.csv", "live,run,frame,id,isPlaying,time,particleCount")
                .WriteLine(head + pair.Key + "," + (int)cur[0] + "," + F(cur[1]) + "," + (int)cur[2]);
        }
    }

    // ---- formatting (reference format: 6 decimals, NaN/Infinity -> empty) ----

    private static string F(float v) => (float.IsNaN(v) || float.IsInfinity(v)) ? "" : v.ToString("F6", Inv);
    private static string B(bool v) => v ? "1" : "0";
    private static string I(int v) => v.ToString(Inv);
    private static string C(Color c) => F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a);
    private static string V3(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
    private static string Q(Quaternion q) => F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w);

    private static void Push(string track, string columns)
    {
        bool stream = _ltc != null && _folder != null;
        if (!stream && !CaptureCollecting) return;
        try
        {
            float time = _time != null ? _time() : 0f;
            int frame = Mathf.FloorToInt(time * 60f + 0.001f);
            double t = Time.realtimeSinceStartupAsDouble - _t0;
            string row = t.ToString("F3", Inv) + "," + frame + "," + F(time) + ",0," + (_camera != null ? _camera() : 0) + "," +
                         _liveId + "," + columns;
            if (CaptureCollecting) CaptureRow(track, row);
            if (!stream) return;
            if (!Writers.TryGetValue(track, out var w))
            {
                w = new StreamWriter(Path.Combine(_folder, "render_" + track + ".csv"), false, new UTF8Encoding(false));
                w.AutoFlush = false;
                w.WriteLine("t,frame,time,sheet,cam,liveId," + HeaderOf(track));
                Writers[track] = w;
            }
            w.WriteLine(row);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LiveTrace] Write failed (" + track + "), export stopped: " + ex.Message);
            Detach();
        }
    }

    // ---- event handlers (read-only) ----

    private static void OnGlobalLight(ref GlobalLightUpdateInfo i)
    {
        Push("global_light", string.Join(",", Q(i.lightRotation), F(i.globalRimShadowRate), C(i.rimColor), F(i.rimStep),
            F(i.rimFeather), F(i.rimSpecRate), I((int)i.flags), F(i.RimHorizonOffset), F(i.RimVerticalOffset),
            F(i.RimHorizonOffset2), F(i.RimVerticalOffset2), C(i.rimColor2), F(i.rimStep2), F(i.rimFeather2),
            F(i.rimSpecRate2), F(i.globalRimShadowRate2)));
    }

    private static void OnFootLight(ref CharaFootLightUpdateInfo i)
    {
        Push("foot_light", string.Join(",", I(i.CharacterIndex), F(i.hightMax), C(i.lightColor), I((int)i.LightBlendMode), I((int)i.Easing)));
    }

    private static void OnFacialToon(ref FacialToonDataUpdateInfo i)
    {
        Push("facial_toon", string.Join(",", I(i.CharacterPos), F(i.CheekPretenseThreshold), F(i.NosePretenseThreshold),
            F(i.CylinderBlend), F(i.HairNormalBlend), I((int)i.UseOriginalDirectionalLight), V3(i.OriginalDirectionalLightDir),
            F(i.EyeToonStep), F(i.EyeToonFeather), F(i.EyeSaturation)));
    }

    private static void OnSpotlight3d(ref Spotlight3dUpdateInfo i)
    {
        Push("spotlight3d", string.Join(",", I(i.timelineIndex), I(i.characterFlag), B(i.isActive), B(i.IsEnabledBillboard),
            B(i.EnableLookAt), I(i.PositionType),
            C(i.color), F(i.colorPower), F(i.localHeight), V3(i.position), V3(i.rotation), V3(i.scale)));
    }

    private static void OnExposure(LiveExposureToneCurveTimeline.ExposureUpdateInfo i)
    {
        //ASSUMED mapping Gain/Lift/MaskGain/MaskLift -> ExposureParameter x/y/z/w (see the header comment).
        Push("exposure", string.Join(",", B(i.IsEnable), F(i.Gain), F(i.Lift), F(i.MaskGain), F(i.MaskLift), F(i.DepthMask)));
    }

    private static void OnToneCurve(LiveExposureToneCurveTimeline.ToneCurveUpdateInfo i)
    {
        Push("tone_curve", string.Join(",", B(i.IsEnable), F(i.DepthMask), C(i.MinCorrectionLevel), C(i.MaxCorrectionLevel),
            C(i.MaskMinCorrectionLevel), C(i.MaskMaxCorrectionLevel)));
    }

    private static void OnBloomDiffusion(PostEffectUpdateInfo_BloomDiffusion i)
    {
        Push("bloom_diffusion", string.Join(",", B(i.IsEnabledBloom), F(i.bloomDofWeight), F(i.threshold), F(i.intensity),
            F(i.BloomBlurSize), I((int)i.BloomBlendMode), B(i.IsEnabledDiffusion), F(i.diffusionBlurSize), F(i.diffusionBright),
            F(i.diffusionThreshold), F(i.diffusionSaturation), F(i.diffusionContrast)));
    }

    private static void OnAdditionalLight(ref AdditionalLightUpdateInfo i)
    {
        Push("additional_light", string.Join(",", I(i.Index), V3(i.Position), V3(i.Rotate), B(i.IsEnable), I((int)i.Type),
            F(i.Range), I(i.SpotAngle), I(i.IndirectMultiplier), I((int)i.ShadowType), F(i.Strength), F(i.Bias),
            F(i.NormalBias), F(i.NearPlane)));
    }

    private static void OnAdditionalLightCount(int count)
    {
        Push("additional_light_count", I(count));
    }

    private static void OnLensFlare(ref LensFlareUpdateInfo i)
    {
        Push("lens_flare", string.Join(",", I(i.TimelineNameHash), V3(i.offset), C(i.color), F(i.brightness), F(i.fadeSpeed),
            B(i.enable), B(i.enableParameter), B(i.IsAutoBrightness), B(i.IsOverridePosition), B(i.IsScreenFit)));
    }

    //DOF: the viewer receives the timeline KEY and the world focus point, not the game's PostEffectUpdateInfo_DOF. The
    //columns are derived the way Director.OnUpdatePostEffect_Dof does (attribute bit 17 = metric focal point, bit 18 =
    //enable, bit 19 = point ball blur). focalPos is the world focus (0 when the key uses a metric depth).
    private static void OnDof(LiveTimelineKeyPostEffectDOFData key, Vector3? worldFocus)
    {
        if (key == null) { Push("dof", "0" + new string(',', 16)); return; }
        int attr = (int)key.attribute;
        bool metric = (attr & (1 << 17)) != 0;
        bool valid = metric || worldFocus.HasValue;
        Push("dof", string.Join(",", B(valid && (attr & (1 << 18)) != 0), F(key.forcalSize), F(key.blurSpread),
            V3(worldFocus.GetValueOrDefault()), I(key.dofQuality), I(key.dofBlurType), F(key.dofForegroundSize),
            F(key.dofFocalPoint), F(key.dofSmoothness), B(metric), F(key.BallBlurPowerFactor),
            F(key.BallBlurBrightnessThreshhold), F(key.BallBlurBrightnessIntensity), F(key.BallBlurSpread),
            B((attr & (1 << 19)) != 0)));
    }

    private static void OnFog(LiveTimelineGlobalFogData data, LiveTimelineKeyGlobalFogData key)
    {
        if (key == null) return;
        Push("global_fog", string.Join(",", B(key.isDistance), F(key.startDistance), B(key.isHeight), F(key.height),
            F(key.heightDensity), C(key.color), I(key.fogMode), F(key.expDensity), F(key.start), F(key.end),
            B(key.useRadialDistance)));
    }

    //Post film: one row per layer (the game also fires it once per layer, in the same order). Fields the viewer's
    //Overlay does not keep as such (colorType, movieFrameOffset, movieTime, movieReverse, RollAngle, FilmScale)
    //are written empty.
    private static void OnPostFilm(int layer, Gallop.ImageEffect.ScreenOverlay.Overlay o)
    {
        if (o == null) return;
        //The game fires OnUpdatePostFilm / OnUpdatePostFilm2 / OnUpdatePostFilm3 (one track each); the viewer one event
        //with the layer index 0 / 1 / 2.
        Push(layer == 1 ? "post_film2" : layer == 2 ? "post_film3" : "post_film", string.Join(",", I((int)o.postFilmMode), "", F(o.postFilmPower),
            F(o.postFilmOffsetParam.x), F(o.postFilmOffsetParam.y),
            F(o.postFilmOptionParam.x), F(o.postFilmOptionParam.y), F(o.postFilmOptionParam.z), F(o.postFilmOptionParam.w),
            C(o.postFilmColor0), C(o.postFilmColor1), C(o.postFilmColor2), C(o.postFilmColor3),
            F(o.depthPower), F(o.DepthClip), I((int)o.layerMode), I((int)o.colorBlend), B(o.inverseVignette),
            F(o.colorBlendFactor), I(o.movieResId), "", "", "", "", "", ""));
    }

    private static void OnMirrorReflection(in LiveTimelineControl.MirrorReflectionUpdateInfo i)
    {
        Push("mirror_reflection", string.Join(",", I(i.TimelineNameHash), I((int)i.BaseCameraType), I(i.BaseCameraIndex),
            B(i.EnableMirror), B(i.EnableBgLayer), B(i.Enable3dLayer), B(i.IsToonMirror), F(i.MirrorReflectionRate),
            I((int)i.TargetChara), B(i.EnableCharaHead), I((int)i.TargetCharaHead)));
    }
}
