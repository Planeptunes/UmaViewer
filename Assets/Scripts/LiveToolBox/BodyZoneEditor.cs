// ─────────────────────────────────────────────────────────────────────────────
// HERRAMIENTA LOCAL DE EDICIÓN — no forma parte del visor publicado.
//
// BodyZoneEditor
//   Permite pintar zonas de la malla M_Body del personaje y QUITAR sus caras
//   (triángulos) en runtime, sin tocar el asset importado.
//
//   Modo CARAS (por defecto)
//     · Clic izq.  → selecciona las caras cuyo centro cae en el pincel.
//     · Alt + clic → selecciona toda la ISLA (pieza conexa) de la cara pulsada.
//     · Ctrl + clic→ deselecciona.
//     Se dibujan rellenas en rojo translúcido sobre la malla.
//
//   Modo VÉRTICES (auxiliar)
//     · Igual, pero pinta la nube de puntos (muestra reducida) y marca vértices.
//     Al quitar, se eliminan las caras que tocan esos vértices.
//
//   · Intro / botón  → quitar las caras seleccionadas.
//   · Retroceso      → deshacer (pila de cortes).
//   · R              → restaurar la malla original completa.
//   · Supr           → limpiar la selección.
//   · F2             → entrar/salir del modo edición.
//   · M              → cambiar Caras / Vértices.
//   · Clic derecho   → orbitar cámara · rueda → zoom.
//
//   Todo ocurre sobre copias en memoria: al recargar el personaje o salir de
//   Play Mode la malla vuelve a su estado original.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class BodyZoneEditor : MonoBehaviour
{
    public enum Mode
    {
        Caras = 0,
        Vertices = 1
    }

    [Header("Objetivo")]
    [Tooltip("Nombre del hijo del personaje que lleva la malla del cuerpo.")]
    public string bodyChildName = "M_Body";

    [Header("Pincel")]
    [Range(0.005f, 0.30f)] public float brushRadius = 0.06f;
    [Range(0, 4)] public int growRings = 1;

    [Header("Modo")]
    public Mode mode = Mode.Caras;

    [Tooltip("Parte de la malla a editar: -1 = todas, si no el índice de parte (material).")]
    public int paintPart = -1;

    [Tooltip("Las telas alpha (materiales *_Alpha*) no tapan: se puede marcar lo que hay detrás.")]
    public bool alphaDoesNotOcclude = true;

    [Header("Vista (solo modo Vértices)")]
    [Range(0.03f, 1.00f)] public float sampleRatio = 0.15f;
    [Range(0.002f, 0.030f)] public float pointSize = 0.007f;
    public bool showPoints = false;
    public bool hideBody = false;
    public bool xRay = false;

    [Header("Teclas")]
    public KeyCode toggleKey = KeyCode.F2;
    public KeyCode modeKey = KeyCode.M;

    public bool editMode = false;

    // ── malla ────────────────────────────────────────────────────────────────
    // Caché de mallas prístinas por nombre de hijo ("M_Body"). Sirve para que, si
    // se vuelve a añadir la herramienta sobre un personaje cuya malla ya fue
    // recortada, la base siga siendo la original y "Restaurar" funcione.
    static readonly Dictionary<string, Mesh> _pristine = new Dictionary<string, Mesh>();

    SkinnedMeshRenderer _sm;
    Mesh _baseMesh;
    int _baseTriCount;

    int[] _faces;                    // triángulos de TODOS los submeshes (3 índices por cara)
    int[] _faceSub;                  // submesh (material) de cada cara
    int[] _faceTri;                  // índice del triángulo dentro de su submesh
    int _faceCount;
    int[] _subGroup;                 // grupo (parte lógica = material) de cada submesh
    List<string> _partNames = new List<string>();
    List<bool> _partIsAlpha = new List<bool>();
    List<int>[] _faceNeighbors;      // adyacencia cara-cara (aristas compartidas)
    int[] _faceIsland;               // isla de cada cara

    int[] _island;                   // isla (componente conexo) por vértice
    List<int>[] _neighbors;          // adyacencia por vértice
    Color[] _vertColor;              // color muestreado del atlas por vértice

    bool[] _selectedFaces;
    bool[] _selectedVerts;
    bool[] _cutFaces;                // caras ya quitadas (acumulado)
    bool[] _cutVerts;                // vértices de esas caras (para ocultar puntos)

    Vector3[] _posed;                // posiciones en pose actual (mundo)
    float _posedStamp = -100f;

    // rejilla espacial para el test de oclusión (solo cuando rayos X está off)
    Dictionary<long, List<int>> _grid;
    List<int> _bigFaces;             // caras demasiado grandes: se prueban siempre
    float _gridCell = 0.05f;
    float _gridBuiltAt = -100f;
    int[] _faceStamp;
    int _stamp;

    // caché de visibilidad: válida mientras no cambien ni la pose ni la cámara
    int _visEpoch = 1;
    int[] _visSerialF;
    bool[] _visValF;
    int[] _visSerialV;
    bool[] _visValV;
    Vector3 _visCamPos = new Vector3(1e9f, 1e9f, 1e9f);

    readonly List<bool[]> _applied = new List<bool[]>();   // pila de cortes
    readonly List<int> _samples = new List<int>();         // muestra para modo Vértices

    // ── visuales ─────────────────────────────────────────────────────────────
    GameObject _pointsGo, _facesGo;
    Mesh _pointsMesh, _facesMesh;
    Material _mat;

    // ── cámara ───────────────────────────────────────────────────────────────
    CameraOrbit _orbit;
    bool _orbitWasEnabled;
    Camera _cam;
    Vector3 _focus;
    float _dist = 3f, _yaw, _pitch;
    Vector3 _lastMouse;
    bool _camDragging;
    bool _painting;
    bool _dirty = true;

    // ── UI ───────────────────────────────────────────────────────────────────
    Rect _panelRect = new Rect(238f, 60f, 350f, 380f);
    bool _overPanel;
    string _status = "";

    // ═════════════════════════════════════════════════════════════════════════
    void Awake()
    {
        Initialize();
    }

    void OnDestroy()
    {
        RestoreOrbit();

        if (_pointsGo != null) Destroy(_pointsGo);
        if (_facesGo != null) Destroy(_facesGo);
        if (_mat != null) Destroy(_mat);
        if (_pointsMesh != null) Destroy(_pointsMesh);
        if (_facesMesh != null) Destroy(_facesMesh);
    }

    public void Initialize()
    {
        if (_sm == null)
        {
            Transform t = transform.Find(bodyChildName);
            if (t != null) _sm = t.GetComponent<SkinnedMeshRenderer>();

            if (_sm == null)
            {
                SkinnedMeshRenderer[] all = GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].sharedMesh != null && all[i].sharedMesh.name == bodyChildName)
                    {
                        _sm = all[i];
                        break;
                    }
                }
            }
        }

        if (_sm == null || _sm.sharedMesh == null)
        {
            Debug.LogWarning("[BodyZoneEditor] No encuentro la malla " + bodyChildName + " en " + name);
            enabled = false;
            return;
        }

        Mesh current = _sm.sharedMesh;
        string key = BodyKey();
        Mesh cached;
        if (_pristine.TryGetValue(key, out cached) && cached != null)
        {
            _baseMesh = cached;
            if (current != cached) Debug.Log("[BodyZoneEditor] Malla actual recortada (" + current.name +
                                             "); uso la base prístina cacheada de " + key + ".");
        }
        else
        {
            _baseMesh = current;
            _pristine[key] = current;
        }

        _baseTriCount = CountTriangles(_baseMesh);

        Debug.Log("[BodyZoneEditor] Base: " + _baseTriCount + " caras / " + _baseMesh.vertexCount +
                  " vértices (mesh '" + _baseMesh.name + "', id " + _baseMesh.GetInstanceID() + ")");

        if (_baseMesh.name.EndsWith("_cut"))
        {
            Debug.LogError("[BodyZoneEditor] La malla base ya es un corte (" + _baseMesh.name +
                           "). Restaura o recarga el personaje antes de usar la herramienta.");
        }

        BuildTopology();
        BuildAtlasColors();
        BuildSamples();

        int n = _baseMesh.vertexCount;
        _selectedFaces = new bool[_faceCount];
        _selectedVerts = new bool[n];
        _cutFaces = new bool[_faceCount];
        _cutVerts = new bool[n];
        _posed = new Vector3[n];
        _faceStamp = new int[_faceCount];
        _visSerialF = new int[_faceCount];
        _visValF = new bool[_faceCount];
        _visSerialV = new int[n];
        _visValV = new bool[n];
        _visEpoch = 1;

        BuildVisuals();

        _status = "Base: " + _baseTriCount + " caras / " + n + " vértices";

        int subs = _baseMesh.subMeshCount;
        string partList = "[BodyZoneEditor] Partes (" + _partNames.Count + " de " + subs + " submeshes):";
        for (int p = 0; p < _partNames.Count; p++)
            partList += " " + p + "=" + _partNames[p] + (_partIsAlpha[p] ? "(alpha)" : "") + ";";

        Debug.Log("[BodyZoneEditor] " + _status + " · islas: " + IslandCount +
                  " · islas de cara: " + FaceIslandCount + " · " + partList);
    }

    public int BaseTriangleCount { get { return _baseTriCount; } }

    /// <summary>
    /// Clave de caché del cuerpo: el modelo (pfb_bdyXXXX_YY), no el personaje. Así la
    /// base prístina se reutiliza entre personajes que comparten cuerpo y no se mezcla
    /// la de un cuerpo con la de otro.
    /// </summary>
    string BodyKey()
    {
        Transform t = _sm != null ? _sm.transform : null;
        while (t != null)
        {
            if (t.name.StartsWith("pfb_bdy")) return t.name.Replace("(Clone)", string.Empty);
            t = t.parent;
        }

        Transform p = _sm != null ? _sm.transform.parent : null;
        return p != null ? p.name.Replace("(Clone)", string.Empty) : bodyChildName;
    }

    /// <summary>Asigna malla al renderer destruyendo la copia anterior (evita fugas).</summary>
    void SetMesh(Mesh mesh)
    {
        if (_sm == null) return;

        Mesh previous = _sm.sharedMesh;
        _sm.sharedMesh = mesh;

        if (previous != null && previous != mesh && previous != _baseMesh)
            Destroy(previous);
    }

    public int CurrentTriangleCount
    {
        get { return _sm != null && _sm.sharedMesh != null ? CountTriangles(_sm.sharedMesh) : 0; }
    }

    static int CountTriangles(Mesh mesh)
    {
        if (mesh == null) return 0;

        int total = 0;
        for (int s = 0; s < mesh.subMeshCount; s++)
            total += mesh.GetTriangles(s).Length / 3;

        return total;
    }

    int IslandCount
    {
        get
        {
            int max = -1;
            for (int i = 0; i < _island.Length; i++) if (_island[i] > max) max = _island[i];
            return max + 1;
        }
    }

    int FaceIslandCount
    {
        get
        {
            int max = -1;
            for (int i = 0; i < _faceIsland.Length; i++) if (_faceIsland[i] > max) max = _faceIsland[i];
            return max + 1;
        }
    }

    // ── topología ────────────────────────────────────────────────────────────
    void BuildTopology()
    {
        int n = _baseMesh.vertexCount;
        _island = new int[n];
        _neighbors = new List<int>[n];
        for (int i = 0; i < n; i++)
        {
            _island[i] = i;
            _neighbors[i] = new List<int>(4);
        }

        int subCount = _baseMesh.subMeshCount;
        _subGroup = new int[subCount];
        _partNames.Clear();
        _partIsAlpha.Clear();

        List<int[]> subTris = new List<int[]>(subCount);
        for (int s = 0; s < subCount; s++) subTris.Add(_baseMesh.GetTriangles(s));

        // agrupa submeshes con los mismos triángulos (doble cara del mismo material)
        for (int s = 0; s < subCount; s++)
        {
            int group = -1;
            for (int prev = 0; prev < s; prev++)
            {
                if (!SameTriangles(subTris[s], subTris[prev])) continue;
                group = _subGroup[prev];
                break;
            }

            if (group < 0)
            {
                group = _partNames.Count;

                Material m = _sm.sharedMaterials != null && s < _sm.sharedMaterials.Length ? _sm.sharedMaterials[s] : null;
                string mn = m != null ? m.name : ("Parte " + s);

                _partNames.Add(mn);
                _partIsAlpha.Add(mn.IndexOf("Alpha", System.StringComparison.OrdinalIgnoreCase) >= 0);
            }

            _subGroup[s] = group;
        }

        List<int> faceIdx = new List<int>(16384);
        List<int> faceSub = new List<int>(16384);
        List<int> faceTri = new List<int>(16384);

        for (int s = 0; s < subCount; s++)
        {
            int[] tris = subTris[s];
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];

                faceIdx.Add(a); faceIdx.Add(b); faceIdx.Add(c);
                faceSub.Add(s);
                faceTri.Add(t / 3);

                Link(a, b); Link(b, c); Link(c, a);
                Union(a, b);
            }
        }

        _faces = faceIdx.ToArray();
        _faceSub = faceSub.ToArray();
        _faceTri = faceTri.ToArray();
        _faceCount = _faceSub.Length;

        // compactar ids de isla de vértice
        Dictionary<int, int> map = new Dictionary<int, int>();
        int next = 0;
        for (int v = 0; v < n; v++)
        {
            int root = Find(v);
            int id;
            if (!map.TryGetValue(root, out id))
            {
                id = next++;
                map[root] = id;
            }
            _island[v] = id;
        }

        // isla por cara + adyacencia cara-cara dentro del mismo material
        _faceIsland = new int[_faceCount];
        _faceNeighbors = new List<int>[_faceCount];

        List<Dictionary<long, int>> edgeMaps = new List<Dictionary<long, int>>(subCount);
        for (int s = 0; s < subCount; s++) edgeMaps.Add(new Dictionary<long, int>(4096));

        for (int f = 0; f < _faceCount; f++)
        {
            _faceIsland[f] = _island[_faces[f * 3]];
            _faceNeighbors[f] = new List<int>(3);
        }

        for (int f = 0; f < _faceCount; f++)
        {
            Dictionary<long, int> edgeFace = edgeMaps[_faceSub[f]];

            for (int k = 0; k < 3; k++)
            {
                int a = _faces[f * 3 + k];
                int b = _faces[f * 3 + (k + 1) % 3];
                long key = EdgeKey(a, b);

                int other;
                if (edgeFace.TryGetValue(key, out other))
                {
                    if (other != f && !_faceNeighbors[f].Contains(other))
                    {
                        _faceNeighbors[f].Add(other);
                        _faceNeighbors[other].Add(f);
                    }
                }
                else
                {
                    edgeFace[key] = f;
                }
            }
        }
    }

    static bool SameTriangles(int[] a, int[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static long EdgeKey(int a, int b)
    {
        int lo = a < b ? a : b;
        int hi = a < b ? b : a;
        return ((long)lo << 32) | (uint)hi;
    }

    int Find(int x)
    {
        while (_island[x] != x)
        {
            _island[x] = _island[_island[x]];
            x = _island[x];
        }
        return x;
    }

    void Union(int a, int b)
    {
        int ra = Find(a), rb = Find(b);
        if (ra != rb) _island[ra] = rb;
    }

    void Link(int a, int b)
    {
        if (!_neighbors[a].Contains(b)) _neighbors[a].Add(b);
        if (!_neighbors[b].Contains(a)) _neighbors[b].Add(a);
    }

    // ── color del atlas por vértice (solo modo Vértices) ─────────────────────
    void BuildAtlasColors()
    {
        int n = _baseMesh.vertexCount;
        _vertColor = new Color[n];
        Vector2[] uvs = _baseMesh.uv;

        Texture2D tex = null;
        if (_sm.sharedMaterials != null && _sm.sharedMaterials.Length > 0 && _sm.sharedMaterials[0] != null)
            tex = _sm.sharedMaterials[0].mainTexture as Texture2D;

        Color fallback = new Color(0.75f, 0.75f, 0.8f, 1f);
        if (tex == null || uvs == null || uvs.Length < n)
        {
            for (int i = 0; i < n; i++) _vertColor[i] = fallback;
            return;
        }

        Color[] px = ReadTexturePixels(tex);
        if (px == null)
        {
            for (int i = 0; i < n; i++) _vertColor[i] = fallback;
            return;
        }

        int w = tex.width, h = tex.height;
        for (int i = 0; i < n; i++)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(uvs[i].x * (w - 1)), 0, w - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(uvs[i].y * (h - 1)), 0, h - 1);
            Color c = px[y * w + x];
            c.a = 1f;
            _vertColor[i] = c;
        }
    }

    static Color[] ReadTexturePixels(Texture2D tex)
    {
        if (tex.isReadable) return tex.GetPixels();

        RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture prev = RenderTexture.active;
        Graphics.Blit(tex, rt);
        RenderTexture.active = rt;

        Texture2D tmp = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, false);
        tmp.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        tmp.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color[] px = tmp.GetPixels();
        Object.Destroy(tmp);
        return px;
    }

    // ── muestra de puntos (modo Vértices) ────────────────────────────────────
    void BuildSamples()
    {
        _samples.Clear();

        Dictionary<int, List<int>> byIsland = new Dictionary<int, List<int>>();
        for (int v = 0; v < _island.Length; v++)
        {
            int id = _island[v];
            List<int> list;
            if (!byIsland.TryGetValue(id, out list))
            {
                list = new List<int>();
                byIsland[id] = list;
            }
            list.Add(v);
        }

        foreach (KeyValuePair<int, List<int>> kv in byIsland)
        {
            List<int> list = kv.Value;
            int take = Mathf.Max(2, Mathf.RoundToInt(list.Count * sampleRatio));
            if (take > list.Count) take = list.Count;
            int step = Mathf.Max(1, list.Count / Mathf.Max(1, take));
            for (int i = 0; i < list.Count; i += step) _samples.Add(list[i]);
        }
    }

    // ── objetos visuales ─────────────────────────────────────────────────────
    void BuildVisuals()
    {
        Shader sh = Shader.Find("Hidden/BodyZonePoints");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        _mat = new Material(sh);
        _mat.hideFlags = HideFlags.DontSave;

        _pointsMesh = new Mesh();
        _pointsMesh.name = "BodyZonePoints";
        _pointsMesh.MarkDynamic();
        _pointsMesh.hideFlags = HideFlags.DontSave;

        _pointsGo = new GameObject("BodyZonePoints");
        _pointsGo.hideFlags = HideFlags.DontSave;
        _pointsGo.AddComponent<MeshFilter>().sharedMesh = _pointsMesh;
        _pointsGo.AddComponent<MeshRenderer>().sharedMaterial = _mat;

        _facesMesh = new Mesh();
        _facesMesh.name = "BodyZoneFaces";
        _facesMesh.MarkDynamic();
        _facesMesh.hideFlags = HideFlags.DontSave;

        _facesGo = new GameObject("BodyZoneFaces");
        _facesGo.hideFlags = HideFlags.DontSave;
        _facesGo.AddComponent<MeshFilter>().sharedMesh = _facesMesh;
        _facesGo.AddComponent<MeshRenderer>().sharedMaterial = _mat;

        _pointsGo.SetActive(false);
        _facesGo.SetActive(false);
    }

    void SkinPose()
    {
        Transform[] bones = _sm.bones;
        Matrix4x4[] bind = _baseMesh.bindposes;
        Vector3[] verts = _baseMesh.vertices;
        BoneWeight[] bw = _baseMesh.boneWeights;

        Matrix4x4[] mats = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
            mats[i] = bones[i] != null ? bones[i].localToWorldMatrix * bind[i] : Matrix4x4.identity;

        for (int i = 0; i < verts.Length; i++)
        {
            BoneWeight w = bw[i];
            Vector3 p = Vector3.zero;
            float total = 0f;

            if (w.boneIndex0 >= 0 && w.boneIndex0 < mats.Length && w.weight0 > 0f)
            {
                p += mats[w.boneIndex0].MultiplyPoint3x4(verts[i]) * w.weight0;
                total += w.weight0;
            }
            if (w.boneIndex1 >= 0 && w.boneIndex1 < mats.Length && w.weight1 > 0f)
            {
                p += mats[w.boneIndex1].MultiplyPoint3x4(verts[i]) * w.weight1;
                total += w.weight1;
            }
            if (w.boneIndex2 >= 0 && w.boneIndex2 < mats.Length && w.weight2 > 0f)
            {
                p += mats[w.boneIndex2].MultiplyPoint3x4(verts[i]) * w.weight2;
                total += w.weight2;
            }
            if (w.boneIndex3 >= 0 && w.boneIndex3 < mats.Length && w.weight3 > 0f)
            {
                p += mats[w.boneIndex3].MultiplyPoint3x4(verts[i]) * w.weight3;
                total += w.weight3;
            }

            if (total < 0.0001f) p = verts[i];
            _posed[i] = p;
        }

        _gridBuiltAt = -100f;   // la rejilla queda obsoleta al cambiar la pose
        _visEpoch++;            // y las visibilidades cacheadas también
        _posedStamp = Time.unscaledTime;
    }

    // ── nube de puntos ───────────────────────────────────────────────────────
    void RefreshPoints()
    {
        bool visible = editMode && mode == Mode.Vertices && showPoints && _cam != null;
        _pointsGo.SetActive(visible);
        if (!visible) return;

        _mat.SetFloat("_ZTest", xRay ? 8f : 4f);

        Vector3 bias = (_cam.transform.position - _focus).normalized * 0.004f;
        Vector3 right = _cam.transform.right * (pointSize * 0.5f);
        Vector3 up = _cam.transform.up * (pointSize * 0.5f);

        List<Vector3> vs = new List<Vector3>(_samples.Count * 4);
        List<Color> cs = new List<Color>(_samples.Count * 4);
        List<int> ts = new List<int>(_samples.Count * 6);

        for (int i = 0; i < _samples.Count; i++)
        {
            int v = _samples[i];
            if (_cutVerts[v]) continue;

            Vector3 p = _posed[v] - bias;
            Color c = _vertColor[v];
            if (_selectedVerts[v]) c = new Color(1f, 0.15f, 0.15f, 1f);

            int b = vs.Count;
            vs.Add(p - right - up);
            vs.Add(p + right - up);
            vs.Add(p + right + up);
            vs.Add(p - right + up);
            cs.Add(c); cs.Add(c); cs.Add(c); cs.Add(c);
            ts.Add(b + 0); ts.Add(b + 1); ts.Add(b + 2);
            ts.Add(b + 0); ts.Add(b + 2); ts.Add(b + 3);
        }

        _pointsMesh.Clear();
        _pointsMesh.SetVertices(vs);
        _pointsMesh.SetColors(cs);
        _pointsMesh.SetTriangles(ts, 0, false);
        _pointsMesh.RecalculateBounds();
    }

    // ── caras seleccionadas (relleno rojo) ───────────────────────────────────
    void RefreshFaces()
    {
        bool visible = editMode && mode == Mode.Caras;
        _facesGo.SetActive(visible);
        if (!visible) return;

        _mat.SetFloat("_ZTest", xRay ? 8f : 4f);

        Color color = new Color(1f, 0.12f, 0.12f, 0.55f);

        List<Vector3> vs = new List<Vector3>(256);
        List<Color> cs = new List<Color>(256);
        List<int> ts = new List<int>(384);

        for (int f = 0; f < _faceCount; f++)
        {
            if (!_selectedFaces[f] || _cutFaces[f]) continue;

            int b = vs.Count;
            for (int k = 0; k < 3; k++)
            {
                vs.Add(_posed[_faces[f * 3 + k]]);
                cs.Add(color);
            }
            ts.Add(b + 0); ts.Add(b + 1); ts.Add(b + 2);
        }

        _facesMesh.Clear();
        if (vs.Count == 0)
        {
            _facesMesh.RecalculateBounds();
            return;
        }

        _facesMesh.SetVertices(vs);
        _facesMesh.SetColors(cs);
        _facesMesh.SetTriangles(ts, 0, false);
        _facesMesh.RecalculateBounds();
    }

    // ═════════════════════════════════════════════════════════════════════════
    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) SetEditMode(!editMode);

        if (!editMode) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        if (Input.GetKeyDown(modeKey))
        {
            mode = mode == Mode.Caras ? Mode.Vertices : Mode.Caras;
            if (mode == Mode.Vertices) showPoints = true;
            _dirty = true;
        }

        if (Time.unscaledTime - _posedStamp > 0.08f)
        {
            SkinPose();
            _dirty = true;
        }

        Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        _overPanel = _panelRect.Contains(guiMouse);

        HandleCamera();
        HandlePaint();
        HandleKeys();

        if (_dirty)
        {
            RefreshPoints();
            RefreshFaces();
            _dirty = false;
        }
    }

    void HandleKeys()
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) ApplySelection();
        if (Input.GetKeyDown(KeyCode.Backspace)) Undo();
        if (Input.GetKeyDown(KeyCode.R) && !Input.GetKey(KeyCode.LeftControl)) RestoreAll();
        if (Input.GetKeyDown(KeyCode.Delete)) ClearSelection();
        if (Input.GetKeyDown(KeyCode.Z) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) Undo();
        if (Input.GetKeyDown(KeyCode.LeftBracket)) brushRadius = Mathf.Max(0.005f, brushRadius - 0.005f);
        if (Input.GetKeyDown(KeyCode.RightBracket)) brushRadius = Mathf.Min(0.30f, brushRadius + 0.005f);
    }

    bool PointerBlockedByUI()
    {
        // Los paneles grandes del visor (Viewport) no deben bloquear el pincel:
        // solo bloqueamos controles interactivos reales.
        EventSystem es = EventSystem.current;
        if (es == null) return false;

        PointerEventData pd = new PointerEventData(es);
        pd.position = Input.mousePosition;

        List<RaycastResult> hits = new List<RaycastResult>();
        es.RaycastAll(pd, hits);

        for (int i = 0; i < hits.Count; i++)
        {
            GameObject go = hits[i].gameObject;
            if (go == null) continue;
            if (go.GetComponentInParent<Selectable>() != null) return true;
            if (go.GetComponentInParent<ScrollRect>() != null) return true;
        }

        return false;
    }

    void HandlePaint()
    {
        if (_overPanel || PointerBlockedByUI()) return;

        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        if (Input.GetMouseButtonDown(0))
        {
            PaintAt(Input.mousePosition, alt, ctrl, false);
            _painting = !alt;
        }
        else if (Input.GetMouseButton(0) && _painting)
        {
            PaintAt(Input.mousePosition, false, ctrl, true);
        }

        if (Input.GetMouseButtonUp(0)) _painting = false;
    }

    void PaintAt(Vector2 screenPos, bool island, bool deselect, bool continuous)
    {
        Vector3 hit;
        int face;
        if (!RaycastBody(screenPos, out hit, out face)) return;

        if (mode == Mode.Caras)
        {
            if (island) SelectFaceIsland(face);
            else SelectFacesSphere(hit, brushRadius, deselect);
        }
        else
        {
            if (island) SelectVertIsland(hit);
            else SelectVertsSphere(hit, brushRadius, deselect);
        }

        if (!continuous) _status = (deselect ? "Quitado de la selección" : "Seleccionado") + ": " + SelectionLabel();
    }

    // ── cámara propia mientras se edita ──────────────────────────────────────
    public void SetEditMode(bool on)
    {
        if (on == editMode) return;
        editMode = on;

        if (on)
        {
            _cam = Camera.main;
            if (_cam != null)
            {
                _orbit = _cam.GetComponent<CameraOrbit>();
                if (_orbit != null)
                {
                    _orbitWasEnabled = _orbit.enabled;
                    _orbit.enabled = false;
                }

                Bounds b = _sm.bounds;
                _focus = b.center;
                Vector3 d = _cam.transform.position - _focus;
                _dist = Mathf.Max(0.4f, d.magnitude);
                _yaw = _cam.transform.eulerAngles.y;
                _pitch = _cam.transform.eulerAngles.x;
            }

            SkinPose();
            _dirty = true;
        }
        else
        {
            RestoreOrbit();
            hideBody = false;
            if (_sm != null) _sm.enabled = true;
            if (_pointsGo != null) _pointsGo.SetActive(false);
            if (_facesGo != null) _facesGo.SetActive(false);
        }
    }

    void RestoreOrbit()
    {
        if (_orbit != null) _orbit.enabled = _orbitWasEnabled;
        _orbit = null;
    }

    void HandleCamera()
    {
        if (_cam == null) return;

        if (!_overPanel && !PointerBlockedByUI())
        {
            if (Input.GetMouseButtonDown(1)) { _camDragging = true; _lastMouse = Input.mousePosition; }
            if (Input.GetMouseButtonUp(1)) _camDragging = false;

            if (Input.GetMouseButton(1))
            {
                Vector3 delta = Input.mousePosition - _lastMouse;
                _lastMouse = Input.mousePosition;
                _yaw += delta.x * 0.25f;
                _pitch -= delta.y * 0.25f;
                _pitch = Mathf.Clamp(_pitch, -80f, 80f);
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f) _dist = Mathf.Clamp(_dist * (1f - scroll * 0.1f), 0.3f, 20f);
        }

        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
        _cam.transform.position = _focus - rot * Vector3.forward * _dist;
        _cam.transform.rotation = rot;
    }

    // ── picking ──────────────────────────────────────────────────────────────
    bool RaycastBody(Vector2 screenPos, out Vector3 hit, out int face)
    {
        hit = Vector3.zero;
        face = -1;
        if (_cam == null) return false;

        Ray ray = _cam.ScreenPointToRay(screenPos);
        float best = float.MaxValue;
        bool found = false;

        for (int f = 0; f < _faceCount; f++)
        {
            if (_cutFaces[f]) continue;
            if (paintPart >= 0 && _subGroup[_faceSub[f]] != paintPart) continue;

            Vector3 a = _posed[_faces[f * 3]];
            Vector3 b = _posed[_faces[f * 3 + 1]];
            Vector3 c = _posed[_faces[f * 3 + 2]];

            float d;
            if (RayTriangle(ray, a, b, c, out d) && d < best)
            {
                best = d;
                hit = ray.origin + ray.direction * d;
                face = f;
                found = true;
            }
        }

        return found;
    }

    static bool RayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance)
    {
        distance = 0f;
        Vector3 e1 = b - a, e2 = c - a;
        Vector3 pv = Vector3.Cross(ray.direction, e2);
        float det = Vector3.Dot(e1, pv);
        if (Mathf.Abs(det) < 1e-10f) return false;

        float inv = 1f / det;
        Vector3 tv = ray.origin - a;
        float u = Vector3.Dot(tv, pv) * inv;
        if (u < 0f || u > 1f) return false;

        Vector3 qv = Vector3.Cross(tv, e1);
        float v = Vector3.Dot(ray.direction, qv) * inv;
        if (v < 0f || u + v > 1f) return false;

        float d = Vector3.Dot(e2, qv) * inv;
        if (d <= 0.0001f) return false;

        distance = d;
        return true;
    }

    Vector3 FaceCenter(int f)
    {
        return (_posed[_faces[f * 3]] + _posed[_faces[f * 3 + 1]] + _posed[_faces[f * 3 + 2]]) / 3f;
    }

    // ── test de oclusión (solo con rayos X OFF) ──────────────────────────────
    // Rejilla espacial sobre las caras, en la pose actual. Se reconstruye si tiene
    // más de 0,2 s para seguir la animación sin pagar el coste cada frame.
    void EnsureGrid()
    {
        if (_grid != null && Time.unscaledTime - _gridBuiltAt < 0.05f) return;

        _grid = new Dictionary<long, List<int>>(Mathf.Max(1024, _faceCount));
        _bigFaces = new List<int>();

        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (int f = 0; f < _faceCount; f++)
        {
            if (_cutFaces[f]) continue;
            for (int k = 0; k < 3; k++)
            {
                Vector3 p = _posed[_faces[f * 3 + k]];
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

        if (min.x > max.x) { _gridBuiltAt = Time.unscaledTime; return; }

        float diag = (max - min).magnitude;
        _gridCell = Mathf.Clamp(diag / 96f, 0.01f, 0.05f);

        for (int f = 0; f < _faceCount; f++)
        {
            if (_cutFaces[f]) continue;

            // Se registra en TODAS las celdas de su caja, no solo en las de sus
            // vértices: si no, un rayo puede atravesar un triángulo grande sin
            // entrar en ninguna de esas celdas y "ver" a través de él.
            Vector3 a = _posed[_faces[f * 3]];
            Vector3 b = _posed[_faces[f * 3 + 1]];
            Vector3 c = _posed[_faces[f * 3 + 2]];

            Vector3 fmin = Vector3.Min(a, Vector3.Min(b, c));
            Vector3 fmax = Vector3.Max(a, Vector3.Max(b, c));

            int x0 = Mathf.FloorToInt(fmin.x / _gridCell), x1 = Mathf.FloorToInt(fmax.x / _gridCell);
            int y0 = Mathf.FloorToInt(fmin.y / _gridCell), y1 = Mathf.FloorToInt(fmax.y / _gridCell);
            int z0 = Mathf.FloorToInt(fmin.z / _gridCell), z1 = Mathf.FloorToInt(fmax.z / _gridCell);

            long cells = (long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1);
            if (cells > 64 || cells <= 0)
            {
                _bigFaces.Add(f);
                continue;
            }

            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int z = z0; z <= z1; z++)
                    {
                        long key = PackCell(x, y, z);
                        List<int> bucket;
                        if (!_grid.TryGetValue(key, out bucket))
                        {
                            bucket = new List<int>(8);
                            _grid[key] = bucket;
                        }
                        if (!bucket.Contains(f)) bucket.Add(f);
                    }
                }
            }
        }

        _gridBuiltAt = Time.unscaledTime;
    }

    static long PackCell(int x, int y, int z)
    {
        return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
    }

    long GridKey(Vector3 p)
    {
        return PackCell(
            Mathf.FloorToInt(p.x / _gridCell),
            Mathf.FloorToInt(p.y / _gridCell),
            Mathf.FloorToInt(p.z / _gridCell));
    }

    /// <summary>
    /// ¿El punto se ve desde la cámara? Lanza el rayo y marcha la rejilla recogiendo
    /// solo las caras de las celdas que atraviesa. Así la falda ya no marca las
    /// piernas que tiene detrás.
    /// </summary>
    bool IsVisibleFromCamera(Vector3 point)
    {
        if (_cam == null) return true;

        EnsureGrid();
        if (_grid == null) return true;

        Vector3 origin = _cam.transform.position;
        Vector3 delta = point - origin;
        float dist = delta.magnitude;
        if (dist < 0.001f) return true;

        Vector3 dir = delta / dist;
        float step = _gridCell * 0.5f;
        float limit = dist - 0.006f;   // margen para no contar la propia cara

        if (_stamp == int.MaxValue) { _stamp = 0; for (int i = 0; i < _faceStamp.Length; i++) _faceStamp[i] = 0; }
        int id = ++_stamp;

        Ray ray = new Ray(origin, dir);

        // caras grandes: no están en la rejilla, se prueban siempre
        if (_bigFaces != null)
        {
            for (int i = 0; i < _bigFaces.Count; i++)
            {
                int f = _bigFaces[i];
                if (alphaDoesNotOcclude && _partIsAlpha[_subGroup[_faceSub[f]]]) continue;
                _faceStamp[f] = id;

                float d;
                if (RayTriangle(ray,
                        _posed[_faces[f * 3]],
                        _posed[_faces[f * 3 + 1]],
                        _posed[_faces[f * 3 + 2]], out d))
                {
                    if (d < limit) return false;
                }
            }
        }

        for (float t = 0f; t < limit; t += step)
        {
            long key = GridKey(origin + dir * t);
            List<int> bucket;
            if (!_grid.TryGetValue(key, out bucket)) continue;

            for (int i = 0; i < bucket.Count; i++)
            {
                int f = bucket[i];
                if (_faceStamp[f] == id) continue;
                _faceStamp[f] = id;
                if (alphaDoesNotOcclude && _partIsAlpha[_subGroup[_faceSub[f]]]) continue;

                float d;
                if (RayTriangle(ray,
                        _posed[_faces[f * 3]],
                        _posed[_faces[f * 3 + 1]],
                        _posed[_faces[f * 3 + 2]], out d))
                {
                    if (d < limit) return false;
                }
            }
        }

        return true;
    }

    // ── selección: caras ─────────────────────────────────────────────────────
    void SelectFacesSphere(Vector3 center, float radius, bool deselect)
    {
        if (!xRay)
        {
            EnsureGrid();
            if ((_visCamPos - _cam.transform.position).sqrMagnitude > 0.0001f)
            {
                _visCamPos = _cam.transform.position;
                _visEpoch++;
            }
        }

        float r2 = radius * radius;
        for (int f = 0; f < _faceCount; f++)
        {
            if (_cutFaces[f]) continue;
            if (paintPart >= 0 && _subGroup[_faceSub[f]] != paintPart) continue;

            Vector3 c = FaceCenter(f);
            if ((c - center).sqrMagnitude > r2) continue;

            if (!xRay && !IsFaceVisible(f)) continue;   // tapada por otra parte de la malla

            _selectedFaces[f] = !deselect;
        }
        _dirty = true;
    }

    bool IsFaceVisible(int f)
    {
        if (_visSerialF[f] == _visEpoch) return _visValF[f];

        bool v = IsVisibleFromCamera(FaceCenter(f));
        _visSerialF[f] = _visEpoch;
        _visValF[f] = v;
        return v;
    }

    void SelectFaceIsland(int face)
    {
        if (face < 0 || face >= _faceCount) return;

        int id = _faceIsland[face];
        int group = _subGroup[_faceSub[face]];

        for (int f = 0; f < _faceCount; f++)
        {
            if (_cutFaces[f] || _faceIsland[f] != id) continue;
            if (paintPart >= 0 && _subGroup[_faceSub[f]] != group) continue;
            _selectedFaces[f] = true;
        }

        _dirty = true;
        _status = "Isla " + id + " seleccionada (" + SelectionLabel() + ")";
    }

    // ── selección: vértices ──────────────────────────────────────────────────
    void SelectVertsSphere(Vector3 center, float radius, bool deselect)
    {
        if (!xRay)
        {
            EnsureGrid();
            if ((_visCamPos - _cam.transform.position).sqrMagnitude > 0.0001f)
            {
                _visCamPos = _cam.transform.position;
                _visEpoch++;
            }
        }

        float r2 = radius * radius;
        for (int v = 0; v < _selectedVerts.Length; v++)
        {
            if (_cutVerts[v]) continue;
            if ((_posed[v] - center).sqrMagnitude > r2) continue;
            if (!xRay && !IsVertexVisible(v)) continue;

            _selectedVerts[v] = !deselect;
        }
        _dirty = true;
    }

    bool IsVertexVisible(int v)
    {
        if (_visSerialV[v] == _visEpoch) return _visValV[v];

        bool vis = IsVisibleFromCamera(_posed[v]);
        _visSerialV[v] = _visEpoch;
        _visValV[v] = vis;
        return vis;
    }

    void SelectVertIsland(Vector3 center)
    {
        float best = 0.12f;
        int bestV = -1;
        for (int v = 0; v < _posed.Length; v++)
        {
            if (_cutVerts[v]) continue;
            float d = Vector3.Distance(_posed[v], center);
            if (d < best) { best = d; bestV = v; }
        }

        if (bestV < 0) return;

        int id = _island[bestV];
        for (int v = 0; v < _island.Length; v++)
            if (_island[v] == id && !_cutVerts[v]) _selectedVerts[v] = true;

        _dirty = true;
        _status = "Isla " + id + " seleccionada (" + SelectionLabel() + ")";
    }

    // ── contadores ───────────────────────────────────────────────────────────
    public int CountSelectedFaces()
    {
        int n = 0;
        for (int f = 0; f < _faceCount; f++) if (_selectedFaces[f] && !_cutFaces[f]) n++;
        return n;
    }

    public int CountSelectedVerts()
    {
        int n = 0;
        for (int v = 0; v < _selectedVerts.Length; v++) if (_selectedVerts[v] && !_cutVerts[v]) n++;
        return n;
    }

    string SelectionLabel()
    {
        if (mode == Mode.Caras) return CountSelectedFaces() + " caras";
        return CountSelectedVerts() + " vértices";
    }

    public void ClearSelection()
    {
        _selectedFaces = new bool[_faceCount];
        _selectedVerts = new bool[_baseMesh.vertexCount];
        _dirty = true;
        _status = "Selección vacía";
    }

    // ── cortes ───────────────────────────────────────────────────────────────
    bool[] SelectedFacesAsMask()
    {
        bool[] mask = new bool[_faceCount];

        if (mode == Mode.Caras)
        {
            for (int f = 0; f < _faceCount; f++) mask[f] = _selectedFaces[f];
        }
        else
        {
            for (int f = 0; f < _faceCount; f++)
            {
                if (paintPart >= 0 && _subGroup[_faceSub[f]] != paintPart) continue;

                if (_selectedVerts[_faces[f * 3]] ||
                    _selectedVerts[_faces[f * 3 + 1]] ||
                    _selectedVerts[_faces[f * 3 + 2]]) mask[f] = true;
            }
        }

        return mask;
    }

    bool[] GrowFaces(bool[] mask)
    {
        bool[] current = (bool[])mask.Clone();
        for (int ring = 0; ring < growRings; ring++)
        {
            bool[] next = (bool[])current.Clone();
            for (int f = 0; f < _faceCount; f++)
            {
                if (!current[f]) continue;
                List<int> nb = _faceNeighbors[f];
                for (int i = 0; i < nb.Count; i++) next[nb[i]] = true;
            }
            current = next;
        }
        return current;
    }

    void RebuildCutVerts()
    {
        _cutVerts = new bool[_baseMesh.vertexCount];
        for (int f = 0; f < _faceCount; f++)
        {
            if (!_cutFaces[f]) continue;
            _cutVerts[_faces[f * 3]] = true;
            _cutVerts[_faces[f * 3 + 1]] = true;
            _cutVerts[_faces[f * 3 + 2]] = true;
        }
    }

    Mesh BuildCutMesh(bool[] facesMask)
    {
        Mesh mesh = Instantiate(_baseMesh);
        mesh.name = "M_Body_cut";

        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            int[] tris = _baseMesh.GetTriangles(s);
            List<int> keep = new List<int>(tris.Length);

            for (int f = 0; f < _faceCount; f++)
            {
                if (_faceSub[f] != s || facesMask[f]) continue;

                int t = _faceTri[f] * 3;
                if (t + 2 >= tris.Length) continue;

                keep.Add(tris[t]);
                keep.Add(tris[t + 1]);
                keep.Add(tris[t + 2]);
            }

            mesh.SetTriangles(keep, s, false);
        }

        mesh.RecalculateBounds();
        return mesh;
    }

    public void ApplySelection()
    {
        int count = mode == Mode.Caras ? CountSelectedFaces() : CountSelectedVerts();
        if (count == 0)
        {
            _status = "Nada seleccionado";
            return;
        }

        bool[] mask = GrowFaces(SelectedFacesAsMask());
        for (int f = 0; f < _faceCount; f++) if (_cutFaces[f]) mask[f] = true;

        _cutFaces = mask;
        RebuildCutVerts();
        _applied.Add((bool[])mask.Clone());

        SetMesh(BuildCutMesh(mask));

        _selectedFaces = new bool[_faceCount];
        _selectedVerts = new bool[_baseMesh.vertexCount];
        _dirty = true;

        _status = "Corte " + _applied.Count + " · caras restantes: " + CurrentTriangleCount + " / " + _baseTriCount;
        Debug.Log("[BodyZoneEditor] " + _status);
    }

    public void Undo()
    {
        if (_applied.Count == 0)
        {
            _status = "Nada que deshacer";
            return;
        }

        _applied.RemoveAt(_applied.Count - 1);

        if (_applied.Count == 0)
        {
            _cutFaces = new bool[_faceCount];
            SetMesh(_baseMesh);
        }
        else
        {
            _cutFaces = (bool[])_applied[_applied.Count - 1].Clone();
            SetMesh(BuildCutMesh(_cutFaces));
        }

        RebuildCutVerts();
        _selectedFaces = new bool[_faceCount];
        _selectedVerts = new bool[_baseMesh.vertexCount];
        _dirty = true;
        _status = "Deshecho · caras: " + CurrentTriangleCount + " / " + _baseTriCount;
    }

    public void RestoreAll()
    {
        _applied.Clear();
        _cutFaces = new bool[_faceCount];
        _cutVerts = new bool[_baseMesh.vertexCount];
        SetMesh(_baseMesh);
        _selectedFaces = new bool[_faceCount];
        _selectedVerts = new bool[_baseMesh.vertexCount];
        _dirty = true;
        _status = "Malla original restaurada · caras: " + CurrentTriangleCount + " / " + _baseTriCount;
    }

    string PartLabel(int p)
    {
        if (_partNames == null || p < 0 || p >= _partNames.Count) return "Parte " + p;

        string name = _partNames[p];

        int subs = 0;
        if (_subGroup != null)
            for (int s = 0; s < _subGroup.Length; s++) if (_subGroup[s] == p) subs++;

        if (subs > 1) name += " (" + subs + " capas)";
        if (_partIsAlpha[p]) name += "  · alpha";

        return name;
    }

    // ── API de prueba ────────────────────────────────────────────────────────
    public string SelectWorld(Vector3 worldPoint, float radius, bool island)
    {
        if (Time.unscaledTime - _posedStamp > 0.01f) SkinPose();

        if (mode == Mode.Caras)
        {
            if (island)
            {
                int bestFace = -1;
                float best = float.MaxValue;
                for (int f = 0; f < _faceCount; f++)
                {
                    if (_cutFaces[f]) continue;
                    if (paintPart >= 0 && _subGroup[_faceSub[f]] != paintPart) continue;
                    float d = Vector3.Distance(FaceCenter(f), worldPoint);
                    if (d < best) { best = d; bestFace = f; }
                }
                if (bestFace >= 0) SelectFaceIsland(bestFace);
            }
            else SelectFacesSphere(worldPoint, radius, false);
        }
        else
        {
            if (island) SelectVertIsland(worldPoint);
            else SelectVertsSphere(worldPoint, radius, false);
        }

        return "sel=" + SelectionLabel();
    }

    // ── UI ───────────────────────────────────────────────────────────────────
    void OnGUI()
    {
        if (!editMode) return;

        // a la derecha de la lista de personajes, a la izquierda del modelo
        _panelRect = new Rect(238f, 60f, 350f, 600f);

        GUI.color = new Color(0f, 0f, 0f, 0.88f);
        GUI.DrawTexture(_panelRect, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(_panelRect, GUI.skin.box);
        GUILayout.Label("EDITOR DE ZONAS · " + bodyChildName);

        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(mode == Mode.Caras, " Caras", GUI.skin.button) && mode != Mode.Caras)
        {
            mode = Mode.Caras;
            _dirty = true;
        }
        if (GUILayout.Toggle(mode == Mode.Vertices, " Vértices", GUI.skin.button) && mode != Mode.Vertices)
        {
            mode = Mode.Vertices;
            showPoints = true;
            _dirty = true;
        }
        GUILayout.EndHorizontal();

        GUILayout.Label(mode == Mode.Caras
            ? "Clic = pintar caras · Alt+clic = pieza · Ctrl+clic = quitar"
            : "Clic = pintar vértices · Alt+clic = pieza · Ctrl+clic = quitar");
        GUILayout.Label("Intro = borrar · Retroceso = deshacer · R = restaurar");

        GUILayout.Space(4);
        GUILayout.Label("Parte a editar (materiales de " + bodyChildName + "):");
        if (GUILayout.Toggle(paintPart < 0, " Todas las partes", GUI.skin.button))
            paintPart = -1;

        for (int p = 0; p < _partNames.Count; p++)
        {
            if (GUILayout.Toggle(paintPart == p, " " + PartLabel(p), GUI.skin.button))
                paintPart = p;
        }

        if (_partIsAlpha.Count > 0)
            alphaDoesNotOcclude = GUILayout.Toggle(alphaDoesNotOcclude, " Las telas alpha no tapan");

        GUILayout.Space(4);
        GUILayout.Label("Radio del pincel: " + (brushRadius * 100f).ToString("F1") + " cm");
        brushRadius = GUILayout.HorizontalSlider(brushRadius, 0.01f, 0.30f);

        GUILayout.Label("Engordar borde: " + growRings + " anillo(s) de caras");
        growRings = Mathf.RoundToInt(GUILayout.HorizontalSlider(growRings, 0, 4));

        if (mode == Mode.Vertices)
        {
            GUILayout.Label("Muestra de puntos: " + Mathf.RoundToInt(sampleRatio * 100f) + " %");
            float newRatio = GUILayout.HorizontalSlider(sampleRatio, 0.03f, 1f);
            if (Mathf.Abs(newRatio - sampleRatio) > 0.001f)
            {
                sampleRatio = newRatio;
                BuildSamples();
                _dirty = true;
            }
            showPoints = GUILayout.Toggle(showPoints, " Ver nube de puntos");
        }

        xRay = GUILayout.Toggle(xRay, " Rayos X (ver a través de la ropa)");
        hideBody = GUILayout.Toggle(hideBody, " Ocultar malla de fondo");

        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Quitar zona")) ApplySelection();
        if (GUILayout.Button("Deshacer")) Undo();
        if (GUILayout.Button("Restaurar")) RestoreAll();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Limpiar selección")) ClearSelection();
        if (GUILayout.Button("Cambiar modo")) mode = mode == Mode.Caras ? Mode.Vertices : Mode.Caras;
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        GUILayout.Label("Seleccionado: " + SelectionLabel());
        GUILayout.Label("Caras: " + CurrentTriangleCount + " / " + _baseTriCount);
        GUILayout.Label(_status);

        GUILayout.EndArea();

        if (_sm != null) _sm.enabled = !hideBody;
    }
}
