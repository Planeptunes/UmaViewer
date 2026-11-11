using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reshapes the body of character B (unique costume) to the body type of the head character A
/// ("Other Characters"/Favorites): chest, thighs and calves, skin and costume together.
///
/// Every character has a body TYPE (DB height/shape/bust) and the generic costume 0004_00_00 exists for the types in use.
/// Those generic meshes share topology and skeleton, so `delta = generic(A) - generic(B)` is the real difference between the
/// two body types. The delta is transferred to the vertices of the unique costume (4 nearest generic vertices of B's type,
/// full effect up to 2 cm from the surface, fading to 0 at 8 cm, so skirts and capes that are far from the body stay as they
/// are). In the chest band the cloth is then pulled 60% of the way toward the skin of A's type along its normal, because a
/// costume keeps its own thickness over the skin it was made for. Works on a clone of each M_Body* mesh (the bundle asset is
/// never touched).
/// </summary>
public static class BodyTypeMorph
{
    private const float ChestMin = 1.04f, ChestMax = 1.30f, ChestFade = 0.03f;   //chest band (mesh space, metres): cloth pull
    private const float FullDist = 0.02f, ZeroDist = 0.08f;                      //distance to the generic surface: full effect / none
    private const float Alpha = 0.6f;                                            //share of the cloth-to-skin gap removed in the chest
    private const float CellSize = ZeroDist;                                     //search grid: one cell reaches the zero-effect distance

    private class TypeData { public Vector3[] V, N; }
    private static readonly Dictionary<string, TypeData> Cache = new Dictionary<string, TypeData>();

    private class MeshHolder : MonoBehaviour
    {
        public readonly List<Mesh> Meshes = new List<Mesh>();
        private void OnDestroy() { foreach (var m in Meshes) if (m != null) Destroy(m); Meshes.Clear(); }
    }

    private class Grid
    {
        private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
        private readonly Vector3[] pts;
        public Grid(Vector3[] points)
        {
            pts = points;
            for (int i = 0; i < points.Length; i++)
            {
                long k = Key(points[i]);
                if (!cells.TryGetValue(k, out List<int> list)) { list = new List<int>(); cells[k] = list; }
                list.Add(i);
            }
        }
        private static int Cell(float v) { return Mathf.FloorToInt(v / CellSize); }
        private static long Key(int x, int y, int z) { return ((long)(x + 1024) << 40) | ((long)(y + 1024) << 20) | (long)(z + 1024); }
        private static long Key(Vector3 p) { return Key(Cell(p.x), Cell(p.y), Cell(p.z)); }

        public void Candidates(Vector3 p, List<int> into)
        {
            into.Clear();
            int cx = Cell(p.x), cy = Cell(p.y), cz = Cell(p.z);
            for (int x = cx - 1; x <= cx + 1; x++)
                for (int y = cy - 1; y <= cy + 1; y++)
                    for (int z = cz - 1; z <= cz + 1; z++)
                        if (cells.TryGetValue(Key(x, y, z), out List<int> list)) into.AddRange(list);
        }
    }

    /// <summary>Types are the DB height/shape/bust of the body character and of the head character.</summary>
    public static void Apply(UmaContainerCharacter c, string bodyH, string bodyS, string bodyB, string headH, string headS, string headB)
    {
        if (c == null || c.Body == null) return;
        string keyBody = $"{bodyH}_{bodyS}_{bodyB}", keyHead = $"{headH}_{headS}_{headB}";
        if (keyBody == keyHead) return;

        TypeData tb = GetType(keyBody), ta = GetType(keyHead);
        if (tb == null || ta == null || tb.V.Length != ta.V.Length)
        {
            Debug.Log($"[BodyTypeMorph] No generic body to compare for types {keyBody} -> {keyHead}: the body is left as it is.");
            return;
        }

        var delta = new Vector3[tb.V.Length]; var deltaN = new Vector3[tb.V.Length];
        for (int i = 0; i < delta.Length; i++) { delta[i] = ta.V[i] - tb.V[i]; deltaN[i] = ta.N[i] - tb.N[i]; }
        var gridB = new Grid(tb.V); var gridA = new Grid(ta.V);

        int total = 0;
        foreach (var smr in c.Body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null || smr.sharedMesh == null || !smr.sharedMesh.isReadable) continue;
            if (!smr.name.StartsWith("M_Body", StringComparison.Ordinal)) continue;
            total += Morph(c, smr, tb, ta, gridB, gridA, delta, deltaN);
        }
        Debug.Log($"[BodyTypeMorph] body type {keyBody} -> {keyHead}: {total} vertices moved.");
    }

    private static int Morph(UmaContainerCharacter c, SkinnedMeshRenderer smr, TypeData tb, TypeData ta, Grid gridB, Grid gridA, Vector3[] delta, Vector3[] deltaN)
    {
        Mesh mesh = smr.sharedMesh;
        Vector3[] vs = mesh.vertices, ns = mesh.normals;
        bool hasN = ns.Length == vs.Length;
        var nv = (Vector3[])vs.Clone();
        var nn = hasN ? (Vector3[])ns.Clone() : null;
        var cand = new List<int>();
        int moved = 0;

        for (int i = 0; i < vs.Length; i++)
        {
            //4 nearest generic vertices of B's type
            gridB.Candidates(vs[i], cand);
            float d0 = float.MaxValue, d1 = float.MaxValue, d2 = float.MaxValue, d3 = float.MaxValue;
            int i0 = -1, i1 = -1, i2 = -1, i3 = -1;
            for (int a = 0; a < cand.Count; a++)
            {
                int j = cand[a];
                float d = (vs[i] - tb.V[j]).sqrMagnitude;
                if (d >= d3) continue;
                if (d < d0) { d3 = d2; i3 = i2; d2 = d1; i2 = i1; d1 = d0; i1 = i0; d0 = d; i0 = j; }
                else if (d < d1) { d3 = d2; i3 = i2; d2 = d1; i2 = i1; d1 = d; i1 = j; }
                else if (d < d2) { d3 = d2; i3 = i2; d2 = d; i2 = j; }
                else { d3 = d; i3 = j; }
            }
            if (i0 < 0) continue;
            float dist = Mathf.Sqrt(d0);
            float f = dist <= FullDist ? 1f : Mathf.Clamp01(1f - (dist - FullDist) / (ZeroDist - FullDist));
            if (f <= 0f) continue;

            float[] dd = { d0, d1, d2, d3 }; int[] ii = { i0, i1, i2, i3 };
            Vector3 acc = Vector3.zero, accN = Vector3.zero; float ws = 0f;
            for (int k = 0; k < 4; k++)
            {
                if (ii[k] < 0) continue;
                float w = 1f / (dd[k] + 1e-6f);
                acc += delta[ii[k]] * w; accN += deltaN[ii[k]] * w; ws += w;
            }
            Vector3 move = acc / ws * f;
            if (move.sqrMagnitude < 1e-8f) continue;
            Vector3 p1 = vs[i] + move;

            //chest: pull the cloth toward the skin of A's type (nearest vertex of A's generic body and its normal)
            float chest = ChestMask(vs[i].y);
            if (chest > 0f)
            {
                gridA.Candidates(p1, cand);
                float bd = float.MaxValue; int bj = -1;
                for (int a = 0; a < cand.Count; a++)
                {
                    int j = cand[a];
                    float d = (p1 - ta.V[j]).sqrMagnitude;
                    if (d < bd) { bd = d; bj = j; }
                }
                if (bj >= 0)
                {
                    Vector3 nrm = ta.N[bj].normalized;
                    float gap = Vector3.Dot(p1 - ta.V[bj], nrm);
                    float mt = Mathf.Clamp01((move.magnitude - 0.005f) / 0.015f); mt = mt * mt * (3f - 2f * mt);
                    float fq = 1f - Mathf.Clamp01((Mathf.Sqrt(bd) - 0.04f) / 0.04f);
                    if (gap > 0f) p1 -= nrm * (gap * Alpha * mt * fq * chest);
                }
            }
            nv[i] = p1;
            if (hasN)
            {
                Vector3 n = ns[i] + accN / ws * f;
                if (n.sqrMagnitude > 1e-8f) nn[i] = n.normalized;
            }
            moved++;
        }
        if (moved == 0) return 0;

        Mesh clone = UnityEngine.Object.Instantiate(mesh);
        clone.vertices = nv;
        if (hasN) clone.normals = nn;
        clone.RecalculateBounds();
        smr.sharedMesh = clone;
        var holder = c.Body.GetComponent<MeshHolder>() ?? c.Body.AddComponent<MeshHolder>();
        holder.Meshes.Add(clone);
        return moved;
    }

    private static float ChestMask(float y)
    {
        float lo = Mathf.Clamp01((y - (ChestMin - ChestFade)) / ChestFade);
        float hi = Mathf.Clamp01(((ChestMax + ChestFade) - y) / ChestFade);
        float m = Mathf.Min(lo, hi);
        return m * m * (3f - 2f * m);
    }

    //Vertices and normals of the generic costume body (0004_00_00) of one body type, cached for the session.
    private static TypeData GetType(string key)
    {
        if (Cache.TryGetValue(key, out TypeData cached)) return cached;
        TypeData data = null;
        string name = $"pfb_bdy0004_00_00_{key}";
        if (UmaViewerMain.Instance.AbList.TryGetValue(UmaDatabaseController.BodyPath + $"bdy0004_00/{name}", out UmaDatabaseEntry entry))
        {
            AssetBundle bundle = UmaAssetManager.LoadAssetBundle(entry, false, false);
            GameObject prefab = bundle != null ? bundle.LoadAsset<GameObject>(name) : null;
            if (prefab != null)
                foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (smr.name == "M_Body" && smr.sharedMesh != null && smr.sharedMesh.isReadable)
                    {
                        data = new TypeData { V = smr.sharedMesh.vertices, N = smr.sharedMesh.normals };
                        break;
                    }
        }
        Cache[key] = data;
        return data;
    }
}
