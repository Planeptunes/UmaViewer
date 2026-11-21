using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Gallop.Live.Cutt;
using UnityEngine;
using K = LiveProbeSpecs.K;

//(Probe capture, timeline part) One timeline tick of every track, in the columns of the fixed reference capture
//format (hand-written tracks + table-driven tracks), for comparison with reference data.
//
//How: LiveProbeCapture arms it; the next LiveTraceExporter.Tick starts COLLECTING (it subscribes the handlers), the
//events of one timeline update push their rows, the following Tick closes it (unsubscribes) and the rows are ready.
//That is the window the reference format covers for a Momentum capture (the calls of the last timeline tick).
//
//It never changes what the viewer evaluates: a handler is only added to an event the viewer itself already uses
//(its delegate is not null), because several LiveTimelineControl updates only evaluate their timeline when someone is
//subscribed. An event nobody subscribes in the viewer is listed in the capture manifest ("not applied by the viewer").
//
//Table-driven tracks: the columns come from LiveProbeSpecs (generated from dump_jp.cs, the tables of the reference format);
//each value is read by NAME from the viewer's UpdateInfo with reflection. A game field the viewer's struct does not have
//is written empty and listed in missing_fields.csv (that is a fidelity difference to look at).
public static partial class LiveTraceExporter
{
    private static LiveTimelineControl _captureLtc;
    private static Transform _captureRoot;
    private enum CaptureState { Idle, Armed, Collecting, Done }
    private static CaptureState _captureState = CaptureState.Idle;
    private static readonly Dictionary<string, List<string>> CaptureRows = new Dictionary<string, List<string>>();
    private static readonly Dictionary<string, string> CaptureMissing = new Dictionary<string, string>();
    private static readonly List<string> CaptureNotApplied = new List<string>();
    private static readonly List<Action> CaptureUnsubscribe = new List<Action>();
    private static float _captureTime;

    private static bool CaptureCollecting => _captureState == CaptureState.Collecting;
    private static float Now => _time != null ? _time() : 0f;
    public static bool CaptureReady => _captureState == CaptureState.Done;
    public static bool CaptureBusy => _captureState == CaptureState.Armed || _captureState == CaptureState.Collecting;
    public static bool CaptureAvailable => _captureLtc != null;
    public static int CaptureLiveId => _liveId;
    public static float CaptureTime => _captureTime;
    public static Transform CaptureRootTransform => _captureRoot;
    public static IReadOnlyDictionary<string, List<string>> CapturedRows => CaptureRows;
    public static IReadOnlyDictionary<string, string> CapturedMissing => CaptureMissing;
    public static IReadOnlyList<string> CapturedNotApplied => CaptureNotApplied;

    //Starts a capture at the next timeline tick. False when no live is running or one is in progress.
    public static bool ArmCapture()
    {
        if (_captureLtc == null || CaptureBusy) return false;
        CaptureRows.Clear();
        CaptureMissing.Clear();
        CaptureNotApplied.Clear();
        _captureState = CaptureState.Armed;
        return true;
    }

    public static void ClearCapture()
    {
        if (_captureState == CaptureState.Done) _captureState = CaptureState.Idle;
    }

    private static void CaptureDetach()
    {
        UnsubscribeCapture();
        _captureState = CaptureState.Idle;
        _captureLtc = null;
        _captureRoot = null;
    }

    private static void CaptureTick(float time)
    {
        if (_captureState == CaptureState.Armed)
        {
            SubscribeCapture();
            _captureState = CaptureState.Collecting;
        }
        else if (_captureState == CaptureState.Collecting)
        {
            UnsubscribeCapture();
            _captureTime = time;
            //Not-subscribed tracks the reference format also has as one row per tick.
            CaptureRow("hdr_bloom", CtxPrefix(time) + "," + (EventUsed("OnUpdateHdrBloom") ? 1 : 0) + ",");
            _captureState = CaptureState.Done;
        }
    }

    private static string CtxPrefix(float time)
    {
        int frame = Mathf.FloorToInt(time * 60f + 0.001f);
        double t = Time.realtimeSinceStartupAsDouble - _t0;
        return t.ToString("F3", Inv) + "," + frame + "," + F(time) + ",0," + (_camera != null ? _camera() : 0) + "," + _liveId;
    }

    private static void CaptureRow(string track, string row)
    {
        if (!CaptureRows.TryGetValue(track, out var list)) CaptureRows[track] = list = new List<string>();
        list.Add(row);
    }

    //Full CSV header of a captured track.
    public static string CaptureFullHeader(string track) => "t,frame,time,sheet,cam,liveId," + HeaderOf(track);

    // ---- subscription (only to events the viewer already uses) ----

    private static bool EventUsed(string eventName)
    {
        if (_captureLtc == null) return false;
        FieldInfo f = typeof(LiveTimelineControl).GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return f != null && f.GetValue(_captureLtc) != null;
    }

    private static void Sub(string eventName, string track, Action add, Action remove)
    {
        if (!EventUsed(eventName)) { CaptureNotApplied.Add(track + " (" + eventName + ")"); return; }
        add();
        CaptureUnsubscribe.Add(remove);
    }

    private static void SubscribeCapture()
    {
        UnsubscribeCapture();
        var l = _captureLtc;
        if (l == null) return;
        //Hand-written tracks: already subscribed when the continuous export is on.
        if (_ltc == null)
        {
            Sub("OnUpdateGlobalLight", "global_light", () => l.OnUpdateGlobalLight += OnGlobalLight, () => l.OnUpdateGlobalLight -= OnGlobalLight);
            Sub("OnUpdateCharaFootLight", "foot_light", () => l.OnUpdateCharaFootLight += OnFootLight, () => l.OnUpdateCharaFootLight -= OnFootLight);
            Sub("OnUpdateFacialToon", "facial_toon", () => l.OnUpdateFacialToon += OnFacialToon, () => l.OnUpdateFacialToon -= OnFacialToon);
            Sub("OnUpdateSpotlight3d", "spotlight3d", () => l.OnUpdateSpotlight3d += OnSpotlight3d, () => l.OnUpdateSpotlight3d -= OnSpotlight3d);
            Sub("OnUpdateExposure", "exposure", () => l.OnUpdateExposure += OnExposure, () => l.OnUpdateExposure -= OnExposure);
            Sub("OnUpdateToneCurve", "tone_curve", () => l.OnUpdateToneCurve += OnToneCurve, () => l.OnUpdateToneCurve -= OnToneCurve);
            Sub("OnUpdatePostEffect_BloomDiffusion", "bloom_diffusion", () => l.OnUpdatePostEffect_BloomDiffusion += OnBloomDiffusion, () => l.OnUpdatePostEffect_BloomDiffusion -= OnBloomDiffusion);
            Sub("OnUpdateAdditionalLight", "additional_light", () => l.OnUpdateAdditionalLight += OnAdditionalLight, () => l.OnUpdateAdditionalLight -= OnAdditionalLight);
            Sub("OnUpdateAdditionalLightCount", "additional_light_count", () => l.OnUpdateAdditionalLightCount += OnAdditionalLightCount, () => l.OnUpdateAdditionalLightCount -= OnAdditionalLightCount);
            Sub("OnUpdateLensFlare", "lens_flare", () => l.OnUpdateLensFlare += OnLensFlare, () => l.OnUpdateLensFlare -= OnLensFlare);
            Sub("OnUpdateMirrorReflection", "mirror_reflection", () => l.OnUpdateMirrorReflection += OnMirrorReflection, () => l.OnUpdateMirrorReflection -= OnMirrorReflection);
            Sub("OnUpdatePostEffect_Dof", "dof", () => l.OnUpdatePostEffect_Dof += OnDof, () => l.OnUpdatePostEffect_Dof -= OnDof);
            Sub("OnUpdateGlobalFog", "global_fog", () => l.OnUpdateGlobalFog += OnFog, () => l.OnUpdateGlobalFog -= OnFog);
            Sub("OnUpdatePostFilm", "post_film", () => l.OnUpdatePostFilm += OnPostFilm, () => l.OnUpdatePostFilm -= OnPostFilm);
        }
        //Extra tracks (only during a capture).
        Sub("OnUpdateToneCurve", "tone_curve_curves", () => l.OnUpdateToneCurve += OnToneCurveCurves, () => l.OnUpdateToneCurve -= OnToneCurveCurves);
        Sub("OnUpdateBgColor1", "bg_color1", () => l.OnUpdateBgColor1 += OnBgColor1, () => l.OnUpdateBgColor1 -= OnBgColor1);
        Sub("OnUpdateBgColor2", "bg_color2", () => l.OnUpdateBgColor2 += OnBgColor2, () => l.OnUpdateBgColor2 -= OnBgColor2);
        Sub("OnUpdateProjector", "projector", () => l.OnUpdateProjector += OnProjector, () => l.OnUpdateProjector -= OnProjector);
        Sub("OnUpdateLaser", "laser", () => l.OnUpdateLaser += OnLaser, () => l.OnUpdateLaser -= OnLaser);
        Sub("OnEnvironmentStageFovShift", "stage_fov_shift", () => l.OnEnvironmentStageFovShift += OnStageFovShift, () => l.OnEnvironmentStageFovShift -= OnStageFovShift);
        Sub("OnEnvironmentMirror", "env_mirror", () => l.OnEnvironmentMirror += OnEnvMirror, () => l.OnEnvironmentMirror -= OnEnvMirror);
        Sub("OnUpdateVolumeLight", "volume_light", () => l.OnUpdateVolumeLight += OnVolumeLight, () => l.OnUpdateVolumeLight -= OnVolumeLight);
        Sub("OnUpdateLightShafts", "light_shafts", () => l.OnUpdateLightShafts += OnLightShafts, () => l.OnUpdateLightShafts -= OnLightShafts);
        Sub("OnUpdateBlinkLight", "blink_light", () => l.OnUpdateBlinkLight += OnBlinkLight, () => l.OnUpdateBlinkLight -= OnBlinkLight);
        Sub("OnUpdateUVScrollLight", "uv_scroll_light", () => l.OnUpdateUVScrollLight += OnUVScrollLight, () => l.OnUpdateUVScrollLight -= OnUVScrollLight);
        Sub("OnUpdateLightProjection", "light_projection", () => l.OnUpdateLightProjection += OnLightProjection, () => l.OnUpdateLightProjection -= OnLightProjection);
        Sub("OnUpdateMultiLightShadowParam", "multi_light_shadow", () => l.OnUpdateMultiLightShadowParam += OnMultiLightShadow, () => l.OnUpdateMultiLightShadowParam -= OnMultiLightShadow);
        Sub("OnUpdateCyalumeControl", "mob_cyalume", () => l.OnUpdateCyalumeControl += OnMobCyalume, () => l.OnUpdateCyalumeControl -= OnMobCyalume);
        Sub("OnUpdateWashLight", "wash_light", () => l.OnUpdateWashLight += OnWashLight, () => l.OnUpdateWashLight -= OnWashLight);
        Sub("OnUpdateChromaticAberration", "chromatic_aberration", () => l.OnUpdateChromaticAberration += OnChromaticAberration, () => l.OnUpdateChromaticAberration -= OnChromaticAberration);
        Sub("OnUpdateTransmittedLightMask", "transmitted_mask", () => l.OnUpdateTransmittedLightMask += OnTransmittedMask, () => l.OnUpdateTransmittedLightMask -= OnTransmittedMask);
        Sub("OnUpdateParticle", "particle", () => l.OnUpdateParticle += OnParticle, () => l.OnUpdateParticle -= OnParticle);
        Sub("OnUpdateParticleGroup", "particle_group", () => l.OnUpdateParticleGroup += OnParticleGroup, () => l.OnUpdateParticleGroup -= OnParticleGroup);
        Sub("OnUpdateEffect", "effect", () => l.OnUpdateEffect += OnEffect, () => l.OnUpdateEffect -= OnEffect);
        Sub("OnUpdateEffectScale", "effect_scale", () => l.OnUpdateEffectScale += OnEffectScale, () => l.OnUpdateEffectScale -= OnEffectScale);
        Sub("OnUpdateFacial", "facial", () => l.OnUpdateFacial += OnFacial, () => l.OnUpdateFacial -= OnFacial);
    }

    private static void UnsubscribeCapture()
    {
        foreach (var remove in CaptureUnsubscribe)
        {
            try { remove(); } catch { }
        }
        CaptureUnsubscribe.Clear();
    }

    // ---- table-driven tracks: (track, game struct table, argument column) ----

    private struct CapTrack { public LiveProbeSpecs.F[] Fields; public string Arg; }

    private static readonly Dictionary<string, CapTrack> CapTracks = new Dictionary<string, CapTrack>
    {
        { "bg_color1", new CapTrack { Fields = LiveProbeSpecs.BgColor1UpdateInfo } },
        { "bg_color2", new CapTrack { Fields = LiveProbeSpecs.BgColor2UpdateInfo } },
        { "projector", new CapTrack { Fields = LiveProbeSpecs.ProjectorUpdateInfo } },
        { "laser", new CapTrack { Fields = LiveProbeSpecs.LaserUpdateInfo } },
        { "stage_fov_shift", new CapTrack { Fields = LiveProbeSpecs.EnvironmentStageFovShiftUpdateInfo } },
        { "env_mirror", new CapTrack { Fields = LiveProbeSpecs.EnvironmentMirrorUpdateInfo } },
        { "volume_light", new CapTrack { Fields = LiveProbeSpecs.VolumeLightUpdateInfo } },
        { "light_shafts", new CapTrack { Fields = LiveProbeSpecs.LightShaftsUpdateInfo } },
        { "blink_light", new CapTrack { Fields = LiveProbeSpecs.BlinkLightUpdateInfo, Arg = "arg" } },
        { "uv_scroll_light", new CapTrack { Fields = LiveProbeSpecs.UVScrollLightUpdateInfo } },
        { "light_projection", new CapTrack { Fields = LiveProbeSpecs.LightProjectionUpdateInfo } },
        { "multi_light_shadow", new CapTrack { Fields = LiveProbeSpecs.MultiLightShadowUpdateInfo } },
        { "mob_cyalume", new CapTrack { Fields = LiveProbeSpecs.MobCyalumeUpdateInfo } },
        { "wash_light", new CapTrack { Fields = LiveProbeSpecs.WashLightUpdateInfo } },
        { "chromatic_aberration", new CapTrack { Fields = LiveProbeSpecs.ChromaticAberrationUpdateInfo } },
        { "effect", new CapTrack { Fields = LiveProbeSpecs.EffectUpdateInfo } },
        { "effect_scale", new CapTrack { Fields = LiveProbeSpecs.EffectScaleUpdateInfo } },
        { "post_film2", new CapTrack { Fields = LiveProbeSpecs.PostFilmUpdateInfo } },
        { "post_film3", new CapTrack { Fields = LiveProbeSpecs.PostFilmUpdateInfo } },
        { "particle", new CapTrack { Fields = LiveProbeSpecs.ParticleUpdateInfo } },
        { "particle_group", new CapTrack { Fields = LiveProbeSpecs.ParticleGroupUpdateInfo } },
    };

    //Headers of the reference format's custom tracks; keep in sync.
    private static readonly Dictionary<string, string> CustomHeaders = new Dictionary<string, string>
    {
        { "facial", "charaIndex,arg1,arg2,faceCur,faceNext,mouth,eyeCur,eyeNext,eyebrow,eyeTrack,ear,tail,effect,faceKeyIndex,mouthKeyIndex,eyeKeyIndex,eyebrowKeyIndex,eyeTrackKeyIndex,earKeyIndex,effectKeyIndex,tailKeyIndex,tailMotionSec" },
        { "foot_light_apply", "hightMax,colR,colG,colB,colA,blendMode,easing,modelCount,models" },
        { "hdr_bloom", "subscribed,timelineCount" },
        { "tone_curve_curves", "ToneAnimationCurve,MaskToneCurve" },
        { "transmitted_mask", "camArg,timelineHash,count,scales" },
    };

    private static string CaptureHeader(string track)
    {
        if (CustomHeaders.TryGetValue(track, out string h)) return h;
        if (!CapTracks.TryGetValue(track, out var t)) return "";
        var cols = new List<string>();
        if (!string.IsNullOrEmpty(t.Arg)) cols.Add(t.Arg);
        foreach (var f in t.Fields) cols.AddRange(Columns(f));
        return string.Join(",", cols);
    }

    //Column expansion of the reference format (V2/V3/V4/... -> .x/.y/...).
    private static IEnumerable<string> Columns(LiveProbeSpecs.F f)
    {
        switch (f.Kind)
        {
            case K.V2: return new[] { f.Name + ".x", f.Name + ".y" };
            case K.V3: return new[] { f.Name + ".x", f.Name + ".y", f.Name + ".z" };
            case K.V4: return new[] { f.Name + ".x", f.Name + ".y", f.Name + ".z", f.Name + ".w" };
            case K.Col: return new[] { f.Name + ".r", f.Name + ".g", f.Name + ".b", f.Name + ".a" };
            default: return new[] { f.Name };
        }
    }

    private static int Width(K k) => k == K.V2 ? 2 : k == K.V3 ? 3 : (k == K.V4 || k == K.Col) ? 4 : 1;

    // ---- reflection: read a game field by name from the viewer's object ----

    //Pure renames (same value, different identifier in the viewer port). Listed in missing_fields.csv as "renamed".
    private static readonly Dictionary<string, string> Renames = new Dictionary<string, string>
    {
        { "ToneAnimationCurve", "ToneCurve" },
        // Game FacialDataUpdateInfo field names (dump_jp TypeDefIndex 33583587) now match the viewer struct one
        // to one (mouth / eyebrow / eyeTrack / ear / tail + the cur/next pairs and all key indexes), so the old
        // facial renames (mouth -> mouthCur, ...) are gone: every captured name is the game's own.
    };

    private static readonly Dictionary<string, MemberInfo> MemberCache = new Dictionary<string, MemberInfo>();

    private static MemberInfo FindMember(Type type, string name)
    {
        string key = type.FullName + "|" + name;
        if (MemberCache.TryGetValue(key, out var cached)) return cached;
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        MemberInfo m = (MemberInfo)type.GetField(name, all) ?? type.GetProperty(name, all);
        if (m == null)
        {
            string want = name.TrimStart('_').ToLowerInvariant();
            foreach (var f in type.GetFields(all))
            {
                string n = f.Name.Replace("k__BackingField", "").Trim('<', '>', '_').ToLowerInvariant();
                if (n == want) { m = f; break; }
            }
            if (m == null)
                foreach (var p in type.GetProperties(all))
                    if (p.GetIndexParameters().Length == 0 && p.Name.TrimStart('_').ToLowerInvariant() == want) { m = p; break; }
        }
        MemberCache[key] = m;
        return m;
    }

    private static bool TryGet(object obj, string name, out object value)
    {
        value = null;
        if (obj == null) return false;
        MemberInfo m = FindMember(obj.GetType(), name);
        if (m == null) return false;
        try { value = m is FieldInfo fi ? fi.GetValue(obj) : ((PropertyInfo)m).GetValue(obj); return true; }
        catch { return false; }
    }

    private static string Num(object v)
    {
        switch (v)
        {
            case null: return "";
            case bool b: return b ? "1" : "0";
            case float f: return F(f);
            case double d: return F((float)d);
            case Enum e: return Convert.ToInt64(e).ToString(Inv);
            case LayerMask lm: return lm.value.ToString(Inv);
            case IConvertible c: return Convert.ToInt64(c, Inv).ToString(Inv);
            default: return "";
        }
    }

    private static float[] Vec(object v)
    {
        switch (v)
        {
            case Vector2 a: return new[] { a.x, a.y };
            case Vector3 a: return new[] { a.x, a.y, a.z };
            case Vector4 a: return new[] { a.x, a.y, a.z, a.w };
            case Quaternion a: return new[] { a.x, a.y, a.z, a.w };
            case Color a: return new[] { a.r, a.g, a.b, a.a };
            case Color32 a: return new[] { a.r / 255f, a.g / 255f, a.b / 255f, a.a / 255f };
            default: return null;
        }
    }

    private static string Quote(string s) => "\"" + (s ?? "").Replace('"', '\'') + "\"";

    private static string ObjText(object o)
    {
        if (o == null) return "null";
        if (o is UnityEngine.Object u) return u == null ? "null" : u.GetType().Name + ":" + u.name;
        return o.GetType().Name;
    }

    private static string CurveText(AnimationCurve c)
    {
        if (c == null) return "";
        var parts = new List<string>();
        foreach (var k in c.keys) parts.Add(F(k.time) + ":" + F(k.value) + ":" + F(k.inTangent) + ":" + F(k.outTangent));
        return string.Join("|", parts);
    }

    //LiveTimelineKey "frame:a:b": frame + the first two int-like fields declared by the key's own class (the reference format
    //takes the key object at +0x18 / +0x1C, i.e. the first two fields after the LiveTimelineKey base).
    private static string KeyText(object key)
    {
        if (key == null) return "";
        TryGet(key, "frame", out object frame);
        var vals = new List<string> { Num(frame) };
        foreach (var f in key.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (vals.Count >= 3) break;
            object v = f.GetValue(key);
            if (v is int || v is Enum || v is short || v is byte) vals.Add(Num(v));
        }
        while (vals.Count < 3) vals.Add("");
        return string.Join(":", vals);
    }

    private static string ListText(object v, Func<object, string> one)
    {
        if (!(v is IList list)) return "";
        var parts = new List<string>();
        foreach (var e in list) parts.Add(one(e));
        return string.Join("|", parts);
    }

    private static void Format(string track, object info, LiveProbeSpecs.F f, List<string> cols)
    {
        string name = f.Name;
        bool found = TryGet(info, name, out object v);
        if (!found && Renames.TryGetValue(name, out string alias) && TryGet(info, alias, out v))
        {
            found = true;
            CaptureMissing[track + "." + name] = "renamed in the viewer: " + alias;
        }
        //Timeline name hashes: the viewer often keeps the group object instead (data / group .nameHash).
        if (!found && name.EndsWith("NameHash", StringComparison.OrdinalIgnoreCase) || !found && name == "nameHash")
        {
            foreach (string holder in new[] { "data", "group", "Group" })
                if (TryGet(info, holder, out object g) && g != null && TryGet(g, "nameHash", out v))
                {
                    found = true;
                    CaptureMissing[track + "." + name] = "taken from " + holder + ".nameHash";
                    break;
                }
        }
        if (!found)
        {
            CaptureMissing[track + "." + name] = "the viewer's " + (info?.GetType().Name ?? "null") + " has no such field";
            for (int i = 0; i < Width(f.Kind); i++) cols.Add("");
            return;
        }
        switch (f.Kind)
        {
            case K.U8: case K.I32: case K.U32: cols.Add(Num(v)); break;
            case K.F32: cols.Add(v is IConvertible ? F(Convert.ToSingle(v, Inv)) : ""); break;
            case K.V2: case K.V3: case K.V4: case K.Col:
            {
                float[] a = Vec(v);
                for (int i = 0; i < Width(f.Kind); i++) cols.Add(a != null && i < a.Length ? F(a[i]) : "");
                break;
            }
            case K.Str: cols.Add(Quote(v as string ?? v?.ToString())); break;
            case K.Obj: cols.Add(Quote(ObjText(v))); break;
            case K.Curve: cols.Add(v is AnimationCurve c ? Quote(CurveText(c)) : ""); break;
            case K.ArrI32: case K.ArrU8: cols.Add(Quote(ListText(v, Num))); break;
            case K.ArrF32: cols.Add(Quote(ListText(v, e => e is IConvertible ? F(Convert.ToSingle(e, Inv)) : ""))); break;
            case K.ArrV4: case K.ArrV3:
                cols.Add(Quote(ListText(v, e => { var a = Vec(e); return a == null ? "" : string.Join(":", Array.ConvertAll(a, F)); })));
                break;
            case K.ArrObj: cols.Add(v is IList l ? l.Count.ToString(Inv) : ""); break;
            case K.Key: cols.Add(KeyText(v)); break;
            case K.SrcData:
                cols.Add(Quote(ListText(v, e =>
                {
                    TryGet(e, "_sourceType", out object st); TryGet(e, "_targetIndex", out object ti); TryGet(e, "_nodeName", out object nn);
                    TryGet(e, "_weight", out object w); TryGet(e, "_propId", out object pid);
                    return Num(st) + ":" + Num(ti) + ":" + (nn as string ?? "") + ":" + (w is IConvertible ? F(Convert.ToSingle(w, Inv)) : "") + ":" + Num(pid);
                })));
                break;
        }
    }

    private static void Generic(string track, object info, string arg = null)
    {
        if (!CaptureCollecting || !CapTracks.TryGetValue(track, out var t)) return;
        var cols = new List<string>();
        if (!string.IsNullOrEmpty(t.Arg)) cols.Add(arg ?? "");
        foreach (var f in t.Fields) Format(track, info, f, cols);
        CaptureRow(track, CtxPrefix(Now) + "," + string.Join(",", cols));
    }

    // ---- handlers of the extra tracks ----

    private static void OnToneCurveCurves(LiveExposureToneCurveTimeline.ToneCurveUpdateInfo i)
    {
        if (!CaptureCollecting) return;
        CaptureRow("tone_curve_curves", CtxPrefix(Now) + "," + Quote(CurveText(i.ToneCurve)) + "," + Quote(CurveText(i.MaskToneCurve)));
    }
    private static void OnBgColor1(ref BgColor1UpdateInfo i) => Generic("bg_color1", i);
    private static void OnBgColor2(ref BgColor2UpdateInfo i) => Generic("bg_color2", i);
    private static void OnProjector(ref LiveProjectorTimeline.UpdateInfo i) => Generic("projector", i);
    private static void OnLaser(ref LaserUpdateInfo i) => Generic("laser", i);
    private static void OnStageFovShift(ref EnvironmentStageFovShiftUpdateInfo i) => Generic("stage_fov_shift", i);
    private static void OnEnvMirror(ref EnvironmentMirrorUpdateInfo i) => Generic("env_mirror", i);
    private static void OnVolumeLight(LiveVolumeLightTimeline.VolumeUpdateInfo i) => Generic("volume_light", i);
    private static void OnLightShafts(LiveVolumeLightTimeline.ShaftsUpdateInfo i) => Generic("light_shafts", i);
    //Game: StageController.UpdateBlinkLight(ref info, int) with the timeline name hash as the int.
    private static void OnBlinkLight(LiveTimelineBlinkLightData data, ref BlinkLightUpdateInfo i, float time) =>
        Generic("blink_light", i, data != null ? data.nameHash.ToString(Inv) : "");
    private static void OnUVScrollLight(ref UVScrollLightUpdateInfo i) => Generic("uv_scroll_light", i);
    private static void OnLightProjection(ref LiveLightProjectionTimeline.UpdateInfo i) => Generic("light_projection", i);
    private static void OnMultiLightShadow(ref MultiLightShadowUpdateInfo i) => Generic("multi_light_shadow", i);
    private static void OnMobCyalume(ref MobCyalumeUpdateInfo i) => Generic("mob_cyalume", i);
    private static void OnWashLight(ref WashLightUpdateInfo i) => Generic("wash_light", i);
    private static void OnChromaticAberration(ref ChromaticAberrationUpdateInfo i) => Generic("chromatic_aberration", i);
    private static void OnEffect(LiveEffectTimeline.EffectUpdateInfo i) => Generic("effect", i);
    private static void OnEffectScale(LiveEffectTimeline.EffectScaleUpdateInfo i) => Generic("effect_scale", i);

    //Game ParticleUpdateInfo = (TimelineNameHash, EmissionRate); the viewer passes the group and the rate.
    private static void OnParticle(LiveTimelineParticleData group, float rate)
    {
        if (!CaptureCollecting) return;
        CaptureRow("particle", CtxPrefix(Now) + "," + (group != null ? group.nameHash.ToString(Inv) : "") + "," + F(rate));
    }

    //Game ParticleGroupUpdateInfo = (TimelineNameHash, FlickerLightRate, FlickerDarkRate); the viewer passes a Vector2
    //(ASSUMED x = light, y = dark: compare to confirm).
    private static void OnParticleGroup(LiveTimelineParticleGroupData group, Vector2 range)
    {
        if (!CaptureCollecting) return;
        CaptureRow("particle_group", CtxPrefix(Now) + "," + (group != null ? group.nameHash.ToString(Inv) : "") + "," + F(range.x) + "," + F(range.y));
    }

    //Game: StageController.UpdateTransmittedLightMask(ref info, int camera). The viewer has no camera argument.
    private static void OnTransmittedMask(ref TransmittedLightMaskUpdateInfo i)
    {
        if (!CaptureCollecting) return;
        object boxed = i;
        TryGet(boxed, "Group", out object group);
        object hash = null;
        if (group != null) TryGet(group, "nameHash", out hash);
        TryGet(boxed, "Scales", out object scales);
        int count = scales is IList l ? l.Count : 0;
        string text = ListText(scales, e => e is IConvertible ? F(Convert.ToSingle(e, Inv)) : "").Replace('|', ' ');
        CaptureRow("transmitted_mask", CtxPrefix(Now) + ",," + Num(hash) + "," + count + "," + Quote(text));
    }

    //Game: LiveFaceController.AlterUpdateFacialNew(int, float, int, ref FacialDataUpdateInfo) (reference columns: charaIndex,
    //arg1, arg2). The viewer raises (info, liveTime, character index): written as charaIndex = index, arg1 = time, arg2 empty.
    private static void OnFacial(FacialDataUpdateInfo info, float time, int index)
    {
        if (!CaptureCollecting) return;
        var cols = new List<string> { index.ToString(Inv), F(time), "" };
        foreach (var f in LiveProbeSpecs.FacialDataUpdateInfo) Format("facial", info, f, cols);
        CaptureRow("facial", CtxPrefix(Now) + "," + string.Join(",", cols));
    }

    //Called by UmaContainerCharacter.SetFootLightParameter with what it writes (_HightLightParam / _HightLightColor).
    //Reference track `foot_light_apply` (game: CharacterObject.SetFootLightParameter: models = "index:BodyTransform:worldY").
    public static void OnFootLightApply(float heightMax, Color color, int blendMode, int easing, Transform rootTransform, float rootY)
    {
        if (!CaptureCollecting) return;
        string models = "0:" + ObjText(rootTransform) + ":" + F(rootY);
        CaptureRow("foot_light_apply", CtxPrefix(Now) + "," + F(heightMax) + "," + C(color) + "," + blendMode + "," + easing + ",1," + Quote(models));
    }
}
