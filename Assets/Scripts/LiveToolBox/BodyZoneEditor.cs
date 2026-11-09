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
using System.IO;
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

    [Tooltip("Si se activa, las telas alpha (materiales *_Alpha*) se atraviesan y se puede marcar lo que hay detrás. Apagado = lo que se ve es lo que se marca.")]
    public bool alphaDoesNotOcclude = false;

    [Tooltip("Alt+clic: una pieza conectada de hasta este nº de caras se toma entera; si es mayor (p. ej. un vestido fundido en una malla) se toma la isla UV.")]
    [Range(200, 8000)] public int autoPieceMax = 1600;

    [Tooltip("Alt+Mayús+clic: solape mínimo de altura entre caras vecinas para ser la misma capa (fila).")]
    [Range(0.2f, 0.8f)] public float rowOverlap = 0.5f;

    [Tooltip("Al seleccionar, incluir también la capa gemela (copia por detrás de la tela de doble cara o capa Alpha duplicada).")]
    public bool selectTwins = true;

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

    // The "body" is several renderers: M_Body plus its M_Body_Alpha* cloth layers
    // (skirt, frills...). All of them share one face/vertex index space so picking
    // and occlusion work across them.
    class BodyTarget
    {
        public SkinnedMeshRenderer sm;
        public Mesh baseMesh;
        public Vector3[] verts;
        public Vector2[] uvs;
        public BoneWeight[] weights;
        public Matrix4x4[] bind;
        public int vertOffset, subOffset, subCount, faceOffset, faceCount;
        public bool live = true;
        public bool wasEnabled = true;
    }

    SkinnedMeshRenderer _sm;                     // primary renderer (M_Body)
    readonly List<BodyTarget> _targets = new List<BodyTarget>();
    int _baseTriCount;
    int _vertCount;
    int[] _faceTarget;               // target of each face
    int[] _vertTarget;               // target of each vertex
    int[] _weld;                     // vertices at the same position (UV/normal seams) share one id
    int[] _uvWeld;                   // vertices at the same position AND uv share one id
    int[] _uvParent;                 // union-find over _uvWeld ids (UV islands)
    int[] _faceUvIsland;             // UV island (piece unwrapped as one chart in the atlas) of each face
    int[] _facePiece;                // edge-connected piece of each face
    int[] _pieceSize;                // faces per piece
    Dictionary<int, int> _uvIslandSize = new Dictionary<int, int>();
    float[] _faceY0, _faceY1;        // bind-pose height range of each face
    int[][] _faceTwins;              // coincident faces (back-face copy / duplicate alpha layer)
    const float TwinDistance = 0.0015f;   // metres; the inner shell was measured at 0.4 mm

    // ── saved presets (per character + outfit) ──────────────────────────────
    [System.Serializable]
    public class PresetTargetCut
    {
        public string target;                         // renderer name (M_Body, M_Body_Alpha0...)
        public int faceCount;                         // validation: faces of that renderer
        public List<int> ranges = new List<int>();    // inclusive [start, end] pairs of removed local faces
    }

    [System.Serializable]
    public class ZonePreset
    {
        public string name;
        public string saved;
        public List<PresetTargetCut> targets = new List<PresetTargetCut>();
    }

    [System.Serializable]
    public class ZonePresetFile
    {
        public int version = 1;
        public string key;
        public List<ZonePreset> presets = new List<ZonePreset>();
    }

    const string DefaultPresetName = "default";
    ZonePresetFile _presetFile = new ZonePresetFile();
    string _presetKey = "";
    string _currentPreset = DefaultPresetName;
    bool _presetDirty;
    bool _presetListOpen;
    string _presetName = "";
    Vector2 _presetScroll;
    Vector2 _panelScroll;
    bool _typing;                    // a text field of the panel has keyboard focus
    bool _hideApplied;

    // Body / cloth classification. The game has no per-vertex "cloth" tag: cloth that
    // moves is skinned to CySpring bones. A vertex is cloth when enough of its skin
    // weight sits on spring bones; every vertex of an *_Alpha* layer is cloth too.
    [Header("Cuerpo / ropa")]
    [Tooltip("No se puede seleccionar nada que no sea ropa (ropa = pesos en huesos con física o capas alpha).")]
    public bool lockBody = false;
    [Tooltip("Peso mínimo en huesos con física para que un vértice cuente como ropa.")]
    [Range(0.1f, 1f)] public float clothWeight = 0.5f;

    bool[] _vertCloth;
    bool[] _faceCloth;
    int _clothFaces, _bodyFaces, _springBoneCount;

    // TEMPFILE-INI1  Temporary check of the body/cloth classification: hides one of the
    // two groups so the user can see whether they are identified correctly. Remove once
    // the classification is validated.
    bool[] _previewHide;
    int _previewMode;   // 0 = all, 1 = only cloth, 2 = only body
    // TEMPFILE-FIN2

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
    const float PanelWidth = 350f;
    const float PanelHeight = 790f;
    const float TitleHeight = 26f;

    Rect _panelRect = new Rect(238f, 60f, PanelWidth, PanelHeight);
    bool _overPanel;
    bool _draggingPanel;
    Vector2 _dragOffset;
    string _status = "";

    // Invisible uGUI blocker that follows the IMGUI panel so the viewer's own
    // uGUI buttons behind it do not receive hover/clicks/scroll.
    GameObject _blockerGo;
    RectTransform _blockerRt;

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
        if (_blockerGo != null) Destroy(_blockerGo);
        if (_mat != null) Destroy(_mat);
        if (_pointsMesh != null) Destroy(_pointsMesh);
        if (_facesMesh != null) Destroy(_facesMesh);
    }

    public void Initialize()
    {
        if (_targets.Count == 0) FindTargets();

        if (_sm == null || _targets.Count == 0)
        {
            Debug.LogWarning("[BodyZoneEditor] No encuentro la malla " + bodyChildName + " en " + name);
            enabled = false;
            return;
        }

        _baseTriCount = 0;
        for (int i = 0; i < _targets.Count; i++)
        {
            BodyTarget t = _targets[i];
            Mesh current = t.sm.sharedMesh;
            string key = BodyKey() + "/" + t.sm.name;
            Mesh cached;
            if (_pristine.TryGetValue(key, out cached) && cached != null)
            {
                t.baseMesh = cached;
                if (current != cached) Debug.Log("[BodyZoneEditor] Malla actual recortada (" + current.name +
                                                 "); uso la base prístina cacheada de " + key + ".");
            }
            else
            {
                t.baseMesh = current;
                _pristine[key] = current;
            }

            t.verts = t.baseMesh.vertices;
            t.weights = t.baseMesh.boneWeights;
            t.bind = t.baseMesh.bindposes;
            t.uvs = t.baseMesh.uv;
            if (t.uvs == null || t.uvs.Length != t.verts.Length) t.uvs = new Vector2[t.verts.Length];
            _baseTriCount += CountTriangles(t.baseMesh);

            Debug.Log("[BodyZoneEditor] Base " + t.sm.name + ": " + CountTriangles(t.baseMesh) + " caras / " +
                      t.baseMesh.vertexCount + " vértices (mesh '" + t.baseMesh.name + "')");

            if (t.baseMesh.name.EndsWith("_cut"))
            {
                Debug.LogError("[BodyZoneEditor] La malla base ya es un corte (" + t.baseMesh.name +
                               "). Restaura o recarga el personaje antes de usar la herramienta.");
            }
        }

        BuildTopology();
        BuildUnits();
        BuildAtlasColors();
        BuildSamples();

        int n = _vertCount;
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

        int subs = _subGroup.Length;
        string partList = "[BodyZoneEditor] Partes (" + _partNames.Count + " de " + subs + " submeshes):";
        for (int p = 0; p < _partNames.Count; p++)
            partList += " " + p + "=" + _partNames[p] + (_partIsAlpha[p] ? "(alpha)" : "") + ";";

        _presetKey = PresetKey();
        LoadPresetFile();

        Debug.Log("[BodyZoneEditor] " + _status + " · islas: " + IslandCount +
                  " · islas de cara: " + FaceIslandCount + " · islas UV: " + UvIslandCount +
                  " · preset key: " + _presetKey + " · " + partList);
    }

    /// <summary>Collects M_Body and its M_Body_* layers (alpha cloth) as editable targets.</summary>
    void FindTargets()
    {
        _targets.Clear();
        _sm = null;

        SkinnedMeshRenderer[] all = GetComponentsInChildren<SkinnedMeshRenderer>(true);

        // primary first
        for (int i = 0; i < all.Length; i++)
        {
            SkinnedMeshRenderer sm = all[i];
            if (sm.sharedMesh == null) continue;
            if (sm.name != bodyChildName && sm.sharedMesh.name != bodyChildName) continue;

            _sm = sm;
            _targets.Add(new BodyTarget { sm = sm });
            break;
        }

        if (_sm == null) return;

        for (int i = 0; i < all.Length; i++)
        {
            SkinnedMeshRenderer sm = all[i];
            if (sm == _sm || sm.sharedMesh == null) continue;
            if (!sm.name.StartsWith(bodyChildName + "_")) continue;

            _targets.Add(new BodyTarget { sm = sm });
        }
    }

    /// <summary>Which renderers are really on screen (viewer toggles materials/layers).</summary>
    void RefreshLive()
    {
        for (int i = 0; i < _targets.Count; i++)
        {
            BodyTarget t = _targets[i];
            t.live = t.sm != null && t.sm.gameObject.activeInHierarchy && (t.sm.enabled || _hideApplied);
        }
    }

    bool FaceLive(int f)
    {
        if (_previewHide != null && _previewHide[f]) return false;   // TEMPFILE-INI1/FIN2 preview
        return _targets[_faceTarget[f]].live;
    }

    bool FaceLocked(int f)
    {
        return lockBody && _faceCloth != null && !_faceCloth[f];
    }

    /// <summary>Classifies every vertex/face as cloth or body (see the field comment).</summary>
    void BuildClothClassification()
    {
        HashSet<Transform> spring = new HashSet<Transform>();
        Gallop.CySpringController ctrl = GetComponentInChildren<Gallop.CySpringController>(true);
        if (ctrl != null)
        {
            foreach (Gallop.CySpringBoneBase b in ctrl.FindAllCySpringBones())
                if (b != null && b.Transform != null) spring.Add(b.Transform);
        }
        _springBoneCount = spring.Count;

        _vertCloth = new bool[_vertCount];
        _faceCloth = new bool[_faceCount];

        for (int ti = 0; ti < _targets.Count; ti++)
        {
            BodyTarget t = _targets[ti];
            bool alphaLayer = t.sm.name.IndexOf("Alpha", System.StringComparison.OrdinalIgnoreCase) >= 0;

            Transform[] bones = t.sm.bones;
            bool[] isSpring = new bool[bones.Length];
            for (int b = 0; b < bones.Length; b++) isSpring[b] = bones[b] != null && spring.Contains(bones[b]);

            for (int i = 0; i < t.verts.Length; i++)
            {
                bool cloth = alphaLayer;
                if (!cloth && i < t.weights.Length)
                {
                    BoneWeight w = t.weights[i];
                    float sum = 0f;
                    if (w.boneIndex0 < isSpring.Length && isSpring[w.boneIndex0]) sum += w.weight0;
                    if (w.boneIndex1 < isSpring.Length && isSpring[w.boneIndex1]) sum += w.weight1;
                    if (w.boneIndex2 < isSpring.Length && isSpring[w.boneIndex2]) sum += w.weight2;
                    if (w.boneIndex3 < isSpring.Length && isSpring[w.boneIndex3]) sum += w.weight3;
                    cloth = sum >= clothWeight;
                }
                _vertCloth[t.vertOffset + i] = cloth;
            }
        }

        _clothFaces = 0;
        _bodyFaces = 0;
        for (int f = 0; f < _faceCount; f++)
        {
            int c = 0;
            for (int k = 0; k < 3; k++) if (_vertCloth[_faces[f * 3 + k]]) c++;

            bool cloth = c >= 2 || _partIsAlpha[_subGroup[_faceSub[f]]];
            _faceCloth[f] = cloth;
            if (cloth) _clothFaces++; else _bodyFaces++;
        }

        Debug.Log("[BodyZoneEditor] Clasificación: ropa " + _clothFaces + " caras · cuerpo " + _bodyFaces +
                  " caras · huesos con física: " + _springBoneCount);
    }

    // TEMPFILE-INI1  Preview buttons (see _previewHide)
    void SetPreview(int mode)
    {
        _previewMode = mode;

        if (mode == 0 || _faceCloth == null)
        {
            _previewHide = null;
            _previewMode = 0;
        }
        else
        {
            _previewHide = new bool[_faceCount];
            for (int f = 0; f < _faceCount; f++)
                _previewHide[f] = mode == 1 ? !_faceCloth[f] : _faceCloth[f];
        }

        _selectedFaces = new bool[_faceCount];
        _selectedVerts = new bool[_vertCount];
        RebuildMeshes();
        _dirty = true;
    }
    // TEMPFILE-FIN2

    bool VertLive(int v)
    {
        return _targets[_vertTarget[v]].live;
    }

    /// <summary>
    /// True for faces of alpha cloth that must be ignored (seen through) because
    /// "alpha no tapan" is on. The alpha part being edited never passes through.
    /// </summary>
    bool IsAlphaPassThrough(int f)
    {
        if (!alphaDoesNotOcclude) return false;
        int g = _subGroup[_faceSub[f]];
        return _partIsAlpha[g] && g != paintPart;
    }

    void ApplyHide()
    {
        if (hideBody == _hideApplied) return;
        _hideApplied = hideBody;

        for (int i = 0; i < _targets.Count; i++)
        {
            BodyTarget t = _targets[i];
            if (t.sm == null) continue;

            if (hideBody)
            {
                t.wasEnabled = t.sm.enabled;
                t.sm.enabled = false;
            }
            else
            {
                t.sm.enabled = t.wasEnabled;
            }
        }
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
    void SetMesh(BodyTarget t, Mesh mesh)
    {
        if (t == null || t.sm == null) return;

        Mesh previous = t.sm.sharedMesh;
        t.sm.sharedMesh = mesh;

        if (previous != null && previous != mesh && previous != t.baseMesh)
            Destroy(previous);
    }

    /// <summary>Rebuilds every target mesh from the current cut mask.</summary>
    void RebuildMeshes()
    {
        // real cuts + (temporary) preview hiding
        bool[] mask = _cutFaces;
        if (_previewHide != null)   // TEMPFILE-INI1/FIN2
        {
            mask = new bool[_faceCount];
            for (int f = 0; f < _faceCount; f++) mask[f] = _cutFaces[f] || _previewHide[f];
        }

        for (int i = 0; i < _targets.Count; i++)
        {
            BodyTarget t = _targets[i];

            bool any = false;
            for (int f = t.faceOffset; f < t.faceOffset + t.faceCount; f++)
            {
                if (mask[f]) { any = true; break; }
            }

            SetMesh(t, any ? BuildCutMesh(t, mask) : t.baseMesh);
        }
    }

    public int CurrentTriangleCount
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _targets.Count; i++)
                if (_targets[i].sm != null) total += CountTriangles(_targets[i].sm.sharedMesh);
            return total;
        }
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

    int UvIslandCount
    {
        get
        {
            HashSet<int> ids = new HashSet<int>();
            for (int i = 0; i < _faceUvIsland.Length; i++) ids.Add(_faceUvIsland[i]);
            return ids.Count;
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
        // global index space over all targets
        int totalSubs = 0;
        _vertCount = 0;
        for (int ti = 0; ti < _targets.Count; ti++)
        {
            BodyTarget t = _targets[ti];
            t.vertOffset = _vertCount;
            t.subOffset = totalSubs;
            t.subCount = t.baseMesh.subMeshCount;
            _vertCount += t.baseMesh.vertexCount;
            totalSubs += t.subCount;
        }

        int n = _vertCount;
        _island = new int[n];
        _neighbors = new List<int>[n];
        _weld = new int[n];
        _vertTarget = new int[n];
        for (int i = 0; i < n; i++)
        {
            _island[i] = i;
            _neighbors[i] = new List<int>(4);
        }

        // Weld vertices that sit at the same position (UV / hard-normal seams duplicate
        // them). Without this a "piece" is split along every seam.
        Dictionary<Vector3Int, int> weldMap = new Dictionary<Vector3Int, int>();
        for (int ti = 0; ti < _targets.Count; ti++)
        {
            BodyTarget t = _targets[ti];
            weldMap.Clear();

            for (int i = 0; i < t.verts.Length; i++)
            {
                Vector3 p = t.verts[i];
                Vector3Int key = new Vector3Int(
                    Mathf.RoundToInt(p.x * 10000f),
                    Mathf.RoundToInt(p.y * 10000f),
                    Mathf.RoundToInt(p.z * 10000f));

                int g = t.vertOffset + i;
                int w;
                if (!weldMap.TryGetValue(key, out w))
                {
                    w = g;
                    weldMap[key] = w;
                }

                _weld[g] = w;
                _vertTarget[g] = ti;
            }
        }

        // Same weld but ALSO by UV: vertices that share position and UV belong to the same
        // chart of the texture atlas. Connecting faces through these gives UV islands,
        // which is how the artist unwrapped each layer / piece of cloth.
        _uvWeld = new int[n];
        _uvParent = new int[n];
        Dictionary<(int, int, int, int, int), int> uvWeldMap = new Dictionary<(int, int, int, int, int), int>();
        for (int ti = 0; ti < _targets.Count; ti++)
        {
            BodyTarget t = _targets[ti];
            uvWeldMap.Clear();

            for (int i = 0; i < t.verts.Length; i++)
            {
                Vector3 p = t.verts[i];
                Vector2 uv = t.uvs[i];
                var key = (Mathf.RoundToInt(p.x * 10000f), Mathf.RoundToInt(p.y * 10000f),
                           Mathf.RoundToInt(p.z * 10000f), Mathf.RoundToInt(uv.x * 10000f),
                           Mathf.RoundToInt(uv.y * 10000f));

                int g = t.vertOffset + i;
                int w;
                if (!uvWeldMap.TryGetValue(key, out w))
                {
                    w = g;
                    uvWeldMap[key] = w;
                }

                _uvWeld[g] = w;
            }
        }
        for (int i = 0; i < n; i++) _uvParent[i] = i;

        _subGroup = new int[totalSubs];
        _partNames.Clear();
        _partIsAlpha.Clear();

        List<int> faceIdx = new List<int>(16384);
        List<int> faceSub = new List<int>(16384);
        List<int> faceTri = new List<int>(16384);
        List<int> faceTarget = new List<int>(16384);

        for (int ti = 0; ti < _targets.Count; ti++)
        {
            BodyTarget bt = _targets[ti];
            int subCount = bt.subCount;

            List<int[]> subTris = new List<int[]>(subCount);
            for (int s = 0; s < subCount; s++) subTris.Add(bt.baseMesh.GetTriangles(s));

            Material[] mats = bt.sm.sharedMaterials;

            // agrupa submeshes con los mismos triángulos (doble cara del mismo material)
            for (int s = 0; s < subCount; s++)
            {
                int group = -1;
                for (int prev = 0; prev < s; prev++)
                {
                    if (!SameTriangles(subTris[s], subTris[prev])) continue;
                    group = _subGroup[bt.subOffset + prev];
                    break;
                }

                if (group < 0)
                {
                    group = _partNames.Count;

                    Material m = mats != null && s < mats.Length ? mats[s] : null;
                    string mn = m != null ? m.name : (bt.sm.name + " #" + s);

                    _partNames.Add(mn);
                    _partIsAlpha.Add(mn.IndexOf("Alpha", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     bt.sm.name.IndexOf("Alpha", System.StringComparison.OrdinalIgnoreCase) >= 0);
                }

                _subGroup[bt.subOffset + s] = group;
            }

            bt.faceOffset = faceSub.Count;

            for (int s = 0; s < subCount; s++)
            {
                int[] tris = subTris[s];
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = tris[t] + bt.vertOffset;
                    int b = tris[t + 1] + bt.vertOffset;
                    int c = tris[t + 2] + bt.vertOffset;

                    faceIdx.Add(a); faceIdx.Add(b); faceIdx.Add(c);
                    faceSub.Add(bt.subOffset + s);
                    faceTri.Add(t / 3);
                    faceTarget.Add(ti);

                    Link(a, b); Link(b, c); Link(c, a);

                    // all three corners belong to the same piece (the old code only joined a-b,
                    // leaving lone corners as one-vertex islands)
                    Union(_weld[a], _weld[b]);
                    Union(_weld[b], _weld[c]);

                    UnionUv(_uvWeld[a], _uvWeld[b]);
                    UnionUv(_uvWeld[b], _uvWeld[c]);
                }
            }

            bt.faceCount = faceSub.Count - bt.faceOffset;
        }

        _faces = faceIdx.ToArray();
        _faceSub = faceSub.ToArray();
        _faceTri = faceTri.ToArray();
        _faceTarget = faceTarget.ToArray();
        _faceCount = _faceSub.Length;

        _faceUvIsland = new int[_faceCount];
        for (int f = 0; f < _faceCount; f++) _faceUvIsland[f] = FindUv(_uvWeld[_faces[f * 3]]);

        // compactar ids de isla de vértice
        Dictionary<int, int> map = new Dictionary<int, int>();
        int next = 0;
        for (int v = 0; v < n; v++)
        {
            int root = Find(_weld[v]);
            int id;
            if (!map.TryGetValue(root, out id))
            {
                id = next++;
                map[root] = id;
            }
            _island[v] = id;
        }

        // isla por cara + adyacencia cara-cara (por aristas soldadas) dentro del mismo material
        _faceIsland = new int[_faceCount];
        _faceNeighbors = new List<int>[_faceCount];

        int groupCount = _partNames.Count;
        List<Dictionary<long, int>> edgeMaps = new List<Dictionary<long, int>>(groupCount);
        for (int g = 0; g < groupCount; g++) edgeMaps.Add(new Dictionary<long, int>(4096));

        for (int f = 0; f < _faceCount; f++)
        {
            _faceIsland[f] = _island[_faces[f * 3]];
            _faceNeighbors[f] = new List<int>(3);
        }

        for (int f = 0; f < _faceCount; f++)
        {
            Dictionary<long, int> edgeFace = edgeMaps[_subGroup[_faceSub[f]]];

            for (int k = 0; k < 3; k++)
            {
                int a = _weld[_faces[f * 3 + k]];
                int b = _weld[_faces[f * 3 + (k + 1) % 3]];
                if (a == b) continue;
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

    int FindUv(int x)
    {
        while (_uvParent[x] != x)
        {
            _uvParent[x] = _uvParent[_uvParent[x]];
            x = _uvParent[x];
        }
        return x;
    }

    void UnionUv(int a, int b)
    {
        int ra = FindUv(a), rb = FindUv(b);
        if (ra != rb) _uvParent[ra] = rb;
    }

    void Link(int a, int b)
    {
        if (!_neighbors[a].Contains(b)) _neighbors[a].Add(b);
        if (!_neighbors[b].Contains(a)) _neighbors[b].Add(a);
    }

    // ── color del atlas por vértice (solo modo Vértices) ─────────────────────
    void BuildAtlasColors()
    {
        _vertColor = new Color[_vertCount];
        Color fallback = new Color(0.75f, 0.75f, 0.8f, 1f);

        for (int ti = 0; ti < _targets.Count; ti++)
        {
            BodyTarget bt = _targets[ti];
            int n = bt.baseMesh.vertexCount;
            Vector2[] uvs = bt.baseMesh.uv;

            Texture2D tex = null;
            Material[] mats = bt.sm.sharedMaterials;
            if (mats != null && mats.Length > 0 && mats[0] != null)
                tex = mats[0].mainTexture as Texture2D;

            Color[] px = (tex == null || uvs == null || uvs.Length < n) ? null : ReadTexturePixels(tex);
            if (px == null)
            {
                for (int i = 0; i < n; i++) _vertColor[bt.vertOffset + i] = fallback;
                continue;
            }

            int w = tex.width, h = tex.height;
            for (int i = 0; i < n; i++)
            {
                int x = Mathf.Clamp(Mathf.RoundToInt(uvs[i].x * (w - 1)), 0, w - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(uvs[i].y * (h - 1)), 0, h - 1);
                Color c = px[y * w + x];
                c.a = 1f;
                _vertColor[bt.vertOffset + i] = c;
            }
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
        for (int ti = 0; ti < _targets.Count; ti++)
        {
            BodyTarget t = _targets[ti];
            if (t.sm == null) continue;

            Transform[] bones = t.sm.bones;
            Matrix4x4[] bind = t.bind;
            Vector3[] verts = t.verts;
            BoneWeight[] bw = t.weights;

            Matrix4x4[] mats = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                mats[i] = bones[i] != null && i < bind.Length ? bones[i].localToWorldMatrix * bind[i] : Matrix4x4.identity;

            for (int i = 0; i < verts.Length; i++)
            {
                BoneWeight w = i < bw.Length ? bw[i] : default(BoneWeight);
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
                _posed[t.vertOffset + i] = p;
            }
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
        // The character was destroyed/reloaded: without this guard SkinPose throws
        // every frame and aborts Update before HandleCamera, freezing the camera.
        if (_sm == null || _targets.Count == 0)
        {
            if (editMode) SetEditMode(false);
            UpdateBlocker();
            return;
        }

        if (Input.GetKeyDown(toggleKey)) SetEditMode(!editMode);

        UpdateBlocker();
        if (!editMode) return;

        // The scene may swap its main camera (Version2 has several): rebind.
        if (_cam == null || !_cam.isActiveAndEnabled)
        {
            Camera main = Camera.main;
            if (main != _cam) BindCamera();
        }
        if (_cam == null) return;

        // Nothing else must fight our camera while editing.
        if (_orbit != null && _orbit.enabled) _orbit.enabled = false;

        if (Input.GetKeyDown(modeKey) && !_typing)
        {
            mode = mode == Mode.Caras ? Mode.Vertices : Mode.Caras;
            if (mode == Mode.Vertices) showPoints = true;
            _dirty = true;
        }

        RefreshLive();

        if (Time.unscaledTime - _posedStamp > 0.08f)
        {
            SkinPose();
            _dirty = true;
        }

        Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        _overPanel = _panelRect.Contains(guiMouse);

        HandleCamera();
        HandlePaint();
        if (!_typing) HandleKeys();

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
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetMouseButtonDown(0))
        {
            PaintAt(Input.mousePosition, alt, ctrl, false, shift);
            _painting = !alt;
        }
        else if (Input.GetMouseButton(0) && _painting)
        {
            PaintAt(Input.mousePosition, false, ctrl, true);
        }

        if (Input.GetMouseButtonUp(0)) _painting = false;
    }

    void PaintAt(Vector2 screenPos, bool island, bool deselect, bool continuous, bool byRow = false)
    {
        Vector3 hit;
        int face;
        if (!RaycastBody(screenPos, out hit, out face)) return;

        if (mode == Mode.Caras)
        {
            if (island)
            {
                // Alt = unit (piece / UV island) · Alt+Shift = row (tier) by height
                if (byRow) SelectFaceRow(face, deselect);
                else SelectFaceUnit(face, deselect);
            }
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
            BindCamera();
            BuildClothClassification();
            LoadPresetFile();   // pick up edits made on disk

            // Keyboard focus on a uGUI button would make Enter "click" it.
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            SkinPose();
            _dirty = true;
        }
        else
        {
            _draggingPanel = false;
            _camDragging = false;
            _painting = false;
            if (_previewHide != null) SetPreview(0);   // TEMPFILE-INI1/FIN2
            RestoreOrbit();
            hideBody = false;
            ApplyHide();
            if (_pointsGo != null) _pointsGo.SetActive(false);
            if (_facesGo != null) _facesGo.SetActive(false);
        }
    }

    /// <summary>Takes control of the current main camera (disables its CameraOrbit).</summary>
    void BindCamera()
    {
        RestoreOrbit();

        _cam = Camera.main;
        if (_cam == null) return;

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

    /// <summary>Keeps the invisible uGUI blocker over the IMGUI panel.</summary>
    void UpdateBlocker()
    {
        bool show = editMode;

        if (show && _blockerGo == null)
        {
            _blockerGo = new GameObject("BodyZoneUIBlocker");
            _blockerGo.hideFlags = HideFlags.DontSave;

            Canvas canvas = _blockerGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            _blockerGo.AddComponent<GraphicRaycaster>();

            GameObject img = new GameObject("Blocker");
            img.hideFlags = HideFlags.DontSave;
            img.transform.SetParent(_blockerGo.transform, false);

            Image image = img.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;

            _blockerRt = img.GetComponent<RectTransform>();
            _blockerRt.anchorMin = Vector2.zero;
            _blockerRt.anchorMax = Vector2.zero;
            _blockerRt.pivot = Vector2.zero;
        }

        if (_blockerGo == null) return;

        if (_blockerGo.activeSelf != show) _blockerGo.SetActive(show);
        if (!show) return;

        // IMGUI rect has its origin top-left; uGUI overlay has it bottom-left.
        _blockerRt.anchoredPosition = new Vector2(_panelRect.x, Screen.height - _panelRect.yMax);
        _blockerRt.sizeDelta = new Vector2(_panelRect.width, _panelRect.height);
    }

    void RestoreOrbit()
    {
        if (_orbit != null) _orbit.enabled = _orbitWasEnabled;
        _orbit = null;
    }

    void HandleCamera()
    {
        if (_cam == null) return;

        bool blocked = _overPanel || _draggingPanel || PointerBlockedByUI();

        // A drag starts only on a free area, and once started it keeps going even if
        // the cursor later crosses UI (previously it was re-evaluated every frame and
        // used a stale _lastMouse, which made the rotation get "stuck").
        if (Input.GetMouseButtonDown(1) && !blocked)
        {
            _camDragging = true;
            _lastMouse = Input.mousePosition;
        }
        if (!Input.GetMouseButton(1)) _camDragging = false;

        if (_camDragging)
        {
            Vector3 delta = Input.mousePosition - _lastMouse;
            _lastMouse = Input.mousePosition;
            _yaw += delta.x * 0.25f;
            _pitch -= delta.y * 0.25f;
            _pitch = Mathf.Clamp(_pitch, -80f, 80f);
        }

        if (!blocked)
        {
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
            if (_cutFaces[f] || !FaceLive(f)) continue;
            if (paintPart >= 0 && _subGroup[_faceSub[f]] != paintPart) continue;
            if (IsAlphaPassThrough(f) || FaceLocked(f)) continue;

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
            if (_cutFaces[f] || !FaceLive(f)) continue;
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
            if (_cutFaces[f] || !FaceLive(f)) continue;

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
                if (IsAlphaPassThrough(f)) continue;
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
                if (IsAlphaPassThrough(f)) continue;

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
    /// <summary>Prepares the occlusion grid / visibility cache (no-op with X-ray on).</summary>
    void PrepareVisibility()
    {
        if (xRay || _cam == null) return;

        EnsureGrid();
        if ((_visCamPos - _cam.transform.position).sqrMagnitude > 0.0001f)
        {
            _visCamPos = _cam.transform.position;
            _visEpoch++;
        }
    }

    void SelectFacesSphere(Vector3 center, float radius, bool deselect)
    {
        PrepareVisibility();

        float r2 = radius * radius;
        for (int f = 0; f < _faceCount; f++)
        {
            if (_cutFaces[f] || !FaceLive(f)) continue;
            if (paintPart >= 0 && _subGroup[_faceSub[f]] != paintPart) continue;
            if (IsAlphaPassThrough(f) || FaceLocked(f)) continue;

            Vector3 c = FaceCenter(f);
            if ((c - center).sqrMagnitude > r2) continue;

            if (!xRay && !IsFaceVisible(f)) continue;   // tapada por otra parte de la malla

            SetSelected(f, !deselect);
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

    // ── unidades (Alt+clic) ──────────────────────────────────────────────────
    /// <summary>Can this face be taken by a bulk selection (unit / row)?</summary>
    bool CanTakeFace(int f)
    {
        if (_cutFaces[f] || !FaceLive(f)) return false;
        if (paintPart >= 0 && _subGroup[_faceSub[f]] != paintPart) return false;
        if (IsAlphaPassThrough(f) || FaceLocked(f)) return false;
        return true;
    }

    /// <summary>
    /// Selects/deselects a face and, when "gemelas" is on, its coincident twins (the
    /// back-face copy of double-sided cloth, or the duplicate Alpha0/Alpha1 layer).
    /// </summary>
    void SetSelected(int f, bool value)
    {
        _selectedFaces[f] = value;

        if (!selectTwins || _faceTwins == null) return;
        int[] tw = _faceTwins[f];
        if (tw == null) return;

        for (int i = 0; i < tw.Length; i++)
        {
            int g = tw[i];
            if (_cutFaces[g] || !FaceLive(g) || FaceLocked(g)) continue;
            if (paintPart >= 0 && _subGroup[_faceSub[g]] != paintPart) continue;
            _selectedFaces[g] = value;
        }
    }

    /// <summary>
    /// Alt+click. The unit is the edge-connected piece when it is small (a ribbon ring,
    /// a bow...) and the UV island when the piece is huge (a whole dress fused in one
    /// mesh). Twin layers that mostly overlap the unit are adopted whole.
    /// </summary>
    void SelectFaceUnit(int face, bool deselect)
    {
        if (face < 0 || face >= _faceCount) return;

        bool usePiece = _pieceSize[_facePiece[face]] <= autoPieceMax;
        int[] ids = usePiece ? _facePiece : _faceUvIsland;
        int id = ids[face];

        int count = 0;
        Dictionary<int, int> twinHits = new Dictionary<int, int>();

        for (int f = 0; f < _faceCount; f++)
        {
            if (ids[f] != id || !CanTakeFace(f)) continue;

            SetSelected(f, !deselect);
            count++;

            int[] tw = _faceTwins != null ? _faceTwins[f] : null;
            if (tw == null || !selectTwins) continue;

            for (int i = 0; i < tw.Length; i++)
            {
                int u = ids[tw[i]];
                if (u == id) continue;

                int hits;
                twinHits.TryGetValue(u, out hits);
                twinHits[u] = hits + 1;
            }
        }

        // adopt whole twin units (e.g. the inner shell, which has extra faces of its own)
        int adopted = 0;
        foreach (KeyValuePair<int, int> kv in twinHits)
        {
            int size = usePiece ? _pieceSize[kv.Key] : _uvIslandSize[kv.Key];
            if (kv.Value < size * 0.6f) continue;

            adopted++;
            for (int f = 0; f < _faceCount; f++)
            {
                if (ids[f] != kv.Key || !CanTakeFace(f)) continue;
                SetSelected(f, !deselect);
                count++;
            }
        }

        _dirty = true;
        _status = (usePiece ? "Pieza: " : "Isla UV: ") + count + " caras" +
                  (adopted > 0 ? " (+" + adopted + " capa gemela)" : string.Empty) + " · " + SelectionLabel();
    }

    /// <summary>
    /// Alt+Shift+click: one "row" (tier) of a stacked cloth: grows over edges while the
    /// height ranges (bind pose) of neighbouring faces overlap enough. It splits tulle
    /// skirts into their tiers.
    /// </summary>
    void SelectFaceRow(int face, bool deselect)
    {
        if (face < 0 || face >= _faceCount) return;

        bool[] seen = new bool[_faceCount];
        Queue<int> queue = new Queue<int>(256);
        queue.Enqueue(face);
        seen[face] = true;

        int count = 0;
        while (queue.Count > 0)
        {
            int f = queue.Dequeue();
            SetSelected(f, !deselect);
            count++;

            List<int> nb = _faceNeighbors[f];
            for (int i = 0; i < nb.Count; i++)
            {
                int o = nb[i];
                if (seen[o] || !CanTakeFace(o)) continue;

                float overlap = Mathf.Min(_faceY1[f], _faceY1[o]) - Mathf.Max(_faceY0[f], _faceY0[o]);
                float smaller = Mathf.Min(_faceY1[f] - _faceY0[f], _faceY1[o] - _faceY0[o]);

                if (smaller <= 0.000001f)
                {
                    if (overlap < -0.001f) continue;      // flat face: must touch the other range
                }
                else if (overlap / smaller < rowOverlap) continue;

                seen[o] = true;
                queue.Enqueue(o);
            }
        }

        _dirty = true;
        _status = "Capa por altura: " + count + " caras · " + SelectionLabel();
    }

    /// <summary>
    /// Pieces (edge connected), UV island sizes, bind-pose heights and coincident twins.
    /// </summary>
    void BuildUnits()
    {
        // edge-connected pieces
        _facePiece = new int[_faceCount];
        for (int f = 0; f < _faceCount; f++) _facePiece[f] = -1;

        List<int> sizes = new List<int>();
        Stack<int> stack = new Stack<int>();
        for (int s = 0; s < _faceCount; s++)
        {
            if (_facePiece[s] >= 0) continue;

            int id = sizes.Count;
            int n = 0;
            _facePiece[s] = id;
            stack.Push(s);

            while (stack.Count > 0)
            {
                int f = stack.Pop();
                n++;

                List<int> nb = _faceNeighbors[f];
                for (int i = 0; i < nb.Count; i++)
                {
                    int o = nb[i];
                    if (_facePiece[o] >= 0) continue;
                    _facePiece[o] = id;
                    stack.Push(o);
                }
            }

            sizes.Add(n);
        }
        _pieceSize = sizes.ToArray();

        _uvIslandSize = new Dictionary<int, int>();
        for (int f = 0; f < _faceCount; f++)
        {
            int hits;
            _uvIslandSize.TryGetValue(_faceUvIsland[f], out hits);
            _uvIslandSize[_faceUvIsland[f]] = hits + 1;
        }

        // bind-pose centroid / normal / height range of each face
        Vector3[] center = new Vector3[_faceCount];
        Vector3[] normal = new Vector3[_faceCount];
        _faceY0 = new float[_faceCount];
        _faceY1 = new float[_faceCount];

        for (int f = 0; f < _faceCount; f++)
        {
            BodyTarget t = _targets[_faceTarget[f]];
            Vector3 a = t.verts[_faces[f * 3] - t.vertOffset];
            Vector3 b = t.verts[_faces[f * 3 + 1] - t.vertOffset];
            Vector3 c = t.verts[_faces[f * 3 + 2] - t.vertOffset];

            center[f] = (a + b + c) / 3f;
            normal[f] = Vector3.Cross(b - a, c - a).normalized;
            _faceY0[f] = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
            _faceY1[f] = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
        }

        // coincident twins: centroids closer than TwinDistance with (anti)parallel normals.
        // The inner shell of double-sided cloth sits ~0.4 mm from the outer one.
        float cell = TwinDistance * 2f;
        Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>(_faceCount);
        for (int f = 0; f < _faceCount; f++)
        {
            long key = PackCell(Mathf.FloorToInt(center[f].x / cell), Mathf.FloorToInt(center[f].y / cell), Mathf.FloorToInt(center[f].z / cell));
            List<int> bucket;
            if (!grid.TryGetValue(key, out bucket))
            {
                bucket = new List<int>(2);
                grid[key] = bucket;
            }
            bucket.Add(f);
        }

        List<int>[] tmp = new List<int>[_faceCount];
        int pairs = 0;
        float maxSq = TwinDistance * TwinDistance;

        for (int f = 0; f < _faceCount; f++)
        {
            int cx = Mathf.FloorToInt(center[f].x / cell);
            int cy = Mathf.FloorToInt(center[f].y / cell);
            int cz = Mathf.FloorToInt(center[f].z / cell);

            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                List<int> bucket;
                if (!grid.TryGetValue(PackCell(cx + dx, cy + dy, cz + dz), out bucket)) continue;

                for (int i = 0; i < bucket.Count; i++)
                {
                    int g = bucket[i];
                    if (g <= f) continue;
                    if ((center[g] - center[f]).sqrMagnitude > maxSq) continue;
                    if (Mathf.Abs(Vector3.Dot(normal[f], normal[g])) < 0.7f) continue;

                    if (tmp[f] == null) tmp[f] = new List<int>(2);
                    if (tmp[g] == null) tmp[g] = new List<int>(2);
                    tmp[f].Add(g);
                    tmp[g].Add(f);
                    pairs++;
                }
            }
        }

        _faceTwins = new int[_faceCount][];
        for (int f = 0; f < _faceCount; f++) if (tmp[f] != null) _faceTwins[f] = tmp[f].ToArray();

        Debug.Log("[BodyZoneEditor] Unidades: " + _pieceSize.Length + " piezas · " + _uvIslandSize.Count +
                  " islas UV · " + pairs + " pares de caras gemelas");
    }

    // ── selección: vértices ──────────────────────────────────────────────────
    void SelectVertsSphere(Vector3 center, float radius, bool deselect)
    {
        PrepareVisibility();

        float r2 = radius * radius;
        for (int v = 0; v < _selectedVerts.Length; v++)
        {
            if (_cutVerts[v] || !VertLive(v)) continue;
            if (lockBody && _vertCloth != null && !_vertCloth[v]) continue;
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
        _selectedVerts = new bool[_vertCount];
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
        _cutVerts = new bool[_vertCount];
        for (int f = 0; f < _faceCount; f++)
        {
            if (!_cutFaces[f]) continue;
            _cutVerts[_faces[f * 3]] = true;
            _cutVerts[_faces[f * 3 + 1]] = true;
            _cutVerts[_faces[f * 3 + 2]] = true;
        }
    }

    Mesh BuildCutMesh(BodyTarget bt, bool[] facesMask)
    {
        Mesh mesh = Instantiate(bt.baseMesh);
        mesh.name = bt.baseMesh.name + "_cut";

        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            int[] tris = bt.baseMesh.GetTriangles(s);
            List<int> keep = new List<int>(tris.Length);
            int globalSub = bt.subOffset + s;

            for (int f = bt.faceOffset; f < bt.faceOffset + bt.faceCount; f++)
            {
                if (_faceSub[f] != globalSub || facesMask[f]) continue;

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

        RebuildMeshes();

        _selectedFaces = new bool[_faceCount];
        _selectedVerts = new bool[_vertCount];
        _dirty = true;

        _presetDirty = true;
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

        if (_applied.Count == 0) _cutFaces = new bool[_faceCount];
        else _cutFaces = (bool[])_applied[_applied.Count - 1].Clone();

        RebuildMeshes();

        RebuildCutVerts();
        _selectedFaces = new bool[_faceCount];
        _selectedVerts = new bool[_vertCount];
        _dirty = true;
        _presetDirty = true;
        _status = "Deshecho · caras: " + CurrentTriangleCount + " / " + _baseTriCount;
    }

    public void RestoreAll()
    {
        _applied.Clear();
        _cutFaces = new bool[_faceCount];
        _cutVerts = new bool[_vertCount];
        RebuildMeshes();
        _selectedFaces = new bool[_faceCount];
        _selectedVerts = new bool[_vertCount];
        _dirty = true;
        _currentPreset = DefaultPresetName;
        _presetDirty = false;
        _status = "Malla original restaurada · caras: " + CurrentTriangleCount + " / " + _baseTriCount;
    }

    // ── presets guardados (por personaje + traje) ────────────────────────────
    /// <summary>Key = character id + outfit id, so presets never leak between outfits.</summary>
    string PresetKey()
    {
        UmaContainerCharacter c = GetComponent<UmaContainerCharacter>();

        string chara = c != null && c.CharaEntry != null
            ? c.CharaEntry.Id.ToString()
            : name.Replace("(Clone)", string.Empty);

        string dress = c != null ? c.VarCostumeIdLong : null;
        if (string.IsNullOrEmpty(dress)) dress = BodyKey();

        string key = chara + "_" + dress;
        foreach (char bad in Path.GetInvalidFileNameChars()) key = key.Replace(bad, '-');
        return key;
    }

    string PresetPath()
    {
        // same convention as Poses/ and Screenshots/: next to Assets/ (project root)
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BodyZonePresets", _presetKey + ".json"));
    }

    void LoadPresetFile()
    {
        _presetFile = new ZonePresetFile { key = _presetKey };

        try
        {
            string path = PresetPath();
            if (File.Exists(path))
            {
                ZonePresetFile loaded = JsonUtility.FromJson<ZonePresetFile>(File.ReadAllText(path));
                if (loaded != null && loaded.presets != null) _presetFile = loaded;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[BodyZoneEditor] No se pudieron leer los presets: " + ex.Message);
        }
    }

    void SavePresetFile()
    {
        try
        {
            string path = PresetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            _presetFile.key = _presetKey;
            File.WriteAllText(path, JsonUtility.ToJson(_presetFile, true));
        }
        catch (System.Exception ex)
        {
            _status = "Error al guardar: " + ex.Message;
            Debug.LogError("[BodyZoneEditor] " + _status);
        }
    }

    ZonePreset FindPreset(string presetName)
    {
        for (int i = 0; i < _presetFile.presets.Count; i++)
            if (string.Equals(_presetFile.presets[i].name, presetName, System.StringComparison.OrdinalIgnoreCase))
                return _presetFile.presets[i];
        return null;
    }

    public void SavePreset(string presetName)
    {
        presetName = (presetName ?? string.Empty).Trim();

        if (presetName.Length == 0) { _status = "Escribe un nombre para el preset"; return; }
        if (string.Equals(presetName, DefaultPresetName, System.StringComparison.OrdinalIgnoreCase))
        {
            _status = "\"default\" es el traje sin modificar: usa otro nombre";
            return;
        }

        bool any = false;
        for (int f = 0; f < _faceCount; f++) if (_cutFaces[f]) { any = true; break; }
        if (!any) { _status = "Nada que guardar: no hay caras quitadas"; return; }

        ZonePreset preset = new ZonePreset
        {
            name = presetName,
            saved = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm")
        };

        for (int i = 0; i < _targets.Count; i++)
        {
            BodyTarget t = _targets[i];
            PresetTargetCut tc = new PresetTargetCut { target = t.sm.name, faceCount = t.faceCount };

            int start = -1;
            for (int k = 0; k <= t.faceCount; k++)
            {
                bool cut = k < t.faceCount && _cutFaces[t.faceOffset + k];
                if (cut && start < 0) start = k;
                if (!cut && start >= 0)
                {
                    tc.ranges.Add(start);
                    tc.ranges.Add(k - 1);
                    start = -1;
                }
            }

            preset.targets.Add(tc);
        }

        ZonePreset existing = FindPreset(presetName);
        if (existing != null) _presetFile.presets.Remove(existing);
        _presetFile.presets.Add(preset);
        SavePresetFile();

        _currentPreset = preset.name;
        _presetDirty = false;
        _status = "Preset \"" + preset.name + "\" guardado (" + _presetKey + ")";
    }

    public void ApplyPreset(ZonePreset preset)
    {
        if (preset == null) return;

        bool[] mask = new bool[_faceCount];
        for (int i = 0; i < _targets.Count; i++)
        {
            BodyTarget t = _targets[i];

            PresetTargetCut tc = null;
            for (int k = 0; k < preset.targets.Count; k++)
                if (preset.targets[k].target == t.sm.name) { tc = preset.targets[k]; break; }

            if (tc == null) continue;

            if (tc.faceCount != t.faceCount)
            {
                _status = "Preset incompatible con " + t.sm.name + " (" + tc.faceCount + " vs " + t.faceCount + " caras)";
                Debug.LogWarning("[BodyZoneEditor] " + _status);
                return;
            }

            for (int r = 0; r + 1 < tc.ranges.Count; r += 2)
            {
                int a = Mathf.Max(0, tc.ranges[r]);
                int b = Mathf.Min(t.faceCount - 1, tc.ranges[r + 1]);
                for (int k = a; k <= b; k++) mask[t.faceOffset + k] = true;
            }
        }

        _applied.Clear();
        _cutFaces = mask;
        _applied.Add((bool[])mask.Clone());
        RebuildCutVerts();
        RebuildMeshes();

        _selectedFaces = new bool[_faceCount];
        _selectedVerts = new bool[_vertCount];
        _dirty = true;

        _currentPreset = preset.name;
        _presetDirty = false;
        _status = "Preset \"" + preset.name + "\" aplicado · caras: " + CurrentTriangleCount + " / " + _baseTriCount;
    }

    public void DeletePreset(string presetName)
    {
        ZonePreset p = FindPreset(presetName);
        if (p == null) { _status = "Selecciona un preset guardado para borrarlo"; return; }

        _presetFile.presets.Remove(p);
        SavePresetFile();

        if (string.Equals(_currentPreset, presetName, System.StringComparison.OrdinalIgnoreCase))
            _currentPreset = DefaultPresetName;

        _status = "Preset \"" + presetName + "\" borrado";
    }

    void DrawPresetUI()
    {
        GUILayout.Label("Traje: " + _presetKey);

        string current = _currentPreset + (_presetDirty ? "  (modificado)" : string.Empty);
        if (GUILayout.Button((_presetListOpen ? "^  " : "v  ") + current))
            _presetListOpen = !_presetListOpen;

        if (_presetListOpen)
        {
            int rows = _presetFile.presets.Count + 1;
            _presetScroll = GUILayout.BeginScrollView(_presetScroll, GUILayout.Height(Mathf.Min(150f, rows * 24f + 4f)));

            if (GUILayout.Button(DefaultPresetName + "  (sin modificar)"))
            {
                RestoreAll();
                _presetListOpen = false;
            }

            ZonePreset toApply = null;
            for (int i = 0; i < _presetFile.presets.Count; i++)
            {
                ZonePreset p = _presetFile.presets[i];
                if (GUILayout.Button(p.name + "   ·   " + p.saved)) toApply = p;
            }

            GUILayout.EndScrollView();

            if (toApply != null)
            {
                ApplyPreset(toApply);
                _presetListOpen = false;
            }
        }

        GUILayout.BeginHorizontal();
        GUI.SetNextControlName("PresetName");
        _presetName = GUILayout.TextField(_presetName, 40);
        if (GUILayout.Button("Guardar", GUILayout.Width(70f))) SavePreset(_presetName);
        if (GUILayout.Button("Borrar", GUILayout.Width(60f))) DeletePreset(_currentPreset);
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
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
                    if (_cutFaces[f] || !FaceLive(f)) continue;
                    if (paintPart >= 0 && _subGroup[_faceSub[f]] != paintPart) continue;
                    float d = Vector3.Distance(FaceCenter(f), worldPoint);
                    if (d < best) { best = d; bestFace = f; }
                }
                if (bestFace >= 0) SelectFaceUnit(bestFace, false);
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

        Event e = Event.current;

        _panelRect.width = PanelWidth;
        _panelRect.height = Mathf.Min(PanelHeight, Screen.height - 10f);

        Rect titleRect = new Rect(_panelRect.x, _panelRect.y, _panelRect.width, TitleHeight);
        Rect closeRect = new Rect(titleRect.xMax - 28f, titleRect.y + 3f, 24f, TitleHeight - 6f);

        // arrastrar el panel desde la barra de título (excluyendo el botón X)
        if (e.type == EventType.MouseDown && e.button == 0 &&
            titleRect.Contains(e.mousePosition) && !closeRect.Contains(e.mousePosition))
        {
            _draggingPanel = true;
            _dragOffset = e.mousePosition - new Vector2(_panelRect.x, _panelRect.y);
            e.Use();
        }
        else if (_draggingPanel && e.type == EventType.MouseDrag)
        {
            _panelRect.x = Mathf.Clamp(e.mousePosition.x - _dragOffset.x, -_panelRect.width + 80f, Screen.width - 80f);
            _panelRect.y = Mathf.Clamp(e.mousePosition.y - _dragOffset.y, 0f, Screen.height - TitleHeight);
            e.Use();
        }
        else if (e.type == EventType.MouseUp && e.button == 0)
        {
            _draggingPanel = false;
        }

        // clicking outside the panel releases the keyboard focus of the name field
        if (e.type == EventType.MouseDown && !_panelRect.Contains(e.mousePosition))
            GUI.FocusControl(null);

        // fondo sólido
        GUI.color = new Color(0f, 0f, 0f, 0.92f);
        GUI.DrawTexture(_panelRect, Texture2D.whiteTexture);
        GUI.color = new Color(0.18f, 0.18f, 0.22f, 1f);
        GUI.DrawTexture(titleRect, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(titleRect.x + 8f, titleRect.y + 3f, titleRect.width - 40f, TitleHeight - 4f),
            "EDITOR DE ZONAS · " + bodyChildName + "   (arrastra para mover)");

        if (GUI.Button(closeRect, "X"))
        {
            SetEditMode(false);
            return;
        }

        // clicking the panel drops any uGUI keyboard focus (so Enter never "clicks" a button behind)
        if (e.isMouse && _panelRect.Contains(e.mousePosition) && e.type == EventType.MouseDown)
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        Rect bodyRect = new Rect(_panelRect.x, _panelRect.y + TitleHeight, _panelRect.width, _panelRect.height - TitleHeight);
        GUILayout.BeginArea(bodyRect, GUI.skin.box);
        _panelScroll = GUILayout.BeginScrollView(_panelScroll);

        DrawPresetUI();

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
            ? "Clic = pintar · Alt+clic = pieza/isla · Alt+Mayús+clic = capa por altura · Ctrl = quitar de la selección"
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
            alphaDoesNotOcclude = GUILayout.Toggle(alphaDoesNotOcclude, " Atravesar telas alpha (elige su parte para editarlas)");

        GUILayout.Space(4);
        GUILayout.Label("Radio del pincel: " + (brushRadius * 100f).ToString("F1") + " cm");
        brushRadius = GUILayout.HorizontalSlider(brushRadius, 0.01f, 0.30f);

        GUILayout.Label("Engordar borde: " + growRings + " anillo(s) de caras");
        growRings = Mathf.RoundToInt(GUILayout.HorizontalSlider(growRings, 0, 4));

        selectTwins = GUILayout.Toggle(selectTwins, " Seleccionar capa gemela (doble cara / Alpha duplicada)");

        GUILayout.Label("Pieza máx. para Alt+clic: " + autoPieceMax + " caras (mayor = isla UV)");
        autoPieceMax = Mathf.RoundToInt(GUILayout.HorizontalSlider(autoPieceMax, 200, 8000));

        GUILayout.Label("Solape de capa (Alt+Mayús+clic): " + rowOverlap.ToString("F2"));
        rowOverlap = GUILayout.HorizontalSlider(rowOverlap, 0.2f, 0.8f);

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
        lockBody = GUILayout.Toggle(lockBody, " Bloquear cuerpo (solo se puede marcar ropa)");
        GUILayout.Label("Ropa: " + _clothFaces + " caras · Cuerpo: " + _bodyFaces + " caras · huesos con física: " + _springBoneCount);

        float newCloth = GUILayout.HorizontalSlider(clothWeight, 0.1f, 1f);
        if (Mathf.Abs(newCloth - clothWeight) > 0.001f)
        {
            clothWeight = newCloth;
            BuildClothClassification();
            if (_previewMode != 0) SetPreview(_previewMode);
        }
        GUILayout.Label("Peso mínimo en huesos con física: " + clothWeight.ToString("F2"));

        // TEMPFILE-INI1  temporary classification check
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(_previewMode == 0, " Ver todo", GUI.skin.button) && _previewMode != 0) SetPreview(0);
        if (GUILayout.Toggle(_previewMode == 1, " Solo ropa", GUI.skin.button) && _previewMode != 1) SetPreview(1);
        if (GUILayout.Toggle(_previewMode == 2, " Solo cuerpo", GUI.skin.button) && _previewMode != 2) SetPreview(2);
        GUILayout.EndHorizontal();
        // TEMPFILE-FIN2

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

        GUILayout.EndScrollView();
        GUILayout.EndArea();

        // keyboard shortcuts (Enter/Backspace/R/Delete/M) must not fire while typing a name
        _typing = GUI.GetNameOfFocusedControl() == "PresetName";

        ApplyHide();
    }
}
