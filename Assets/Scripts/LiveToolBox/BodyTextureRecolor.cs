// Herramienta local (no forma parte del visor publicado).
// Recolour of the body textures (M_Body: _MainTex = diffuse, _ToonMap = toon) of the loaded character.
// Same workflow as tools/recolor_swimsuit_gui.py: 5 mask zones, each with its own "colour to replace -> new colour"
// rule and thresholds, one rule set for the diffuse and another for the toon (the masks are shared: same UVs).
// Opened from the Editor menu (BodyTextureRecolorMenu) while in Play Mode.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Core = BodyTextureRecolorCore;
using Debug = UnityEngine.Debug;

public partial class BodyTextureRecolor : MonoBehaviour
{
    // Diffuse and toon are recoloured with rules; the triple mask is painted directly (BodyTextureRecolor.Triple.cs).
    const int KindDiffuse = 0, KindToon = 1, KindTriple = 2, KindCount = 3;
    // Fourth editing mode (no texture of its own): one rule set / mask applied to the diffuse and then to the toon.
    const int KindBoth = 3, TabCount = 4;
    static readonly string[] KindNames = { "Diffuse", "Toon", "TripleMask", "Diffuse + Toon" };
    static readonly string[] KindProps = { "_MainTex", "_ToonMap", "_TripleMaskMap" };

    UmaContainerCharacter _chara;
    bool _initialized, _pendingInit, _visible, _pendingTripleOpen;

    // Original textures (as they were when the tool was opened, kept in memory even after Aplicar / Vista previa)
    // and their pixels.
    readonly Texture[] _orig = new Texture[KindCount];
    readonly Core.Source[] _src = new Core.Source[KindCount];
    readonly Texture2D[] _opaque = new Texture2D[KindCount];   // original RGB with alpha 1, for drawing
    readonly Texture2D[] _result = new Texture2D[KindCount];   // last preview (triple: the painted texture)
    bool _resultDirty = true;

    // Rules: [kind][zone].
    readonly Core.Rule[][] _rules = new Core.Rule[TabCount][];   // [KindDiffuse], [KindToon], (unused), [KindBoth]
    int _kind, _zone;
    int _pendingKind = -1;
    bool _bothSeeded;
    bool _toonSeeded;   // the toon rules were copied from the diffuse (done once, the first time the toon is selected)

    // Zone masks (shared by diffuse and toon), size of the diffuse; null = nothing painted.
    byte[][] _layers = new byte[Core.ZoneCount][];
    int _maskW, _maskH;
    readonly bool[] _layerUsed = new bool[Core.ZoneCount];

    // Material instances made by this tool (the costume materials may be shared bundle assets).
    readonly Dictionary<Material, Material> _instances = new Dictionary<Material, Material>();
    readonly HashSet<Material> _ownInstances = new HashSet<Material>();
    readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();

    string _status = "";
    bool _savePrompt;
    string _saveName = "";

    // Windows (IMGUI).
    const int MainWinId = 0x52C0, MaskWinId = 0x52C1, EyeWinId = 0x52C2, PickerWinId = 0x52C3;
    Rect _mainRect = new Rect(20, 40, 680, 800);
    Vector2 _mainScroll;
    bool _mainSized, _mainResizing;
    Vector2 _pendingMainSize;

    // ------------------------------------------------------------------ entry
    /// <summary>Shows the tool for the character this component is on (reads its textures on first use).</summary>
    public void Open()
    {
        _chara = GetComponent<UmaContainerCharacter>();
        _visible = true;
        if (!_initialized) _pendingInit = true;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    void Update()
    {
        if (_pendingInit)
        {
            _pendingInit = false;
            Initialize();
        }
        if (_pendingTripleOpen)
        {
            _pendingTripleOpen = false;
            OpenTripleEditor();
        }
        if (_pendingMaskLoad)
        {
            _pendingMaskLoad = false;
            LoadMasksFromFolder();
        }
    }

    void LateUpdate()
    {
        UpdateBlocker();
    }

    void OnDestroy()
    {
        foreach (var o in _owned) if (o != null) Destroy(o);
        _owned.Clear();
        foreach (var m in _ownInstances) if (m != null) Destroy(m);
        _ownInstances.Clear();
        if (_blockerGo != null) Destroy(_blockerGo);
    }

    // ------------------------------------------------------------------ character textures
    static bool IsBodyRenderer(string n)
    {
        return n == "M_Body" || n.StartsWith("M_Body_", StringComparison.Ordinal);
    }

    List<SkinnedMeshRenderer> BodyRenderers()
    {
        var list = new List<SkinnedMeshRenderer>();
        if (_chara == null) return list;
        foreach (var smr in _chara.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr != null && IsBodyRenderer(smr.name)) list.Add(smr);
        return list;
    }

    Material TransMaterial
    {
        get { return UmaViewerBuilder.Instance != null ? UmaViewerBuilder.Instance.TransMaterialCharas : null; }
    }

    /// <summary>The material of M_Body that holds the textures (the toggle helper's when the slot is hidden).</summary>
    Material FindBodyMaterial()
    {
        var renderers = BodyRenderers();
        renderers.Sort((a, b) => (a.name == "M_Body" ? 0 : 1).CompareTo(b.name == "M_Body" ? 0 : 1));
        foreach (var smr in renderers)
        {
            Material[] mats = smr.sharedMaterials;
            for (int s = 0; s < mats.Length; s++)
            {
                Material m = mats[s];
                if (m == TransMaterial && _chara != null)
                {
                    foreach (var helper in _chara.Materials)
                        if (helper != null && helper.Renderers != null && helper.Renderers.TryGetValue(smr, out var slots) && slots.Contains(s))
                            m = helper.Mat;
                }
                if (m != null && m != TransMaterial && m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null) return m;
            }
        }
        return null;
    }

    void Initialize()
    {
        if (_chara == null) { _status = "No hay personaje."; return; }
        Material m = FindBodyMaterial();
        if (m == null) { _status = "No se encontró ningún material de M_Body con _MainTex."; return; }

        for (int k = 0; k < KindCount; k++)
        {
            Texture tex = m.HasProperty(KindProps[k]) ? m.GetTexture(KindProps[k]) : null;
            _orig[k] = tex;
            _src[k] = null;
            if (tex == null) continue;
            Color32[] px = BodySkinRecolor.ReadPixels(tex, tex.width, tex.height, out bool srgb);
            if (px == null) continue;
            _src[k] = new Core.Source(px, tex.width, tex.height, srgb, k != KindTriple);

            var opaque = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++) { Color32 p = px[i]; p.a = 255; opaque[i] = p; }
            _opaque[k] = NewTexture(tex.width, tex.height, srgb, tex.name + "_view", false);
            _opaque[k].SetPixels32(opaque);
            _opaque[k].Apply(false, false);
        }

        if (_src[KindDiffuse] == null) { _status = "No se pudo leer la diffuse de M_Body."; return; }
        _maskW = _src[KindDiffuse].w;
        _maskH = _src[KindDiffuse].h;

        for (int k = 0; k < TabCount; k++)
        {
            if (k == KindTriple) continue;
            _rules[k] = new Core.Rule[Core.ZoneCount];
            for (int z = 0; z < Core.ZoneCount; z++) _rules[k][z] = new Core.Rule();
        }

        _initialized = true;
        _resultDirty = true;
        _status = "Diffuse " + _orig[0].name + " (" + _maskW + "×" + _maskH + ")" +
                  (_orig[1] != null ? " · Toon " + _orig[1].name + " (" + _orig[1].width + "×" + _orig[1].height + ")" : " · sin toon") +
                  (_orig[2] != null ? " · TripleMask " + _orig[2].name + " (" + _orig[2].width + "×" + _orig[2].height + ")" : " · sin triple mask");
        _loadPrompt = HasSavedRecolorData();
    }

    Texture2D NewTexture(int w, int h, bool srgb, string texName, bool mips)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, mips, !srgb);
        t.name = texName;
        _owned.Add(t);
        return t;
    }

    /// <summary>Recolours both textures with the current rules and masks.</summary>
    void RunPreview()
    {
        if (!_initialized) return;
        var sw = Stopwatch.StartNew();
        int total = 0;
        for (int k = 0; k < 2; k++)
        {
            Core.Source s = _src[k];
            if (s == null) continue;
            Color32[] px = Core.Recolor(s, _rules[_kind == KindBoth ? KindBoth : k], _layers, _maskW, _maskH, out int changed);
            total += changed;
            Texture2D t = _result[k];
            if (t == null || t.width != s.w || t.height != s.h)
            {
                t = NewTexture(s.w, s.h, s.srgb, _orig[k].name + "_recolor", true);
                t.wrapMode = _orig[k].wrapMode;
                t.filterMode = _orig[k].filterMode;
                t.anisoLevel = _orig[k].anisoLevel;
                _result[k] = t;
            }
            t.SetPixels32(px);
            t.Apply(true, false);   // stays readable (needed to save the PNG)
        }
        _resultDirty = false;
        _status = "Vista previa: " + total + " píxeles cambiados (" + sw.ElapsedMilliseconds + " ms).";
    }

    /// <summary>Puts the textures on every M_Body* material of the character (on instances: shared assets are never touched).</summary>
    int ApplyTextures(Texture diff, Texture toon, Texture triple = null)
    {
        var texs = new[] { diff, toon, triple };
        var remap = new Dictionary<Material, Material>();
        int slots = 0;
        Material trans = TransMaterial;

        foreach (var smr in BodyRenderers())
        {
            Material[] mats = smr.sharedMaterials;
            bool any = false;
            for (int s = 0; s < mats.Length; s++)
            {
                Material m = mats[s];
                if (m == null || m == trans) continue;
                if (!HasAnyKindProperty(m)) continue;
                Material inst = InstanceOf(m, remap);
                SetTextures(inst, texs);
                slots++;
                if (inst != m) { mats[s] = inst; any = true; }
            }
            if (any) smr.sharedMaterials = mats;
        }

        // Hidden slots (Model panel toggles) keep their material in the helper: update it too.
        if (_chara != null)
        {
            foreach (var helper in _chara.Materials)
            {
                if (helper == null || helper.Mat == null || helper.Renderers == null) continue;
                bool body = false;
                foreach (var kv in helper.Renderers)
                    if (kv.Key != null && IsBodyRenderer(kv.Key.name)) { body = true; break; }
                if (!body) continue;
                if (!HasAnyKindProperty(helper.Mat)) continue;
                Material inst = InstanceOf(helper.Mat, remap);
                SetTextures(inst, texs);
                helper.Mat = inst;
            }
            if (remap.Count > 0) BodySkinRecolor.RemapShaderEffectTargets(_chara, remap);
        }
        return slots;
    }

    Material InstanceOf(Material m, Dictionary<Material, Material> remap)
    {
        if (_ownInstances.Contains(m)) return m;
        if (!_instances.TryGetValue(m, out Material inst))
        {
            inst = new Material(m) { name = m.name };
            _instances[m] = inst;
            _ownInstances.Add(inst);
        }
        remap[m] = inst;
        return inst;
    }

    static bool HasAnyKindProperty(Material m)
    {
        foreach (string prop in KindProps) if (m.HasProperty(prop)) return true;
        return false;
    }

    /// <summary>Sets the non-null textures (index = kind) on the material.</summary>
    static void SetTextures(Material m, Texture[] texs)
    {
        for (int k = 0; k < KindCount; k++)
            if (texs[k] != null && m.HasProperty(KindProps[k])) m.SetTexture(KindProps[k], texs[k]);
    }

    void ApplyResults()
    {
        if (_resultDirty) RunPreview();
        int slots = ApplyTextures(_result[KindDiffuse], _result[KindToon], _tripleEdited ? _result[KindTriple] : null);
        _status += "  Aplicado a " + slots + " material(es) de M_Body.";
    }

    /// <summary>All maps back to the originals kept in memory (rules and masks are kept; triple mask edits are dropped).</summary>
    void RestoreOriginal()
    {
        int slots = ApplyTextures(_orig[KindDiffuse], _orig[KindToon], _orig[KindTriple]);
        DropResult(KindDiffuse);
        DropResult(KindToon);
        _resultDirty = true;
        DiscardTripleEdits();
        _status = "Todas las texturas originales restauradas (" + slots + " material(es)).";
    }

    void DropResult(int k)
    {
        if (_result[k] == null) return;
        _owned.Remove(_result[k]);
        Destroy(_result[k]);
        _result[k] = null;
    }

    /// <summary>
    /// The selected map back to its original (kept in memory, also after Aplicar / Vista previa): the character
    /// gets the original texture and the preview shows it. Diffuse/toon keep their rules (Aplicar recolours them
    /// again); the triple mask drops its painted edits.
    /// </summary>
    void RestoreSelected()
    {
        if (!HasKind(_kind)) return;
        var texs = new Texture[KindCount];
        if (_kind == KindBoth) { texs[KindDiffuse] = _orig[KindDiffuse]; texs[KindToon] = _orig[KindToon]; }
        else texs[_kind] = _orig[_kind];
        int slots = ApplyTextures(texs[0], texs[1], texs[2]);
        if (_kind == KindTriple) DiscardTripleEdits();
        else
        {
            if (_kind == KindBoth) { DropResult(KindDiffuse); DropResult(KindToon); }
            else DropResult(_kind);
            _resultDirty = true;
        }
        _status = KindNames[_kind] + " original restaurada (" + slots + " material(es)).";
    }

    /// <summary>The kind can be edited (its textures exist); "Diffuse + Toon" needs both.</summary>
    bool HasKind(int k)
    {
        if (k == KindBoth) return _src[KindDiffuse] != null && _src[KindToon] != null;
        return k >= 0 && k < KindCount && _src[k] != null;
    }

    void SaveModel()
    {
        string modelName = (_saveName ?? "").Trim();
        if (modelName.Length == 0) { _status = "Escribe un nombre para el modelo."; return; }
        if (_chara == null || _chara.CharaEntry == null || string.IsNullOrEmpty(_chara.VarCostumeIdLong))
        {
            _status = "No se sabe qué personaje/traje es: no se puede guardar.";
            return;
        }

        int charaId = _chara.CharaEntry.Id;
        string baseCostume = _chara.VarCostumeIdLong;
        if (CustomModelRegistry.NameTakenByOtherCostume(charaId, modelName, baseCostume))
        {
            _status = "Ya hay un modelo \"" + modelName + "\" de otro traje de este personaje: usa otro nombre.";
            return;
        }

        // A custom model without textures keeps its original colours: the recolor is always a new model.
        string loaded = _chara.CustomModelName;
        if (!string.IsNullOrEmpty(loaded) && string.Equals(loaded, modelName, StringComparison.OrdinalIgnoreCase) &&
            !CustomModelRegistry.HasTextures(charaId, loaded))
        {
            _status = "El custom \"" + loaded + "\" conserva sus colores originales: guarda el recolor con otro nombre.";
            return;
        }

        try
        {
            ApplyResults();
            byte[] diff = _result[KindDiffuse] != null ? _result[KindDiffuse].EncodeToPNG() : null;
            byte[] toon = _result[KindToon] != null ? _result[KindToon].EncodeToPNG() : null;
            byte[] triple = _tripleEdited && _result[KindTriple] != null ? _result[KindTriple].EncodeToPNG() : null;
            CustomModelRegistry.SaveTextured(charaId, baseCostume, modelName, _chara.CustomModelName, diff, toon, triple);
            SaveRecolorData(charaId, modelName);
            _savePrompt = false;
            _status = "Modelo guardado: CustomModels/" + charaId + "/" + CustomModelRegistry.FolderName(modelName) +
                      " (" + CustomModelRegistry.IdOf(modelName) + ")" +
                      (string.IsNullOrEmpty(_chara.CustomModelName) ? "" : " · cortes de \"" + _chara.CustomModelName + "\" conservados");
            Debug.Log("[BodyTextureRecolor] " + _status);
        }
        catch (Exception ex)
        {
            _status = "Error al guardar: " + ex.Message;
            Debug.LogError("[BodyTextureRecolor] " + ex);
        }
    }

    /// <summary>
    /// Switches the texture whose rules are edited. The first time the toon is opened it starts as a copy of the
    /// diffuse rules (colours, modes and thresholds); the masks are shared by both textures already.
    /// </summary>
    void SelectKind(int k)
    {
        if (k == KindTriple)
        {
            if (_kind == KindBoth) _resultDirty = true;   // the individual rules will be used again
            _kind = k;
            _pendingTripleOpen = true;   // opened from Update: a window opened in the middle of a Layout event has no layout at Repaint
            return;
        }
        if (k == KindBoth && !_bothSeeded)
        {
            for (int z = 0; z < Core.ZoneCount; z++) _rules[KindBoth][z].CopyFrom(_rules[KindDiffuse][z]);
            _bothSeeded = true;
            _status = "Diffuse + Toon: copiados los colores, modos y ajustes de la diffuse (sólo esta primera vez).";
        }
        if ((k == KindBoth) != (_kind == KindBoth)) _resultDirty = true;   // another rule set will be used by the preview
        if (k == KindToon && !_toonSeeded)
        {
            for (int z = 0; z < Core.ZoneCount; z++) _rules[KindToon][z].CopyFrom(_rules[KindDiffuse][z]);
            _toonSeeded = true;
            _resultDirty = true;
            _status = "Toon: copiados los colores, modos y ajustes de la diffuse (sólo esta primera vez).";
        }
        _kind = k;
    }

    void RefreshLayerUsed()
    {
        for (int z = 0; z < Core.ZoneCount; z++) _layerUsed[z] = Core.LayerUsed(_layers[z]);
    }

    // ------------------------------------------------------------------ IMGUI
    void OnGUI()
    {
        if (!_visible) return;
        GUI.depth = -100;

        // Eyedropper and colour picker are modal windows (GUI.ModalWindow): the others ignore input meanwhile.
        if (!_maskOpen && !_tripleOpen)
        {
            // Tall by default (almost the whole screen); resizable from the bottom-right corner.
            if (!_mainSized)
            {
                _mainRect.height = Mathf.Max(400f, Screen.height - _mainRect.y - 20f);
                _pendingMainSize = _mainRect.size;
                _mainSized = true;
            }
            if (_pendingMainSize.x > 0f) { _mainRect.width = _pendingMainSize.x; _mainRect.height = _pendingMainSize.y; }
            _mainRect = GUI.Window(MainWinId, _mainRect, DrawMainWindow, "RECOLOR DE M_Body (diffuse / toon)", WindowStyle);
        }

        if (_maskOpen) DrawMaskEditorWindow();
        if (_tripleOpen) DrawTripleEditorWindow();
        if (_viewOpen) DrawViewerWindow();
        if (_loadPrompt) DrawLoadPromptWindow();
        if (_eyeOpen) DrawEyedropperWindow();
        if (_pickerOpen) DrawPickerWindow();
    }

    void DrawMainWindow(int id)
    {
        Event e = Event.current;
        if (e.type == EventType.MouseDown && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        Rect close = new Rect(_mainRect.width - 26, 2, 22, 16);
        if (GUI.Button(close, "X")) { _visible = false; _savePrompt = false; return; }

        if (!_initialized)
        {
            GUILayout.Label(_pendingInit ? "Leyendo texturas..." : _status);
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
            return;
        }

        GUILayout.BeginArea(new Rect(4, 20, _mainRect.width - 8, _mainRect.height - 40));
        _mainScroll = GUILayout.BeginScrollView(_mainScroll);

        string chara = _chara != null && _chara.CharaEntry != null ? _chara.CharaEntry.Id + " · " + _chara.VarCostumeIdLong : name;
        if (_chara != null && !string.IsNullOrEmpty(_chara.CustomModelName)) chara += " · custom \"" + _chara.CustomModelName + "\"";
        GUILayout.Label("Personaje: " + chara);

        // (kind switches are applied on the next Layout event: the layout changes with the kind)
        if (Event.current.type == EventType.Layout && _pendingKind >= 0)
        {
            int pk = _pendingKind;
            _pendingKind = -1;
            SelectKind(pk);
        }

        // ---- texture being edited
        GUILayout.BeginHorizontal();
        GUILayout.Label("Editando:", GUILayout.Width(70));
        for (int k = 0; k < TabCount; k++)
        {
            GUI.enabled = HasKind(k) && !(_eyeOpen || _pickerOpen || _viewOpen);
            // Only a real click changes the selection (also on the selected triple mask: it reopens its editor).
            bool selected = _kind == k;
            if (GUILayout.Toggle(selected, " " + KindNames[k], GUI.skin.button, GUILayout.Width(100)) != selected)
                _pendingKind = k;
        }
        GUI.enabled = !(_eyeOpen || _pickerOpen);
        GUILayout.EndHorizontal();

        // ---- previews (all maps)
        const float pv = 205f;
        GUILayout.BeginHorizontal();
        for (int k = 0; k < KindCount; k++)
        {
            GUILayout.BeginVertical(GUILayout.Width(pv));
            string state = k == KindTriple
                ? (_tripleEdited ? " (editada)" : " (original)")
                : (_result[k] != null && !_resultDirty ? " (vista previa)" : _result[k] != null ? " (desactualizada)" : " (original)");
            GUILayout.Label(KindNames[k] + state);
            Rect r = GUILayoutUtility.GetRect(pv, pv, GUILayout.Width(pv), GUILayout.Height(pv));
            GUI.color = new Color(0.25f, 0.25f, 0.25f, 1f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = k == _kind || (_kind == KindBoth && k != KindTriple) ? new Color(1f, 0.55f, 0f) : new Color(0.1f, 0.1f, 0.1f);
            DrawFrame(r, 2);
            GUI.color = Color.white;
            Texture show = PreviewTexture(k);
            if (show != null)
            {
                GUI.DrawTexture(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), show, ScaleMode.ScaleToFit, false);
                if (GUI.Button(r, new GUIContent(GUIContent.none) { tooltip = "Clic: ampliar" }, GUIStyle.none)) OpenViewer(k);
            }
            else GUI.Label(r, "  (no hay " + KindNames[k] + ")");
            GUILayout.EndVertical();
        }
        GUILayout.EndHorizontal();

        if (_kind == KindTriple)
        {
            // ---- triple mask: painted directly, no rules
            GUILayout.Space(6);
            GUILayout.Label("TripleMask (" + (_orig[KindTriple] != null ? _orig[KindTriple].name : "-") + "): se edita pintando directamente sobre la textura.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Abrir editor de TripleMask...", GUILayout.Width(220), GUILayout.Height(26))) _pendingTripleOpen = true;
            GUILayout.Label(_tripleEdited ? "Hay cambios pintados (Aplicar los pone en el personaje)." : "Sin cambios.");
            GUILayout.EndHorizontal();
        }
        else
        {
        // ---- mask
        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Máscara:", GUILayout.Width(70));
        bool anyLayer = Array.IndexOf(_layerUsed, true) >= 0;
        if (GUILayout.Button(anyLayer ? "Editar..." : "Crear...", GUILayout.Width(90))) OpenMaskEditor();
        GUI.enabled = anyLayer && !(_eyeOpen || _pickerOpen);
        if (GUILayout.Button("Borrar", GUILayout.Width(70)))
        {
            _layers = new byte[Core.ZoneCount][];
            RefreshLayerUsed();
            _resultDirty = true;
        }
        GUI.enabled = !(_eyeOpen || _pickerOpen);
        string used = "";
        for (int z = 0; z < Core.ZoneCount; z++) if (_layerUsed[z]) used += (used.Length > 0 ? ", " : "") + Core.ZoneNames[z];
        GUILayout.Label(anyLayer ? "Zonas en uso: " + used : "Sin máscara: se aplica la columna negra a toda la textura.");
        GUILayout.EndHorizontal();

        // ---- colours grid (rules of the edited texture)
        GUILayout.Space(6);
        GUILayout.Label("Colores — " + KindNames[_kind]);
        Core.Rule[] rules = _rules[_kind];
        const float labelW = 140f, colW = 88f;

        GUILayout.BeginHorizontal();
        GUILayout.Label("Máscara a sustituir", GUILayout.Width(labelW));
        for (int z = 0; z < Core.ZoneCount; z++)
        {
            Rect r = GUILayoutUtility.GetRect(colW, 34, GUILayout.Width(colW), GUILayout.Height(34));
            if (ColorBox(new Rect(r.x + 8, r.y + 2, colW - 16, 30), Core.ZoneColors[z], z == _zone, 3)) _zone = z;
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Color a sustituir", GUILayout.Width(labelW));
        for (int z = 0; z < Core.ZoneCount; z++)
        {
            Rect r = GUILayoutUtility.GetRect(colW, 34, GUILayout.Width(colW), GUILayout.Height(34));
            if (ColorBox(new Rect(r.x + 16, r.y + 2, colW - 32, 30), rules[z].src, false, 1))
            {
                _zone = z;
                OpenEyedropper(_kind, z);
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Color nuevo", GUILayout.Width(labelW));
        for (int z = 0; z < Core.ZoneCount; z++)
        {
            Rect r = GUILayoutUtility.GetRect(colW, 34, GUILayout.Width(colW), GUILayout.Height(34));
            Rect nb = new Rect(r.x + 16, r.y + 2, colW - 32, 30);
            if (ColorBox(nb, rules[z].dst, false, 1))
            {
                _zone = z;
                OpenPicker(_kind, z);
            }
            else if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && nb.Contains(Event.current.mousePosition))
            {
                // right click: eyedropper on the texture (same as "Color a sustituir") that sets the new colour
                _zone = z;
                OpenEyedropper(_kind, z, true);
                Event.current.Use();
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Modo", GUILayout.Width(labelW));
        for (int z = 0; z < Core.ZoneCount; z++)
        {
            if (GUILayout.Button(rules[z].full ? "Completo" : "Tono", GUILayout.Width(colW - 6)))
            {
                rules[z].full = !rules[z].full;
                _zone = z;
                _resultDirty = true;
            }
        }
        GUILayout.EndHorizontal();
        GUILayout.Label("Color a sustituir: cuentagotas sobre la textura · Color nuevo: clic izquierdo selector de color, clic derecho cuentagotas.\n" +
                        "Tono: cambia tono y saturación, conserva el brillo. Completo: también el brillo (blanco, gris, negro).");

        // ---- thresholds of the active zone
        GUILayout.Space(6);
        Core.Rule rule = rules[_zone];
        GUILayout.Label("Qué píxeles cambian — " + KindNames[_kind] + " · Zona " + Core.ZoneNames[_zone]);
        bool before = GUI.enabled;
        float v;
        GUI.enabled = before && !rule.full;
        v = SliderRow("Tolerancia de tono (°)", rule.tolDeg, 0f, 180f, "F0");
        if (v != rule.tolDeg) { rule.tolDeg = v; _resultDirty = true; }
        v = SliderRow("Saturación mínima (0-1)", rule.minSat, 0f, 1f, "F2");
        if (v != rule.minSat) { rule.minSat = v; _resultDirty = true; }
        GUI.enabled = before;
        v = SliderRow("Brillo mínimo (0-1)", rule.minVal, 0f, 1f, "F2");
        if (v != rule.minVal) { rule.minVal = v; _resultDirty = true; }
        GUI.enabled = before && rule.full;
        v = SliderRow("Tolerancia de color (RGB)", rule.rgbTol, 0f, 442f, "F0");
        if (v != rule.rgbTol) { rule.rgbTol = v; _resultDirty = true; }
        v = SliderRow("Sombreado conservado (negros)", rule.shade, 0f, 1f, "F2");
        if (v != rule.shade) { rule.shade = v; _resultDirty = true; }
        GUI.enabled = before;
        if (GUILayout.Button("Aplicar a todas las zonas", GUILayout.Width(200)))
        {
            for (int z = 0; z < Core.ZoneCount; z++) if (z != _zone) rules[z].CopyThresholdsFrom(rule);
            _resultDirty = true;
            _status = "Ajustes de la zona " + Core.ZoneNames[_zone] + " copiados a todas las zonas (" + KindNames[_kind] + ").";
        }
        }

        // ---- actions
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Vista previa", GUILayout.Height(26))) RunPreview();
        if (GUILayout.Button("Aplicar", GUILayout.Height(26))) ApplyResults();
        if (GUILayout.Button("Guardar...", GUILayout.Height(26)))
        {
            _savePrompt = true;
            if (string.IsNullOrEmpty(_saveName) && _chara != null && !string.IsNullOrEmpty(_chara.CustomModelName))
                _saveName = _chara.CustomModelName + "_recolor";
        }
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUI.enabled = HasKind(_kind) && !(_eyeOpen || _pickerOpen);
        if (GUILayout.Button("Restaurar " + KindNames[_kind], GUILayout.Height(24))) RestoreSelected();
        GUI.enabled = !(_eyeOpen || _pickerOpen);
        if (GUILayout.Button("Restaurar todo", GUILayout.Height(24))) RestoreOriginal();
        GUILayout.EndHorizontal();

        if (_savePrompt) DrawSavePrompt();

        GUILayout.Space(4);
        GUILayout.Label(_status);

        GUILayout.EndScrollView();
        GUILayout.EndArea();
        _pendingMainSize = ResizeGrip(_mainRect.size, new Vector2(660, 400), ref _mainResizing);
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    void DrawSavePrompt()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("Guardar como modelo custom (se aplica al personaje y aparece como traje \"cus_<nombre>\"):");
        GUILayout.BeginHorizontal();
        GUILayout.Label("Nombre:", GUILayout.Width(60));
        GUI.SetNextControlName("BodyTextureRecolorSaveName");
        _saveName = GUILayout.TextField(_saveName ?? "", 48, GUILayout.Width(260));
        if (GUILayout.Button("Guardar", GUILayout.Width(80))) SaveModel();
        if (GUILayout.Button("Cancelar", GUILayout.Width(80))) _savePrompt = false;
        GUILayout.EndHorizontal();

        string n = (_saveName ?? "").Trim();
        if (n.Length > 0 && _chara != null && _chara.CharaEntry != null && !string.IsNullOrEmpty(_chara.VarCostumeIdLong) &&
            CustomModelRegistry.Exists(_chara.CharaEntry.Id, n, _chara.VarCostumeIdLong))
            GUILayout.Label(n == _chara.CustomModelName
                ? "Mismo nombre que el modelo cargado: se actualiza ese modelo (conserva sus cortes y lo que no edites)."
                : "AVISO: ya existe un modelo con ese nombre para este traje: se sobrescribirá.");
        if (_chara != null && !string.IsNullOrEmpty(_chara.CustomModelName))
        {
            GUILayout.Label("El traje cargado es el custom \"" + _chara.CustomModelName + "\": el nuevo conserva su malla (cortes y bordes extendidos) y no lo modifica.");
            if (string.Equals(n, _chara.CustomModelName, StringComparison.OrdinalIgnoreCase) && _chara.CharaEntry != null &&
                !CustomModelRegistry.HasTextures(_chara.CharaEntry.Id, _chara.CustomModelName))
                GUILayout.Label("Usa otro nombre: ese custom conserva sus colores originales.");
        }
        GUILayout.EndVertical();
    }

    float SliderRow(string label, float value, float min, float max, string fmt)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(190));
        float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(300));
        GUILayout.Label(v.ToString(fmt), GUILayout.Width(50));
        GUILayout.EndHorizontal();
        if (fmt == "F0") v = Mathf.Round(v);
        else if (fmt == "F2") v = Mathf.Round(v * 100f) / 100f;
        return v;
    }

    // ------------------------------------------------------------------ opaque window style
    static readonly Color WindowBg = new Color32(0x57, 0x5C, 0x63, 255);
    static GUIStyle _winStyle;
    static Texture2D _winBgTex;

    /// <summary>GUI.skin.window with an opaque grey background (the default one is translucent). Call inside OnGUI.</summary>
    static GUIStyle WindowStyle
    {
        get
        {
            if (_winStyle == null || _winBgTex == null)
            {
                _winBgTex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "RecolorWindowBg", hideFlags = HideFlags.DontSave };
                _winBgTex.SetPixel(0, 0, WindowBg);
                _winBgTex.Apply(false);
                _winStyle = new GUIStyle(GUI.skin.window);
                _winStyle.normal.background = _winBgTex;
                _winStyle.onNormal.background = _winBgTex;
                _winStyle.focused.background = _winBgTex;
                _winStyle.onFocused.background = _winBgTex;
                _winStyle.border = new RectOffset(0, 0, 0, 0);
                _winStyle.normal.textColor = _winStyle.onNormal.textColor = Color.white;
                _winStyle.fontStyle = FontStyle.Bold;
            }
            return _winStyle;
        }
    }

    // ------------------------------------------------------------------ small drawing helpers
    static void DrawFrame(Rect r, float t)
    {
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), Texture2D.whiteTexture);
    }

    /// <summary>Colour box with a frame (orange when selected); returns true when clicked.</summary>
    static bool ColorBox(Rect r, Color32 c, bool selected, float frame, string text = null)
    {
        Color prev = GUI.color;
        GUI.color = selected ? new Color(1f, 0.55f, 0f) : new Color(0.05f, 0.05f, 0.05f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = c;
        GUI.DrawTexture(new Rect(r.x + frame, r.y + frame, r.width - 2 * frame, r.height - 2 * frame), Texture2D.whiteTexture);
        GUI.color = prev;
        if (!string.IsNullOrEmpty(text))
        {
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            style.normal.textColor = lum > 140 ? Color.black : Color.white;
            GUI.Label(r, text, style);
        }
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    // ------------------------------------------------------------------ uGUI blocker (so the viewer UI / camera ignore clicks on the windows)
    GameObject _blockerGo;
    readonly List<RectTransform> _blockers = new List<RectTransform>();

    void UpdateBlocker()
    {
        var rects = new List<Rect>();
        if (_visible)
        {
            if (!_maskOpen && !_tripleOpen) rects.Add(_mainRect);
            if (_maskOpen) rects.Add(_maskRect);
            if (_tripleOpen) rects.Add(_tripleRect);
            if (_loadPrompt) rects.Add(LoadPromptRect());
            if (_viewOpen) rects.Add(_viewRect);
            if (_eyeOpen) rects.Add(_eyeRect);
            if (_pickerOpen) rects.Add(_pickerRect);
        }

        if (rects.Count > 0 && _blockerGo == null)
        {
            _blockerGo = new GameObject("BodyTextureRecolorUIBlocker");
            _blockerGo.hideFlags = HideFlags.DontSave;
            Canvas canvas = _blockerGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            _blockerGo.AddComponent<GraphicRaycaster>();
        }
        if (_blockerGo == null) return;
        _blockerGo.SetActive(rects.Count > 0);

        while (_blockers.Count < rects.Count)
        {
            var go = new GameObject("Blocker");
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(_blockerGo.transform, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0);
            img.raycastTarget = true;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            _blockers.Add(rt);
        }
        for (int i = 0; i < _blockers.Count; i++)
        {
            bool on = i < rects.Count;
            _blockers[i].gameObject.SetActive(on);
            if (!on) continue;
            Rect r = rects[i];
            // IMGUI origin is top-left; uGUI overlay is bottom-left.
            _blockers[i].anchoredPosition = new Vector2(r.x, Screen.height - r.yMax);
            _blockers[i].sizeDelta = new Vector2(r.width, r.height);
        }
    }
}
