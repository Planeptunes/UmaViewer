using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

//(Probe capture) Three more files of the viewer's Momentum capture, written with the file names and columns of the
//fixed reference capture format, for comparison with reference data column by column:
//   state_trace.csv            chara / charabone / prop / propnode transforms
//   monitor_trace.csv          the monitor nodes: layer, visibility, which camera draws them, material + MPB values
//   render_cyspring_calls.csv  one row per native CySpringPlugin call (rates, wind, flags, skirt evaluation)
//   render_cyspring_bones.csv  one row per bone of the chain (parameters, positions before/after, final rotation)
//Read-only: nothing here changes what is rendered.
//
//Quaternion column order: the reference format stores Transform.rotation permuted, so its rotX,rotY,rotZ,rotW
//columns hold (unity y, unity z, unity w, unity x) (identity shows as 0,0,1,0; verified against the camera of
//Live 1001 at frames 1550/3248/3919). The viewer writes the same permutation in the files that follow the reference
//format so the columns match; QuatCols is the single place to change if the reference format ever changes.
public static class LiveProbeTraces
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const int MaxDepth = 32;
    private const int MaxStateNodes = 3000;
    private const int MaxMonitorNodes = 400;

    public static string F5(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "nan" : v.ToString("F5", Inv);
    public static string F6(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "" : v.ToString("F6", Inv);
    private static string Q(string s) => "\"" + (s ?? "").Replace('"', '\'') + "\"";

    //Four quaternion columns in the reference format's order (see header).
    public static string QuatCols(Quaternion q) => F5(q.y) + "," + F5(q.z) + "," + F5(q.w) + "," + F5(q.x);

    //The game's root is "Director(Clone)"; the viewer's is "Director". Same path text in the shared files.
    private static string GameRootName(Transform root) => root.name.EndsWith("(Clone)", StringComparison.Ordinal) ? root.name : root.name + "(Clone)";

    // ---------------------------------------------------------------- state_trace.csv

    private struct StateNode { public string Kind; public string Path; public Transform T; public int Slot; }

    private static void CollectState(Transform t, string parentPath, int depth, bool inChara, bool inProp, int slot, List<StateNode> output)
    {
        if (t == null || depth > MaxDepth || output.Count >= MaxStateNodes) return;
        string name = t.name;
        string path = parentPath.Length == 0 ? name : parentPath + "/" + name;
        bool chara = inChara, prop = inProp;
        bool isChara = name.StartsWith("chara_", StringComparison.OrdinalIgnoreCase);
        if (isChara)
        {
            //The reference format falls back to the `CharacterObjectNN` index when no character census is available; the viewer has no census.
            int p = parentPath.LastIndexOf("CharacterObject", StringComparison.Ordinal);
            slot = -1;
            if (p >= 0)
            {
                int i = p + 15, n = 0, digits = 0;
                while (i < parentPath.Length && char.IsDigit(parentPath[i])) { n = n * 10 + (parentPath[i] - '0'); i++; digits++; }
                if (digits > 0) slot = n;
            }
        }
        string kind = null;
        if (isChara) { chara = true; kind = "chara"; }
        else if (name.StartsWith("LiveProps[", StringComparison.Ordinal) || name.StartsWith("pfb_chr_prop", StringComparison.Ordinal)) { prop = true; kind = "prop"; }
        else if (inProp && (name == "M_Prop" || name == "Root" || name.StartsWith("pfb_", StringComparison.Ordinal) ||
                            name.StartsWith("Wrist_", StringComparison.Ordinal) || name.StartsWith("Elbow_", StringComparison.Ordinal))) kind = "propnode";
        else if (inChara && (name == "Position" || name == "Hip" || name == "Head" || name == "Hand_Attach_L" || name == "Hand_Attach_R" ||
                             name == "Wrist_L" || name == "Wrist_R")) kind = "charabone";
        if (kind != null) output.Add(new StateNode { Kind = kind, Path = path, T = t, Slot = slot });
        for (int i = 0; i < t.childCount; i++) CollectState(t.GetChild(i), path, depth + 1, chara, prop, slot, output);
    }

    public static string WriteStateTrace(string dir, int live, int frame, Transform root)
    {
        if (root == null) return "[state] skipped: no Director root";
        var nodes = new List<StateNode>();
        CollectState(root, "", 0, false, false, -1, nodes);
        //The viewer's models are NOT children of Director/CharacterObjectN (those are empty stand-position nodes):
        //every UmaContainerCharacter of Director.CharaContainerScript is walked as the game's
        //"CharacterObjectNN/chara_xxx" subtree, with the same bone-name rules and a synthetic game-style path.
        var director = root.GetComponent<Gallop.Live.Director>();
        if (director != null && director.CharaContainerScript != null)
        {
            string rootName = GameRootName(root);
            for (int k = 0; k < director.CharaContainerScript.Count; k++)
            {
                UmaContainerCharacter c = director.CharaContainerScript[k];
                if (c == null) continue;
                Transform ct = c.transform;
                string modelName = ct.name.StartsWith("chara_", StringComparison.OrdinalIgnoreCase) ? ct.name : "chara_" + ct.name;
                string parent = rootName + "/CharacterObject" + k.ToString("00", Inv);
                string path = parent + "/" + modelName;
                nodes.Add(new StateNode { Kind = "chara", Path = path, T = ct, Slot = k });
                for (int i = 0; i < ct.childCount; i++) CollectState(ct.GetChild(i), path, 1, true, false, k, nodes);
            }
        }
        var sb = new StringBuilder("live,run,frame,kind,slot,modelIdx,standing,path,activeSelf,wposX,wposY,wposZ,wrotX,wrotY,wrotZ,wrotW,lossyX,lossyY,lossyZ,lposX,lposY,lposZ,lrotX,lrotY,lrotZ,lrotW\n");
        string rootGame = GameRootName(root);
        foreach (StateNode n in nodes)
        {
            Transform t = n.T;
            if (t == null) continue;
            //Replace the first path segment by the game's root name.
            string path = n.Path;
            int slash = path.IndexOf('/');
            path = rootGame + (slash >= 0 ? path.Substring(slash) : "");
            Vector3 wp = t.position, ls = t.lossyScale, lp = t.localPosition;
            int slot = n.Slot, model = slot < 0 ? -1 : 0, standing = slot;
            sb.Append(live).Append(",1,").Append(frame).Append(',').Append(n.Kind).Append(',').Append(slot).Append(',').Append(model).Append(',').Append(standing)
              .Append(',').Append(Q(path)).Append(',').Append(t.gameObject.activeSelf ? 1 : 0)
              .Append(',').Append(F5(wp.x)).Append(',').Append(F5(wp.y)).Append(',').Append(F5(wp.z))
              .Append(',').Append(QuatCols(t.rotation))
              .Append(',').Append(F5(ls.x)).Append(',').Append(F5(ls.y)).Append(',').Append(F5(ls.z))
              .Append(',').Append(F5(lp.x)).Append(',').Append(F5(lp.y)).Append(',').Append(F5(lp.z))
              .Append(',').Append(QuatCols(t.localRotation)).Append('\n');
        }
        File.WriteAllText(Path.Combine(dir, "state_trace.csv"), sb.ToString(), new UTF8Encoding(false));
        return "[state] state_trace.csv (" + nodes.Count + " nodes; rotation columns in the reference format's order)";
    }

    // ---------------------------------------------------------------- monitor_trace.csv

    //Monitor node names and the monitor property list (with value types) of the reference format.
    private static readonly string[] MonitorFilters = { "monitor_side_frame", "monitor_r_s_frame", "monitor_l_s_frame", "monitor_l_b_frame", "monitor_r_b_frame", "monitor00" };
    private static readonly string[,] MonitorProps =
    {
        {"_Color","Color"},{"_ColorPower","Float"},{"_Alpha","Float"},{"_Alpha2","Float"},{"_MaskAlpha","Float"},{"_AlphaParam","Vector"},
        {"_Cutoff","Float"},{"_Intensity","Float"},{"_EmissiveColor","Color"},{"_EmissiveIntensity","Float"},{"_Bloom","Float"},
        {"_BloomHighRange","Float"},{"_bloomHighRangeIntensity","Float"},{"_bloomDofWeight","Float"},{"_BloomIsScreenBlend","Float"},
        {"_PostFilmIsAlphaMasking","Float"},{"_Stencil","Float"},{"_StencilComp","Float"},{"_StencilOp","Float"},{"_StencilMask","Float"},
        {"_Brightness","Float"},{"_BlendFactor","Float"},{"_BaseColor","Color"},{"_ColorFade","Color"},{"_UVAdjust","Vector"},
        {"_MonitorWidth","Float"},{"_MonitorHeight","Float"},{"_AlphaCutoff","Float"},{"_AmbientColor","Color"},{"_MulColor0","Color"},
        {"_MainTex","Tex"},{"_FilterTex","Tex"},{"_FadeTex","Tex"},{"_SrcBlend","Float"},{"_DstBlend","Float"},{"_ZWrite","Float"},
        {"_ZTest","Float"},{"_Cull","Float"},
    };

    private static void CollectMonitor(Transform t, string parentPath, int depth, List<KeyValuePair<Transform, string>> output)
    {
        if (t == null || depth > MaxDepth || output.Count >= MaxMonitorNodes) return;
        string path = parentPath.Length == 0 ? t.name : parentPath + "/" + t.name;
        string lower = t.name.ToLowerInvariant();
        foreach (string f in MonitorFilters)
            if (lower.Contains(f)) { output.Add(new KeyValuePair<Transform, string>(t, path)); break; }
        for (int i = 0; i < t.childCount; i++) CollectMonitor(t.GetChild(i), path, depth + 1, output);
    }

    private static string V4s(Vector4 v) => "(" + F5(v.x) + "," + F5(v.y) + "," + F5(v.z) + "," + F5(v.w) + ")";

    public static string WriteMonitorTrace(string dir, int live, int frame, Transform root)
    {
        if (root == null) return "[monitor] skipped: no Director root";
        var nodes = new List<KeyValuePair<Transform, string>>();
        CollectMonitor(root, "", 0, nodes);
        var sb = new StringBuilder("live,run,frame,path,layer,activeSelf,inHierarchy,rendererEnabled,isVisible,drawnBy,material,shader,queue,props,sortingOrder,mpb\n");
        string rootGame = GameRootName(root);
        var cams = new List<KeyValuePair<string, int>>();
        foreach (Camera c in Camera.allCameras)
            if (c != null && c.enabled) cams.Add(new KeyValuePair<string, int>(c.transform.name, c.cullingMask));
        var block = new MaterialPropertyBlock();
        foreach (var pair in nodes)
        {
            Transform t = pair.Key;
            if (t == null) continue;
            string path = pair.Value;
            int slash = path.IndexOf('/');
            path = rootGame + (slash >= 0 ? path.Substring(slash) : "");
            GameObject go = t.gameObject;
            int layer = go.layer;
            var drawn = new List<string>();
            foreach (var c in cams) if (layer >= 0 && layer < 32 && ((c.Value >> layer) & 1) != 0) drawn.Add(c.Key);
            string drawnBy = string.Join("|", drawn);
            foreach (Renderer rend in go.GetComponents<Renderer>())
            {
                if (rend == null) continue;
                string head = live + ",1," + frame + "," + Q(path) + "," + layer + "," + (go.activeSelf ? 1 : 0) + "," + (go.activeInHierarchy ? 1 : 0) + "," +
                              (rend.enabled ? 1 : 0) + "," + (rend.isVisible ? 1 : 0) + "," + Q(drawnBy);
                var mpb = new StringBuilder();
                if (rend.HasPropertyBlock())
                {
                    rend.GetPropertyBlock(block);
                    for (int i = 0; i < MonitorProps.GetLength(0); i++)
                    {
                        string name = MonitorProps[i, 0], type = MonitorProps[i, 1];
                        if (!block.HasProperty(name)) continue;
                        string v;
                        switch (type)
                        {
                            case "Color": { Color c = block.GetColor(name); v = V4s(new Vector4(c.r, c.g, c.b, c.a)); break; }
                            case "Vector": v = V4s(block.GetVector(name)); break;
                            case "Float": v = F5(block.GetFloat(name)); break;
                            default: { Texture tx = block.GetTexture(name); v = tx == null ? "null" : tx.name; break; }
                        }
                        mpb.Append(name).Append('=').Append(v).Append(';');
                    }
                }
                Material[] mats = rend.sharedMaterials;
                bool wrote = false;
                foreach (Material mat in mats)
                {
                    if (mat == null) continue;
                    var props = new StringBuilder();
                    for (int i = 0; i < MonitorProps.GetLength(0); i++)
                    {
                        string name = MonitorProps[i, 0], type = MonitorProps[i, 1];
                        if (!mat.HasProperty(name)) continue;
                        string v;
                        switch (type)
                        {
                            case "Color": { Color c = mat.GetColor(name); v = V4s(new Vector4(c.r, c.g, c.b, c.a)); break; }
                            case "Vector": v = V4s(mat.GetVector(name)); break;
                            case "Float": v = F5(mat.GetFloat(name)); break;
                            default: { Texture tx = mat.GetTexture(name); v = tx == null ? "null" : tx.name; break; }
                        }
                        props.Append(name).Append('=').Append(v).Append(';');
                    }
                    sb.Append(head).Append(',').Append(Q(mat.name)).Append(',').Append(Q(mat.shader != null ? mat.shader.name : "null")).Append(',')
                      .Append(mat.renderQueue).Append(',').Append(Q(props.ToString())).Append(',').Append(rend.sortingOrder).Append(',').Append(Q(mpb.ToString())).Append('\n');
                    wrote = true;
                }
                if (!wrote) sb.Append(head).Append(",,,,,,").Append(Q(mpb.ToString())).Append('\n');
            }
        }
        File.WriteAllText(Path.Combine(dir, "monitor_trace.csv"), sb.ToString(), new UTF8Encoding(false));
        return "[monitor] monitor_trace.csv (" + nodes.Count + " nodes)";
    }

    // ---------------------------------------------------------------- CySpring (calls + bones)

    private const int BoneStride = 0x168;
    private static readonly StringBuilder _calls = new StringBuilder();
    private static readonly StringBuilder _bones = new StringBuilder();
    private static bool _cyArmed;
    private static int _cySeq;

    //(Motion Probe, viewer tool) Motion mode: records every native call of each stepped frame (LiveProbeMotion), not the
    //one-frame timeline tick of Momentum. Rows are cleared by the motion driver between frames.
    private static bool _motionArmed;

    public static void CySpringArm() { _cyArmed = true; _cySeq = 0; _calls.Length = 0; _bones.Length = 0; }
    public static void CySpringDisarm() { _cyArmed = false; }
    public static void CySpringArmMotion() { _motionArmed = true; CySpringResetRows(); }
    public static void CySpringDisarmMotion() { _motionArmed = false; CySpringResetRows(); }
    public static void CySpringResetRows() { _cySeq = 0; _calls.Length = 0; _bones.Length = 0; }

    //True only for the calls that happen after the capture frame's timeline update (the frame the reference format covers),
    //or, in Motion mode, for every call while the motion capture runs.
    public static bool CySpringActive => (_cyArmed && LiveTraceExporter.CaptureReady) || _motionArmed;

    private static float RF(IntPtr b, int off) => BitConverter.Int32BitsToSingle(Marshal.ReadInt32(b, off));
    private static int RI(IntPtr b, int off) => Marshal.ReadInt32(b, off);

    //TargetPosition (+0x40) of every bone before the solver runs.
    public static float[] CySpringSnapshot(IntPtr cond, int n)
    {
        if (cond == IntPtr.Zero || n < 1 || n > 512) return null;
        var s = new float[n * 3];
        for (int i = 0; i < n; i++)
        {
            s[i * 3] = RF(cond, i * BoneStride + 0x40);
            s[i * 3 + 1] = RF(cond, i * BoneStride + 0x44);
            s[i * 3 + 2] = RF(cond, i * BoneStride + 0x48);
        }
        return s;
    }

    //SkirtWorking +0x50 (the skirt evaluation value of the reference format), NaN when the pointer is null.
    public static float CySpringSkirtEval(IntPtr work) => work == IntPtr.Zero ? float.NaN : RF(work, 0x50);

    public static void CySpringAddCloth(string kind, IntPtr cond, int n, float[] before, float st, float dr, float gr, float wx, float wy, float wz, float ws,
                                        bool collisionSwitch, float timescale, bool is60, float mv, float amv, float sp, float evBefore, float evAfter)
    {
        if (before == null) return;
        int seq = _cySeq++;
        _calls.Append(0).Append(',').Append(seq).Append(',').Append(kind).Append(",0x").Append(cond.ToInt64().ToString("x", Inv)).Append(',').Append(n)
              .Append(',').Append(F6(st)).Append(',').Append(F6(dr)).Append(',').Append(F6(gr)).Append(',').Append(F6(wx)).Append(',').Append(F6(wy)).Append(',').Append(F6(wz)).Append(',').Append(F6(ws))
              .Append(',').Append(collisionSwitch ? 1 : 0).Append(',').Append(F6(timescale)).Append(',').Append(is60 ? 1 : 0)
              .Append(',').Append(F6(mv)).Append(',').Append(F6(amv)).Append(',').Append(F6(sp))
              .Append(',').Append(F6(evBefore)).Append(',').Append(F6(evAfter)).Append('\n');
        for (int i = 0; i < before.Length / 3; i++)
        {
            int o = i * BoneStride;
            _bones.Append(0).Append(',').Append(seq).Append(",0x").Append(cond.ToInt64().ToString("x", Inv)).Append(',').Append(i)
                  .Append(',').Append(RI(cond, o + 0xe8)).Append(',').Append(F6(RF(cond, o + 0xc0))).Append(',').Append(F6(RF(cond, o + 0xc4)))
                  .Append(',').Append(F6(RF(cond, o + 0xc8))).Append(',').Append(F6(RF(cond, o + 0xcc))).Append(',').Append(F6(RF(cond, o + 0xd0)))
                  .Append(',').Append(F6(RF(cond, o + 0x104))).Append(',').Append(RI(cond, o + 0xf0)).Append(',').Append(RI(cond, o + 0xec))
                  .Append(',').Append(RI(cond, o + 0x164)).Append(',').Append(F6(RF(cond, o + 0x13c))).Append(',').Append(RI(cond, o + 0xe4))
                  .Append(',').Append(F6(RF(cond, o + 0x90))).Append(',').Append(F6(RF(cond, o + 0x94))).Append(',').Append(F6(RF(cond, o + 0x98)))
                  .Append(',').Append(F6(before[i * 3])).Append(',').Append(F6(before[i * 3 + 1])).Append(',').Append(F6(before[i * 3 + 2]))
                  .Append(',').Append(F6(RF(cond, o + 0x40))).Append(',').Append(F6(RF(cond, o + 0x44))).Append(',').Append(F6(RF(cond, o + 0x48)))
                  .Append(',').Append(F6(RF(cond, o + 0x20))).Append(',').Append(F6(RF(cond, o + 0x24))).Append(',').Append(F6(RF(cond, o + 0x28))).Append(',').Append(F6(RF(cond, o + 0x2c)))
                  .Append(',').Append(RI(cond, o + 0x140)).Append('\n');
        }
    }

    public static void CySpringAddSkirt(IntPtr work, float evBefore, float evAfter)
    {
        int seq = _cySeq++;
        _calls.Append(0).Append(',').Append(seq).Append(",skirt,0x").Append(work.ToInt64().ToString("x", Inv))
              .Append(",,,,,,,,,,,,,,,").Append(F6(evBefore)).Append(',').Append(F6(evAfter)).Append('\n');
    }

    public static string WriteCySpring(string dir, int frame)
    {
        const string callsHeader = "frame,seq,kind,chain,nCond,stiffnessRate,dragRate,gravityRate,windX,windY,windZ,windStrength,collisionSwitch,timescale,is60FPS,moveRate,addMoveRate,springRate,skirtEvaluationBefore,skirtEvaluationAfter\n";
        const string bonesHeader = "frame,seq,chain,bone,isSkip,initBoneDistance,stiffness,drag,collisionRadius,gravity,dynamicRatio,activeCollision,isLimit,isAddSpring,moveSpringApplyRate,checkCharaCollision,selfX,selfY,selfZ,targetBeforeX,targetBeforeY,targetBeforeZ,targetAfterX,targetAfterY,targetAfterZ,finalRotX,finalRotY,finalRotZ,finalRotW,parentWorkIndex\n";
        string calls = _calls.ToString(), bones = _bones.ToString();
        //Rows were built with a 0 placeholder frame: stamp the capture frame.
        string fs = frame.ToString(Inv);
        calls = StampFrame(calls, fs);
        bones = StampFrame(bones, fs);
        File.WriteAllText(Path.Combine(dir, "render_cyspring_calls.csv"), callsHeader + calls, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "render_cyspring_bones.csv"), bonesHeader + bones, new UTF8Encoding(false));
        int callCount = _cySeq;
        return "[cyspring] render_cyspring_calls.csv / render_cyspring_bones.csv (" + callCount + " native calls after the capture frame's timeline update" +
               (callCount == 0 ? "; none: the live is paused or the native plugin is off" : "") + ")";
    }

    private static string StampFrame(string rows, string frame)
    {
        if (rows.Length == 0) return rows;
        var sb = new StringBuilder(rows.Length + 64);
        foreach (string line in rows.Split('\n'))
        {
            if (line.Length == 0) continue;
            sb.Append(frame).Append(line, 1, line.Length - 1).Append('\n');
        }
        return sb.ToString();
    }
}
