using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Custom models made with the BodyZoneEditor (MBODY). A custom model is a unique costume with some faces cut.
///
/// Layout (per character, per model):
///   Assets/Resources/CustomModels/{charaId}/{name}/manifest.json
///
/// The manifest stores the base costume and the removed face ranges per renderer (same data as the editor preset).
/// Optional textures in the same folder: triple_mask.png (_TripleMaskMap), main_tex.png (_MainTex) and toon_map.png
/// (_ToonMap); the last two are written by BodyTextureRecolor (a model may have textures and no cuts).
/// The mesh is rebuilt on load from the base costume, so only the cut is stored (no mesh data).
///
/// Costume id of a custom model = "cus_{name}". Anything that receives a costume id resolves it to the base
/// costume first (ResolveBaseCostume), so bundle paths keep working; only the body mesh gets the cut.
/// </summary>
public static class CustomModelRegistry
{
    public const string Prefix = "cus_";

    [Serializable]
    public class Manifest
    {
        public int version = 1;
        public string name;
        public int charaId;
        public string baseCostume;
        public string saved;
        public List<BodyZoneEditor.PresetTargetCut> targets = new List<BodyZoneEditor.PresetTargetCut>();
        public List<TargetAdd> additions = new List<TargetAdd>();   // geometry added by the BodyZoneEditor (border extension)
    }

    /// <summary>
    /// Geometry added to one body renderer (BodyZoneEditor "Extender borde"): new vertices that copy the bone weights,
    /// UV, normal and colour of an original vertex but sit at a new position, plus the triangles that use them.
    /// Stored resolved (absolute positions), so loading does not depend on the editor's classification.
    /// </summary>
    [Serializable]
    public class TargetAdd
    {
        public string target;                              // renderer name
        public int op;                                     // editor operation (undo groups the renderers of one extension)
        public int baseVertexCount;                        // validation: vertices of the original mesh
        public List<int> srcVerts = new List<int>();       // original vertex copied by each new vertex
        public List<Vector3> pos = new List<Vector3>();    // position of each new vertex (mesh space)
        public List<int> tris = new List<int>();           // 3 indices per triangle; index >= baseVertexCount = new vertex (index - baseVertexCount)
        public List<int> triSub = new List<int>();         // submesh of each triangle
    }

    static string Root
    {
        get { return Path.Combine(Application.dataPath, "Resources", "CustomModels"); }
    }

    public static bool IsCustom(string costumeId)
    {
        return !string.IsNullOrEmpty(costumeId) && costumeId.StartsWith(Prefix, StringComparison.Ordinal);
    }

    public static string IdOf(string name)
    {
        return Prefix + name;
    }

    public static string FolderName(string name)
    {
        string safe = name ?? string.Empty;
        foreach (char bad in Path.GetInvalidFileNameChars()) safe = safe.Replace(bad, '-');
        return safe;
    }

    /// <summary>Folder of a model (recolor settings and masks live next to its textures).</summary>
    public static string ModelFolder(int charaId, string name)
    {
        return ModelDir(charaId, name);
    }

    static string ModelDir(int charaId, string name)
    {
        return Path.Combine(Root, charaId.ToString(), FolderName(name));
    }

    /// <summary>Manifest of a custom costume id, or null when the id is not a custom model (or it is missing).</summary>
    public static Manifest Load(int charaId, string costumeId)
    {
        if (!IsCustom(costumeId)) return null;
        return ReadManifest(Path.Combine(ModelDir(charaId, costumeId.Substring(Prefix.Length)), "manifest.json"));
    }

    /// <summary>Base costume of a custom id; any other id is returned unchanged.</summary>
    public static string ResolveBaseCostume(int charaId, string costumeId)
    {
        if (!IsCustom(costumeId)) return costumeId;
        Manifest m = Load(charaId, costumeId);
        return m != null ? m.baseCostume : costumeId;
    }

    /// <summary>Custom models saved for a character, in folder name order.</summary>
    public static List<Manifest> List(int charaId)
    {
        var result = new List<Manifest>();
        string dir = Path.Combine(Root, charaId.ToString());
        if (!Directory.Exists(dir)) return result;

        string[] folders = Directory.GetDirectories(dir);
        Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders)
        {
            Manifest m = ReadManifest(Path.Combine(folder, "manifest.json"));
            if (m != null) result.Add(m);
        }
        return result;
    }

    /// <summary>True when a model with that name already exists for another base costume (it would be overwritten).</summary>
    public static bool NameTakenByOtherCostume(int charaId, string name, string baseCostume)
    {
        Manifest m = ReadManifest(Path.Combine(ModelDir(charaId, name), "manifest.json"));
        return m != null && m.baseCostume != baseCostume;
    }

    /// <summary>
    /// Saves (or overwrites) a model made with the BodyZoneEditor: manifest only. Textures already in the model folder
    /// (recolor) are kept. When copyTexturesFrom names another model, its textures are copied into the folder if it has
    /// none yet, so a geometry variant saved under a new name keeps the colours that were on screen.
    /// </summary>
    public static void Save(int charaId, string baseCostume, BodyZoneEditor.ZonePreset preset, string copyTexturesFrom = null)
    {
        var m = new Manifest
        {
            name = preset.name,
            charaId = charaId,
            baseCostume = baseCostume,
            saved = preset.saved,
            targets = preset.targets,
            additions = preset.additions
        };

        string dir = ModelDir(charaId, preset.name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonUtility.ToJson(m, true));

        if (!string.IsNullOrEmpty(copyTexturesFrom))
            CopyTextures(ModelDir(charaId, copyTexturesFrom), dir, false);
    }

    static readonly string[] TextureFiles = { "main_tex.png", "toon_map.png", "triple_mask.png" };

    static void CopyTextures(string srcDir, string dstDir, bool overwrite)
    {
        if (!Directory.Exists(srcDir)) return;
        if (string.Equals(Path.GetFullPath(srcDir), Path.GetFullPath(dstDir), StringComparison.OrdinalIgnoreCase)) return;

        foreach (string f in TextureFiles)
        {
            string from = Path.Combine(srcDir, f);
            string to = Path.Combine(dstDir, f);
            if (File.Exists(from) && (overwrite || !File.Exists(to))) File.Copy(from, to, true);
        }
    }

    /// <summary>True when the model has its own textures (it is a recolor): overwriting it would lose those colours.</summary>
    public static bool HasTextures(int charaId, string name)
    {
        string dir = ModelDir(charaId, name);
        foreach (string f in TextureFiles)
            if (File.Exists(Path.Combine(dir, f))) return true;
        return false;
    }

    /// <summary>True when a model with that name already exists for the same base costume (saving replaces it).</summary>
    public static bool Exists(int charaId, string name, string baseCostume)
    {
        Manifest m = ReadManifest(Path.Combine(ModelDir(charaId, name), "manifest.json"));
        return m != null && m.baseCostume == baseCostume;
    }

    /// <summary>
    /// Saves a model with body textures (BodyTextureRecolor): manifest + main_tex.png (_MainTex) + toon_map.png (_ToonMap).
    /// When the body was loaded from another custom model (copyFrom), its cuts are kept and its triple_mask.png is copied
    /// (unless an edited one is given: tripleMaskPng).
    /// </summary>
    public static void SaveTextured(int charaId, string baseCostume, string name, string copyFrom, byte[] mainTexPng, byte[] toonMapPng,
                                    byte[] tripleMaskPng = null)
    {
        var m = new Manifest
        {
            name = name,
            charaId = charaId,
            baseCostume = baseCostume,
            saved = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
        };

        string srcDir = null;
        if (!string.IsNullOrEmpty(copyFrom))
        {
            Manifest src = Load(charaId, IdOf(copyFrom));
            if (src != null)
            {
                if (src.targets != null) m.targets = src.targets;
                if (src.additions != null) m.additions = src.additions;
                srcDir = ModelDir(charaId, copyFrom);
            }
        }

        string dir = ModelDir(charaId, name);
        Directory.CreateDirectory(dir);

        // The new model starts as a copy of the one it comes from (cuts, added geometry and every texture); the
        // textures given below replace the copied ones. The source model itself is never touched.
        if (srcDir != null) CopyTextures(srcDir, dir, true);

        if (mainTexPng != null) File.WriteAllBytes(Path.Combine(dir, "main_tex.png"), mainTexPng);
        if (toonMapPng != null) File.WriteAllBytes(Path.Combine(dir, "toon_map.png"), toonMapPng);
        if (tripleMaskPng != null) File.WriteAllBytes(Path.Combine(dir, "triple_mask.png"), tripleMaskPng);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonUtility.ToJson(m, true));
    }

    /// <summary>Deletes the model folder, only when it belongs to the same base costume.</summary>
    public static void Delete(int charaId, string name, string baseCostume)
    {
        string dir = ModelDir(charaId, name);
        Manifest m = ReadManifest(Path.Combine(dir, "manifest.json"));
        if (m == null || m.baseCostume != baseCostume) return;
        Directory.Delete(dir, true);
    }

    /// <summary>
    /// Removes the cut faces from the body renderers (M_Body and its M_Body_* layers, same targets as BodyZoneEditor).
    /// Each original mesh is first registered as the editor's pristine base, so the editor can still edit from the base.
    /// </summary>
    public static void ApplyCuts(GameObject body, Manifest m)
    {
        if (body == null || m == null) return;

        foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null || smr.sharedMesh == null) continue;
            if (smr.name != "M_Body" && !smr.name.StartsWith("M_Body_", StringComparison.Ordinal)) continue;

            BodyZoneEditor.PresetTargetCut cut = m.targets.Find(t => t.target == smr.name);
            List<TargetAdd> adds = m.additions != null ? m.additions.FindAll(a => a.target == smr.name) : null;
            if (cut == null && (adds == null || adds.Count == 0)) continue;

            Mesh src = smr.sharedMesh;
            if (!src.isReadable)
            {
                Debug.LogWarning("[CustomModel] " + m.name + ": mesh " + src.name + " is not readable, not cut.");
                continue;
            }

            int faces = CountFaces(src);
            if (cut != null && faces != cut.faceCount)
            {
                Debug.LogWarning("[CustomModel] " + m.name + ": " + smr.name + " has " + faces + " faces, the model expects " +
                                 cut.faceCount + ". Not cut.");
                continue;
            }

            if (adds != null) adds = adds.FindAll(a => a.baseVertexCount == src.vertexCount);

            BodyZoneEditor.RememberPristine(smr, src);
            BodyZoneEditor.RememberPristine("pfb_bdy" + m.charaId + "_" + m.baseCostume + "/" + smr.name, src);

            bool[] removed = new bool[faces];
            if (cut != null)
            {
                for (int r = 0; r + 1 < cut.ranges.Count; r += 2)
                {
                    int a = Mathf.Max(0, cut.ranges[r]);
                    int b = Mathf.Min(faces - 1, cut.ranges[r + 1]);
                    for (int k = a; k <= b; k++) removed[k] = true;
                }
            }

            smr.sharedMesh = CutMesh(src, removed, adds);
        }
    }

    /// <summary>
    /// Appends the geometry of one TargetAdd to a mesh: the new vertices copy every attribute (bone weights, UV, normal,
    /// tangent, colour) of the original vertex they come from and sit at their stored position; their triangles are added
    /// to the per-submesh lists in keeps. Used by the loader and by the BodyZoneEditor, so both build the same mesh.
    /// </summary>
    public static void ApplyAdd(Mesh mesh, TargetAdd add, List<int>[] keeps)
    {
        if (mesh == null || add == null || add.srcVerts.Count == 0) return;

        int cur = mesh.vertexCount;
        if (cur < add.baseVertexCount) return;

        if (mesh.blendShapeCount > 0)
        {
            Debug.LogWarning("[CustomModel] " + mesh.name + " has blend shapes; added geometry skipped.");
            return;
        }

        var vs = new List<Vector3>(); mesh.GetVertices(vs);
        var ns = new List<Vector3>(); mesh.GetNormals(ns);
        var ts = new List<Vector4>(); mesh.GetTangents(ts);
        var cs = new List<Color>(); mesh.GetColors(cs);
        var bws = new List<BoneWeight>(mesh.boneWeights);
        var uvs = new List<Vector4>[4];
        for (int ch = 0; ch < 4; ch++) { uvs[ch] = new List<Vector4>(); mesh.GetUVs(ch, uvs[ch]); }

        for (int k = 0; k < add.srcVerts.Count; k++)
        {
            int s = add.srcVerts[k];
            vs.Add(add.pos[k]);
            if (ns.Count > 0) ns.Add(ns[s]);
            if (ts.Count > 0) ts.Add(ts[s]);
            if (cs.Count > 0) cs.Add(cs[s]);
            if (bws.Count > 0) bws.Add(bws[s]);
            for (int ch = 0; ch < 4; ch++) if (uvs[ch].Count > 0) uvs[ch].Add(uvs[ch][s]);
        }

        mesh.SetVertices(vs);
        if (ns.Count == vs.Count) mesh.SetNormals(ns);
        if (ts.Count == vs.Count) mesh.SetTangents(ts);
        if (cs.Count == vs.Count) mesh.SetColors(cs);
        for (int ch = 0; ch < 4; ch++) if (uvs[ch].Count == vs.Count) mesh.SetUVs(ch, uvs[ch]);
        if (bws.Count == vs.Count) mesh.boneWeights = bws.ToArray();

        for (int t = 0; t * 3 + 2 < add.tris.Count && t < add.triSub.Count; t++)
        {
            int sub = add.triSub[t];
            if (sub < 0 || sub >= keeps.Length) continue;
            for (int j = 0; j < 3; j++)
            {
                int idx = add.tris[t * 3 + j];
                keeps[sub].Add(idx >= add.baseVertexCount ? cur + (idx - add.baseVertexCount) : idx);
            }
        }
    }

    /// <summary>
    /// Optional hand-edited _TripleMaskMap of the model (triple_mask.png in its folder). Applied to every body
    /// material that uses the base mask, on the instance only (the shared asset is never touched).
    /// </summary>
    public static void ApplyTripleMask(GameObject body, Manifest m)
    {
        if (body == null || m == null) return;

        string file = Path.Combine(ModelDir(m.charaId, m.name), "triple_mask.png");
        if (!File.Exists(file)) return;

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, true);
        tex.name = m.name + "_triple_mask";
        tex.LoadImage(File.ReadAllBytes(file));

        foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null) continue;
            if (smr.name != "M_Body" && !smr.name.StartsWith("M_Body_", StringComparison.Ordinal)) continue;

            Material[] inst = smr.materials;
            for (int s = 0; s < inst.Length; s++)
            {
                if (inst[s] == null || !inst[s].HasProperty("_TripleMaskMap")) continue;
                inst[s].SetTexture("_TripleMaskMap", tex);
            }
            smr.materials = inst;
        }
    }

    /// <summary>
    /// Optional recoloured _MainTex (diffuse) of the model (main_tex.png in its folder, made with BodyTextureRecolor).
    /// Same rules as ApplyTripleMask: instance materials only, the shared asset is never touched.
    /// </summary>
    public static void ApplyMainTex(GameObject body, Manifest m)
    {
        if (body == null || m == null) return;

        string file = Path.Combine(ModelDir(m.charaId, m.name), "main_tex.png");
        if (!File.Exists(file)) return;

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, false);
        tex.name = m.name + "_main_tex";
        tex.LoadImage(File.ReadAllBytes(file));

        foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null) continue;
            if (smr.name != "M_Body" && !smr.name.StartsWith("M_Body_", StringComparison.Ordinal)) continue;

            Material[] inst = smr.materials;
            for (int s = 0; s < inst.Length; s++)
            {
                if (inst[s] == null || !inst[s].HasProperty("_MainTex")) continue;
                inst[s].SetTexture("_MainTex", tex);
            }
            smr.materials = inst;
        }
    }

    /// <summary>
    /// Optional hand-edited _ToonMap of the model (toon_map.png in its folder). Same rules as ApplyTripleMask:
    /// instance materials only, the shared asset is never touched.
    /// </summary>
    public static void ApplyToonMap(GameObject body, Manifest m)
    {
        if (body == null || m == null) return;

        string file = Path.Combine(ModelDir(m.charaId, m.name), "toon_map.png");
        if (!File.Exists(file)) return;

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, false);
        tex.name = m.name + "_toon_map";
        tex.LoadImage(File.ReadAllBytes(file));

        foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null) continue;
            if (smr.name != "M_Body" && !smr.name.StartsWith("M_Body_", StringComparison.Ordinal)) continue;

            Material[] inst = smr.materials;
            for (int s = 0; s < inst.Length; s++)
            {
                if (inst[s] == null || !inst[s].HasProperty("_ToonMap")) continue;
                inst[s].SetTexture("_ToonMap", tex);
            }
            smr.materials = inst;
        }
    }

    static int CountFaces(Mesh mesh)
    {
        int total = 0;
        for (int s = 0; s < mesh.subMeshCount; s++)
            total += mesh.GetTriangles(s).Length / 3;
        return total;
    }

    // Same face numbering as BodyZoneEditor.BuildTopology: submeshes in order, triangles in order.
    static Mesh CutMesh(Mesh src, bool[] removed, List<TargetAdd> adds)
    {
        Mesh mesh = UnityEngine.Object.Instantiate(src);
        mesh.name = src.name + "_custom";

        var keeps = new List<int>[mesh.subMeshCount];
        int local = 0;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            int[] tris = src.GetTriangles(s);
            var keep = new List<int>(tris.Length);
            int count = tris.Length / 3;

            for (int t = 0; t < count; t++)
            {
                if (removed != null && removed[local + t]) continue;
                keep.Add(tris[t * 3]);
                keep.Add(tris[t * 3 + 1]);
                keep.Add(tris[t * 3 + 2]);
            }

            local += count;
            keeps[s] = keep;
        }

        if (adds != null) foreach (TargetAdd add in adds) ApplyAdd(mesh, add, keeps);

        for (int s = 0; s < keeps.Length; s++) mesh.SetTriangles(keeps[s], s, false);

        mesh.RecalculateBounds();
        return mesh;
    }

    static Manifest ReadManifest(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[CustomModel] Cannot read " + path + ": " + ex.Message);
            return null;
        }
    }
}
