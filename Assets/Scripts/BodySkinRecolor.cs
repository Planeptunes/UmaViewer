using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// Recolors the SKIN of a character-owned (unique) costume body so it matches the skin index of the
/// character that provides the head ("Other Characters"/Favorites: body of B, head of A).
///
/// Why: a unique costume has its skin painted inside its own textures (no per-skin variant), so the
/// body of B keeps B's skin tone next to A's face. Generic costumes are not concerned: they pick the
/// texture by skin index already.
///
/// How (CPU, once per load, on copies of the textures; the bundle assets are never touched):
///  1. Calibration: the generic costumes ship one `_diff` / `_shad_c` per skin index with the same UV
///     and the same clothes, so the pixels that differ between two skin indices ARE skin. From them we
///     get the skin colour statistics (HSV) of both skins and the HSV shift "skin B -> skin A".
///  2. Geometry mask: only UV regions of triangles that are NOT cloth are touched. Cloth = triangles
///     with enough skin weight on physics (CySpring) bones, same criterion as BodyZoneEditor, plus
///     the *_Alpha* layers. This is a hint, not exact (cloth without physics still counts as body).
///  3. Colour key: inside the mask each pixel gets a soft weight by how close it is to skin B (hue,
///     saturation, value); the HSV shift is applied scaled by that weight, so clothes with other colours
///     are left alone and skin shading/details are kept.
/// </summary>
public static class BodySkinRecolor
{
    /// <summary>Master switch (kept as a field so it can be flipped from code while testing).</summary>
    public static bool Enabled = true;

    // ---------------------------------------------------------------- tunables
    private const int ReferenceSize = 512;          //reference textures are compared at this size
    private const float SkinDiffThreshold = 0.06f;  //max per-channel difference (0..1) to call a pixel "skin" in the reference
    private const int MinReferencePixels = 300;     //a reference costume needs at least this many skin pixels
    private const int MaxReferenceCandidates = 4;   //costumes evaluated per skin pair (the one with most skin wins)
    private const float KeyTolerance = 2.5f;        //key width in standard deviations of the reference skin
    private const float MinHueTol = 0.03f, MinSatTol = 0.06f, MinValTol = 0.12f;
    private const int MaskDilate = 2;               //px, so seams/edges of the UV islands are covered

    private static readonly string[] PreferredReferences = { "0007_05_00", "0003_00" };
    private static readonly string[] TexProperties = { "_MainTex", "_ToonMap" };   // diff, shad_c

    // ---------------------------------------------------------------- data
    private class SkinStats
    {
        public float h, s, v, sdH, sdS, sdV;
        public int count;
    }

    private class PairTransform
    {
        public SkinStats fromDiff, toDiff, fromShad, toShad;
    }

    private static readonly Dictionary<string, PairTransform> PairCache = new Dictionary<string, PairTransform>();
    private static readonly HashSet<string> PairFailed = new HashSet<string>();
    private static readonly Regex RefNameRegex =
        new Regex(@"^tex_bdy(\d{4}_\d{2}_\d{2}|\d{4}_\d{2})_(\d)_0_(diff|shad_c)$", RegexOptions.Compiled);

    //Owns the recolored copies so they die with the character.
    private class Holder : MonoBehaviour
    {
        public readonly List<Texture2D> Textures = new List<Texture2D>();
        private void OnDestroy()
        {
            for (int i = 0; i < Textures.Count; i++) if (Textures[i] != null) Destroy(Textures[i]);
            Textures.Clear();
        }
    }

    // ================================================================= entry point
    /// <summary>Recolors the body skin of `c` from skin index `bodySkin` to `headSkin` (both as in the DB).</summary>
    public static void Apply(UmaContainerCharacter c, string bodySkin, string headSkin)
    {
        if (!Enabled || c == null || c.Body == null) return;
        if (!int.TryParse(bodySkin, out int from) || !int.TryParse(headSkin, out int to) || from == to) return;

        try
        {
            var pair = GetPair(from, to);
            if (pair == null)
            {
                Debug.LogWarning($"[BodySkinRecolor] No reference costume with skins {from} and {to}; the body keeps its own skin.");
                return;
            }
            Run(c, pair, from, to);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BodySkinRecolor] Failed, the body keeps its own skin: {ex}");
        }
    }

    // ================================================================= recolor of the character
    private class MaskData
    {
        public int w, h;
        public byte[] body, cloth;
        public bool wholeTexture;     //mesh not readable: no geometry hint
    }

    private static void Run(UmaContainerCharacter c, PairTransform pair, int from, int to)
    {
        HashSet<string> springNames = CollectSpringBoneNames(c);
        var masks = new Dictionary<Texture, MaskData>();
        var rendererSlots = new List<KeyValuePair<SkinnedMeshRenderer, int>>();

        foreach (var smr in c.Body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null || smr.sharedMesh == null) continue;
            if (smr.name.IndexOf("Alpha", StringComparison.OrdinalIgnoreCase) >= 0) continue;   //alpha layers = cloth

            Mesh mesh = smr.sharedMesh;
            bool readable = mesh.isReadable;
            Vector2[] uv = readable ? mesh.uv : null;
            bool[] vertCloth = readable ? ClassifyVertices(smr, mesh, springNames) : null;
            Material[] mats = smr.sharedMaterials;

            for (int s = 0; s < mats.Length; s++)
            {
                Material m = mats[s];
                if (m == null || m.name.IndexOf("Alpha", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                bool any = false;
                foreach (string prop in TexProperties)
                {
                    if (!m.HasProperty(prop)) continue;
                    var tex = m.GetTexture(prop) as Texture2D;
                    if (tex == null) continue;
                    any = true;

                    if (!masks.TryGetValue(tex, out MaskData md))
                    {
                        md = new MaskData { w = tex.width, h = tex.height };
                        md.wholeTexture = !readable || uv == null || uv.Length == 0;
                        if (!md.wholeTexture) { md.body = new byte[md.w * md.h]; md.cloth = new byte[md.w * md.h]; }
                        masks[tex] = md;
                    }
                    if (!md.wholeTexture && s < mesh.subMeshCount) RasterSubmesh(md, mesh, s, uv, vertCloth);
                }
                if (any) rendererSlots.Add(new KeyValuePair<SkinnedMeshRenderer, int>(smr, s));
            }
        }

        if (rendererSlots.Count == 0) return;

        foreach (var md in masks.Values) FinalizeMask(md);

        //--- recolored copy of every source texture (diff with the diff stats, shad_c with the shad stats)
        var holder = c.Body.GetComponent<Holder>() ?? c.Body.AddComponent<Holder>();
        var results = new Dictionary<Texture, Texture2D>();
        long recolored = 0;
        foreach (var kv in masks)
        {
            Texture src = kv.Key;
            bool isToon = IsToonTexture(rendererSlots, src);
            SkinStats a = isToon ? pair.fromShad : pair.fromDiff;
            SkinStats b = isToon ? pair.toShad : pair.toDiff;
            Texture2D copy = RecolorTexture(src, kv.Value, a, b, out long px);
            if (copy == null) continue;
            holder.Textures.Add(copy);
            results[src] = copy;
            recolored += px;
        }
        if (results.Count == 0) return;

        //--- assign on material INSTANCES (the originals are shared bundle assets)
        var done = new HashSet<SkinnedMeshRenderer>();
        var originalToInstance = new Dictionary<Material, Material>();
        foreach (var rs in rendererSlots)
        {
            var smr = rs.Key;
            if (!done.Add(smr)) continue;

            Material[] shared = smr.sharedMaterials;
            Material[] inst = smr.materials;
            for (int s = 0; s < inst.Length && s < shared.Length; s++)
            {
                if (shared[s] == null || inst[s] == null) continue;
                originalToInstance[shared[s]] = inst[s];
                foreach (string prop in TexProperties)
                {
                    if (!shared[s].HasProperty(prop)) continue;
                    var tex = shared[s].GetTexture(prop);
                    if (tex != null && results.TryGetValue(tex, out Texture2D copy)) inst[s].SetTexture(prop, copy);
                }
            }
        }
        RemapShaderEffectTargets(c, originalToInstance);

        Debug.Log($"[BodySkinRecolor] Body skin {from} -> {to}: {results.Count} textures, {recolored} px recolored.");
    }

    private static bool IsToonTexture(List<KeyValuePair<SkinnedMeshRenderer, int>> slots, Texture tex)
    {
        foreach (var rs in slots)
        {
            var mats = rs.Key.sharedMaterials;
            if (rs.Value >= mats.Length || mats[rs.Value] == null) continue;
            if (mats[rs.Value].HasProperty("_ToonMap") && mats[rs.Value].GetTexture("_ToonMap") == tex) return true;
        }
        return false;
    }

    // ---- the shader-effect data (UV scroll emissive) points to the ORIGINAL materials: re-point it.
    private static void RemapShaderEffectTargets(UmaContainerCharacter c, Dictionary<Material, Material> map)
    {
        var data = c.BodyShaderEffectData;
        if (data == null || map.Count == 0) return;

        FieldInfo f = typeof(Gallop.CharaShaderEffectData).GetField("_settingDataArray",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var array = f != null ? f.GetValue(data) as Gallop.CharaShaderEffectData.SettingData[] : null;
        if (array == null) return;
        foreach (var sd in array)
        {
            if (sd != null && sd.TargetMaterial != null && map.TryGetValue(sd.TargetMaterial, out Material inst))
                sd.TargetMaterial = inst;
        }
    }

    // ================================================================= cloth / geometry mask
    //Names of every physics bone of the costume (body cloth + the rest): read from the physics data, which is
    //already loaded when the builder gets here (the controller itself may still be pending).
    private static HashSet<string> CollectSpringBoneNames(UmaContainerCharacter c)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (c.PhysicsContainer == null) return names;
        foreach (var dc in c.PhysicsContainer.GetComponentsInChildren<Gallop.CySpringDataContainer>(true))
        {
            if (dc == null || dc.springParam == null) continue;
            foreach (var p in dc.springParam)
            {
                if (p == null) continue;
                if (!string.IsNullOrEmpty(p._boneName)) names.Add(p._boneName);
                if (p._childElements == null) continue;
                foreach (var ch in p._childElements)
                    if (ch != null && !string.IsNullOrEmpty(ch._boneName)) names.Add(ch._boneName);
            }
        }
        return names;
    }

    private const float ClothWeight = 0.5f;   //same threshold as BodyZoneEditor.clothWeight

    //Cloth = vertices weighted on physics bones. The bust bones (Sp_Ch_Bust*) are physics bones too, but the
    //bust is BODY (skin), so they do not count as cloth: otherwise the chest skin would be left unrecolored.
    private static bool[] ClassifyVertices(SkinnedMeshRenderer smr, Mesh mesh, HashSet<string> springNames)
    {
        Transform[] bones = smr.bones;
        bool[] isSpring = new bool[bones.Length];
        for (int i = 0; i < bones.Length; i++)
            isSpring[i] = bones[i] != null && springNames.Contains(bones[i].name) &&
                          bones[i].name.IndexOf("Bust", StringComparison.OrdinalIgnoreCase) < 0;

        BoneWeight[] weights = mesh.boneWeights;
        bool[] cloth = new bool[mesh.vertexCount];
        for (int i = 0; i < cloth.Length && i < weights.Length; i++)
        {
            BoneWeight w = weights[i];
            float sum = 0f;
            if (w.boneIndex0 < isSpring.Length && isSpring[w.boneIndex0]) sum += w.weight0;
            if (w.boneIndex1 < isSpring.Length && isSpring[w.boneIndex1]) sum += w.weight1;
            if (w.boneIndex2 < isSpring.Length && isSpring[w.boneIndex2]) sum += w.weight2;
            if (w.boneIndex3 < isSpring.Length && isSpring[w.boneIndex3]) sum += w.weight3;
            cloth[i] = sum >= ClothWeight;
        }
        return cloth;
    }

    private static void RasterSubmesh(MaskData md, Mesh mesh, int sub, Vector2[] uv, bool[] vertCloth)
    {
        int[] tris = mesh.GetTriangles(sub);
        for (int t = 0; t + 2 < tris.Length; t += 3)
        {
            int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
            if (i0 >= uv.Length || i1 >= uv.Length || i2 >= uv.Length) continue;

            int c = 0;
            if (vertCloth != null)
            {
                if (vertCloth[i0]) c++;
                if (vertCloth[i1]) c++;
                if (vertCloth[i2]) c++;
            }
            RasterTriangle(c >= 2 ? md.cloth : md.body, md.w, md.h, uv[i0], uv[i1], uv[i2]);
        }
    }

    private static void RasterTriangle(byte[] mask, int w, int h, Vector2 a, Vector2 b, Vector2 c)
    {
        float ax = a.x * w, ay = a.y * h, bx = b.x * w, by = b.y * h, cx = c.x * w, cy = c.y * h;
        int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, Mathf.Min(bx, cx))));
        int maxX = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(ax, Mathf.Max(bx, cx))));
        int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ay, Mathf.Min(by, cy))));
        int maxY = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(ay, Mathf.Max(by, cy))));
        float d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
        if (Mathf.Abs(d) < 1e-6f) return;
        const float eps = 0.02f;

        for (int y = minY; y <= maxY; y++)
        {
            float py = y + 0.5f;
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float l1 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / d;
                float l2 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / d;
                float l3 = 1f - l1 - l2;
                if (l1 >= -eps && l2 >= -eps && l3 >= -eps) mask[y * w + x] = 1;
            }
        }
    }

    //body (dilated) minus cloth. Leaves `body` as the final 0/1 mask.
    private static void FinalizeMask(MaskData md)
    {
        if (md.wholeTexture) return;
        for (int it = 0; it < MaskDilate; it++) Dilate(md.body, md.w, md.h);
        for (int i = 0; i < md.body.Length; i++) if (md.cloth[i] != 0) md.body[i] = 0;
        md.cloth = null;
    }

    private static void Dilate(byte[] m, int w, int h)
    {
        byte[] src = (byte[])m.Clone();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (src[i] != 0) continue;
                if ((x > 0 && src[i - 1] != 0) || (x < w - 1 && src[i + 1] != 0) ||
                    (y > 0 && src[i - w] != 0) || (y < h - 1 && src[i + w] != 0)) m[i] = 1;
            }
        }
    }

    // ================================================================= recolor of one texture
    private static Texture2D RecolorTexture(Texture src, MaskData md, SkinStats from, SkinStats to, out long recoloredPixels)
    {
        recoloredPixels = 0;
        Color32[] px = ReadPixels(src, src.width, src.height, out bool srgb);
        if (px == null) return null;

        float dh = WrapDelta(to.h, from.h);
        float sr = Mathf.Clamp(to.s / Mathf.Max(from.s, 0.02f), 0.25f, 4f);
        float vr = Mathf.Clamp(to.v / Mathf.Max(from.v, 0.02f), 0.4f, 2.5f);

        for (int i = 0; i < px.Length; i++)
        {
            if (!md.wholeTexture && md.body[i] == 0) continue;
            Color32 p = px[i];
            if (p.a == 0) continue;

            Color.RGBToHSV(new Color(p.r / 255f, p.g / 255f, p.b / 255f), out float h, out float s, out float v);
            float w = KeyWeight(h, s, v, from);
            if (w <= 0.001f) continue;

            h = Mathf.Repeat(h + dh * w, 1f);
            s = Mathf.Clamp01(s * Mathf.Lerp(1f, sr, w));
            v = Mathf.Clamp01(v * Mathf.Lerp(1f, vr, w));
            Color o = Color.HSVToRGB(h, s, v);
            px[i] = new Color32((byte)Mathf.RoundToInt(o.r * 255f), (byte)Mathf.RoundToInt(o.g * 255f),
                                (byte)Mathf.RoundToInt(o.b * 255f), p.a);
            if (w > 0.5f) recoloredPixels++;
        }

        var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true, !srgb);
        tex.name = src.name + "_skinRecolor";
        tex.wrapMode = src.wrapMode;
        tex.filterMode = src.filterMode;
        tex.anisoLevel = src.anisoLevel;
        tex.SetPixels32(px);
        tex.Apply(true, true);
        return tex;
    }

    //Soft 0..1 weight: how much a pixel looks like the reference skin (hue, saturation, value).
    private static float KeyWeight(float h, float s, float v, SkinStats st)
    {
        float hTol = Mathf.Max(MinHueTol, st.sdH * KeyTolerance);
        float sTol = Mathf.Max(MinSatTol, st.sdS * KeyTolerance);
        float vTol = Mathf.Max(MinValTol, st.sdV * KeyTolerance);

        float wh = 1f - SmoothStep(hTol, hTol * 2f, Mathf.Abs(WrapDelta(h, st.h)));
        //Hue is meaningless for grey pixels: trust it only as far as the pixel is saturated.
        wh = Mathf.Lerp(1f, wh, Mathf.Clamp01(s / 0.08f));
        float ws = 1f - SmoothStep(sTol, sTol * 2f, Mathf.Abs(s - st.s));
        float wv = 1f - SmoothStep(vTol, vTol * 2f, Mathf.Abs(v - st.v));
        return wh * ws * wv;
    }

    private static float SmoothStep(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / Mathf.Max(b - a, 1e-5f));
        return t * t * (3f - 2f * t);
    }

    private static float WrapDelta(float a, float b)
    {
        float d = a - b;
        return d - Mathf.Round(d);
    }

    // ================================================================= calibration (generic costumes)
    private static PairTransform GetPair(int from, int to)
    {
        string key = from + ">" + to;
        if (PairCache.TryGetValue(key, out PairTransform cached)) return cached;
        if (PairFailed.Contains(key)) return null;

        PairTransform pt = null;
        try { pt = BuildPair(from, to); }
        catch (Exception ex) { Debug.LogWarning($"[BodySkinRecolor] Calibration failed for skins {key}: {ex.Message}"); }

        if (pt != null) PairCache[key] = pt; else PairFailed.Add(key);
        return pt;
    }

    private class RefEntry
    {
        public UmaDatabaseEntry diffFrom, diffTo, shadFrom, shadTo;
    }

    private static PairTransform BuildPair(int from, int to)
    {
        var main = UmaViewerMain.Instance;
        if (main == null || main.AbChara == null) return null;

        //costume -> (skin, kind) -> entry, only bust 0 and no "_wet"
        var table = new Dictionary<string, Dictionary<string, UmaDatabaseEntry>>();
        foreach (var e in main.AbChara)
        {
            if (e == null || e.Name == null || !e.Name.StartsWith(UmaDatabaseController.BodyPath)) continue;
            Match m = RefNameRegex.Match(Path.GetFileName(e.Name));
            if (!m.Success) continue;
            string costume = m.Groups[1].Value;
            if (!table.TryGetValue(costume, out var skins)) table[costume] = skins = new Dictionary<string, UmaDatabaseEntry>();
            skins[m.Groups[2].Value + "_" + m.Groups[3].Value] = e;
        }

        var candidates = new List<string>();
        foreach (string pref in PreferredReferences) if (table.ContainsKey(pref)) candidates.Add(pref);
        foreach (string k in table.Keys.OrderBy(x => x, StringComparer.Ordinal)) if (!candidates.Contains(k)) candidates.Add(k);

        PairTransform best = null;
        int bestCount = 0, tried = 0;
        foreach (string costume in candidates)
        {
            if (tried >= MaxReferenceCandidates) break;
            var t = table[costume];
            string kf = from + "_", kt = to + "_";
            if (!t.ContainsKey(kf + "diff") || !t.ContainsKey(kf + "shad_c") ||
                !t.ContainsKey(kt + "diff") || !t.ContainsKey(kt + "shad_c")) continue;
            tried++;

            var re = new RefEntry
            {
                diffFrom = t[kf + "diff"], shadFrom = t[kf + "shad_c"],
                diffTo = t[kt + "diff"], shadTo = t[kt + "shad_c"]
            };
            PairTransform pt = EvaluateReference(re, out int count);
            if (pt != null && count > bestCount) { best = pt; bestCount = count; }
        }
        return bestCount >= MinReferencePixels ? best : null;
    }

    private static PairTransform EvaluateReference(RefEntry re, out int skinPixels)
    {
        skinPixels = 0;
        Color32[] dFrom = ReadEntry(re.diffFrom);
        Color32[] dTo = ReadEntry(re.diffTo);
        Color32[] sFrom = ReadEntry(re.shadFrom);
        Color32[] sTo = ReadEntry(re.shadTo);
        if (dFrom == null || dTo == null || sFrom == null || sTo == null) return null;

        //skin = the pixels that change between the two skin indices
        var idx = new List<int>();
        int thr = Mathf.RoundToInt(SkinDiffThreshold * 255f);
        for (int i = 0; i < dFrom.Length; i++)
        {
            int d = Mathf.Max(Mathf.Abs(dFrom[i].r - dTo[i].r),
                    Mathf.Max(Mathf.Abs(dFrom[i].g - dTo[i].g), Mathf.Abs(dFrom[i].b - dTo[i].b)));
            if (d > thr && dFrom[i].a > 0 && dTo[i].a > 0) idx.Add(i);
        }
        skinPixels = idx.Count;
        if (idx.Count < MinReferencePixels) return null;

        return new PairTransform
        {
            fromDiff = Stats(dFrom, idx), toDiff = Stats(dTo, idx),
            fromShad = Stats(sFrom, idx), toShad = Stats(sTo, idx)
        };
    }

    private static SkinStats Stats(Color32[] px, List<int> idx)
    {
        double sx = 0, sy = 0, ss = 0, sv = 0;
        var hs = new float[idx.Count]; var ss2 = new float[idx.Count]; var vs = new float[idx.Count];
        for (int k = 0; k < idx.Count; k++)
        {
            Color32 p = px[idx[k]];
            Color.RGBToHSV(new Color(p.r / 255f, p.g / 255f, p.b / 255f), out float h, out float s, out float v);
            hs[k] = h; ss2[k] = s; vs[k] = v;
            sx += Math.Cos(h * 2.0 * Math.PI); sy += Math.Sin(h * 2.0 * Math.PI);
            ss += s; sv += v;
        }
        int n = idx.Count;
        var st = new SkinStats { count = n };
        st.h = Mathf.Repeat((float)(Math.Atan2(sy, sx) / (2.0 * Math.PI)), 1f);
        st.s = (float)(ss / n);
        st.v = (float)(sv / n);

        double dh2 = 0, ds2 = 0, dv2 = 0;
        for (int k = 0; k < n; k++)
        {
            float dh = WrapDelta(hs[k], st.h);
            dh2 += dh * dh;
            ds2 += (ss2[k] - st.s) * (ss2[k] - st.s);
            dv2 += (vs[k] - st.v) * (vs[k] - st.v);
        }
        st.sdH = (float)Math.Sqrt(dh2 / n);
        st.sdS = (float)Math.Sqrt(ds2 / n);
        st.sdV = (float)Math.Sqrt(dv2 / n);
        return st;
    }

    //Loads the texture of a reference bundle, copies its pixels (reference size) and releases the bundle.
    private static Color32[] ReadEntry(UmaDatabaseEntry entry)
    {
        string name = Path.GetFileName(entry.Name);
        Color32[] result = null;
        try
        {
            Texture2D tex = null;
            foreach (var t in entry.GetAll<Texture2D>())
            {
                if (t != null && t.name == name) { tex = t; break; }
            }
            if (tex != null) result = ReadPixels(tex, ReferenceSize, ReferenceSize, out _);
        }
        finally
        {
            try { UmaAssetManager.UnloadAssetBundle(entry, false); } catch { /* best effort */ }
        }
        return result;
    }

    // ================================================================= texture -> CPU pixels
    //GPU blit to a temporary RT (the bundle textures are not readable) and read back, keeping the colour space.
    private static Color32[] ReadPixels(Texture tex, int w, int h, out bool srgb)
    {
        srgb = GraphicsFormatUtility.IsSRGBFormat(tex.graphicsFormat);
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32,
            srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
        RenderTexture prev = RenderTexture.active;
        Texture2D tmp = null;
        try
        {
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            tmp = new Texture2D(w, h, TextureFormat.RGBA32, false, !srgb);
            tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tmp.Apply(false);
            return tmp.GetPixels32();
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            if (tmp != null) UnityEngine.Object.Destroy(tmp);
        }
    }
}
