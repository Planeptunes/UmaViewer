using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Erases the painted chest shadows of a unique costume when the head character has less bust than the body character
/// ("Other Characters"/Favorites).
///
/// The shader lights a pixel with `0.4 - NDL * _TripleMaskMap.R`: where R = 0 the pixel is ALWAYS in shadow. The costume
/// textures paint the figure of the bust there (the V of the cleavage, the lines under each breast, the bumps of a tight
/// top), on the skin AND on the cloth that covers it. After the chest is reshaped to a smaller bust those painted shadows
/// would stay, so on a copy of `_TripleMaskMap` the R channel is raised toward 255 in the UV area of the front-facing
/// triangles of the breast zone (the sheer Alpha layers included). Triangles skinned to physics bones (ribbons, loose
/// cloth) are left alone. Strength = (bust body - bust head) / bust body: total when the head character has no bust, zero
/// when the bust type does not change. The neck is not touched (its shadow comes from the head and the hair).
/// </summary>
public static class BodyChestShadow
{
    public static bool Enabled = true;

    private const float ChestMin = 1.08f, ChestMax = 1.28f, ChestHalfWidth = 0.14f;   //breast zone (mesh space, metres)
    private const float FrontNormalZ = 0.25f;                                           //average normal z of a front-facing triangle
    private const int MaskDilate = 2, FeatherRadius = 3;                                //px

    private class Holder : MonoBehaviour
    {
        public readonly List<Texture2D> Textures = new List<Texture2D>();
        private void OnDestroy()
        {
            for (int i = 0; i < Textures.Count; i++) if (Textures[i] != null) Destroy(Textures[i]);
            Textures.Clear();
        }
    }

    private class TexJob
    {
        public Texture2D Triple;
        public byte[] Mask;
    }

    /// <summary>Indexes are the DB `bust` of the body character and of the head character.</summary>
    public static void Apply(UmaContainerCharacter c, string bodyBust, string headBust)
    {
        if (!Enabled || c == null || c.Body == null) return;
        if (!int.TryParse(bodyBust, out int bb) || !int.TryParse(headBust, out int hb) || bb <= 0 || hb >= bb) return;
        float strength = Mathf.Clamp01((bb - hb) / (float)bb);
        try { Run(c, strength); }
        catch (Exception ex) { Debug.LogWarning($"[BodyChestShadow] Failed, the chest keeps its painted shadows: {ex}"); }
    }

    private static void Run(UmaContainerCharacter c, float strength)
    {
        HashSet<string> spring = BodySkinRecolor.CollectSpringBoneNames(c);
        var jobs = new Dictionary<Texture, TexJob>();
        var slots = new List<KeyValuePair<SkinnedMeshRenderer, int>>();

        foreach (var smr in c.Body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null || smr.sharedMesh == null || !smr.sharedMesh.isReadable) continue;
            bool alphaRenderer = smr.name.IndexOf("Alpha", StringComparison.OrdinalIgnoreCase) >= 0;
            Mesh mesh = smr.sharedMesh;
            Vector3[] vs = mesh.vertices, ns = mesh.normals;
            Vector2[] uv = mesh.uv;
            if (ns.Length != vs.Length || uv.Length != vs.Length) continue;
            bool[] cloth = BodySkinRecolor.ClassifyVertices(smr, mesh, spring);
            Material[] mats = smr.sharedMaterials;

            for (int s = 0; s < mats.Length && s < mesh.subMeshCount; s++)
            {
                Material m = mats[s];
                if (m == null || !m.HasProperty("_TripleMaskMap")) continue;
                bool alpha = alphaRenderer || m.name.IndexOf("Alpha", StringComparison.OrdinalIgnoreCase) >= 0;
                var triple = m.GetTexture("_TripleMaskMap") as Texture2D;
                if (triple == null) continue;

                if (!jobs.TryGetValue(triple, out TexJob job))
                {
                    job = new TexJob { Triple = triple, Mask = new byte[triple.width * triple.height] };
                    jobs[triple] = job;
                }
                slots.Add(new KeyValuePair<SkinnedMeshRenderer, int>(smr, s));

                int[] tris = mesh.GetTriangles(s);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                    int cl = (cloth[i0] ? 1 : 0) + (cloth[i1] ? 1 : 0) + (cloth[i2] ? 1 : 0);
                    if (!alpha && cl >= 2) continue;
                    float x = (vs[i0].x + vs[i1].x + vs[i2].x) / 3f;
                    float y = (vs[i0].y + vs[i1].y + vs[i2].y) / 3f;
                    float nz = (ns[i0].z + ns[i1].z + ns[i2].z) / 3f;
                    if (y < ChestMin || y > ChestMax || Mathf.Abs(x) > ChestHalfWidth || nz < FrontNormalZ) continue;
                    RasterTriangle(job.Mask, triple.width, triple.height, uv[i0], uv[i1], uv[i2]);
                }
            }
        }
        if (jobs.Count == 0) return;

        var holder = c.Body.GetComponent<Holder>() ?? c.Body.AddComponent<Holder>();
        var results = new Dictionary<Texture, Texture2D>();
        long changed = 0;
        foreach (var kv in jobs)
        {
            Texture2D edited = Edit(kv.Value, strength, out long px);
            if (edited == null) continue;
            holder.Textures.Add(edited);
            results[kv.Key] = edited;
            changed += px;
        }
        if (results.Count == 0) return;

        var done = new HashSet<SkinnedMeshRenderer>();
        var originalToInstance = new Dictionary<Material, Material>();
        foreach (var rs in slots)
        {
            var smr = rs.Key;
            if (!done.Add(smr)) continue;
            Material[] shared = smr.sharedMaterials;
            Material[] inst = smr.materials;
            for (int s = 0; s < inst.Length && s < shared.Length; s++)
            {
                if (shared[s] == null || inst[s] == null || !shared[s].HasProperty("_TripleMaskMap")) continue;
                originalToInstance[shared[s]] = inst[s];
                var tex = shared[s].GetTexture("_TripleMaskMap");
                if (tex != null && results.TryGetValue(tex, out Texture2D copy)) inst[s].SetTexture("_TripleMaskMap", copy);
            }
        }
        BodySkinRecolor.RemapShaderEffectTargets(c, originalToInstance);
        Debug.Log($"[BodyChestShadow] strength {strength:F2}: {changed} texels of {results.Count} _TripleMaskMap raised toward no-shadow.");
    }

    private static Texture2D Edit(TexJob job, float strength, out long changed)
    {
        changed = 0;
        int w = job.Triple.width, h = job.Triple.height;
        Color32[] px = BodySkinRecolor.ReadPixels(job.Triple, w, h, out bool srgb);
        if (px == null) return null;

        for (int it = 0; it < MaskDilate; it++) Dilate(job.Mask, w, h);
        float[] weight = Feather(job.Mask, w, h, FeatherRadius);
        for (int i = 0; i < px.Length; i++)
        {
            float wt = weight[i];
            if (wt <= 0.001f) continue;
            Color32 p = px[i];
            if (p.r >= 250) continue;
            float k = wt * strength;
            px[i] = new Color32((byte)Mathf.RoundToInt(p.r + (255 - p.r) * k), p.g, p.b, p.a);
            if (k > 0.5f) changed++;
        }
        if (changed == 0) return null;

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, !srgb);
        tex.name = job.Triple.name + "_chestShadow";
        tex.wrapMode = job.Triple.wrapMode;
        tex.filterMode = job.Triple.filterMode;
        tex.anisoLevel = job.Triple.anisoLevel;
        tex.SetPixels32(px);
        tex.Apply(true, true);
        return tex;
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

    private static void Dilate(byte[] m, int w, int h)
    {
        byte[] src = (byte[])m.Clone();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (src[i] != 0) continue;
                if ((x > 0 && src[i - 1] != 0) || (x < w - 1 && src[i + 1] != 0) ||
                    (y > 0 && src[i - w] != 0) || (y < h - 1 && src[i + w] != 0)) m[i] = 1;
            }
    }

    //Separable box blur of a 0/1 mask into 0..1 weights (soft borders).
    private static float[] Feather(byte[] mask, int w, int h, int radius)
    {
        var a = new float[w * h]; var b = new float[w * h];
        for (int i = 0; i < a.Length; i++) a[i] = mask[i];
        float inv = 1f / (2 * radius + 1);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float s = 0f;
                for (int k = -radius; k <= radius; k++) s += a[y * w + Mathf.Clamp(x + k, 0, w - 1)];
                b[y * w + x] = s * inv;
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float s = 0f;
                for (int k = -radius; k <= radius; k++) s += b[Mathf.Clamp(y + k, 0, h - 1) * w + x];
                a[y * w + x] = s * inv;
            }
        return a;
    }
}
