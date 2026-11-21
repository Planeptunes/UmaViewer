// Herramienta local (no forma parte del visor publicado).
// BodyTextureRecolor: the recolor settings (every rule of every mode, the zone masks) are saved with the custom model
// (recolor.json + recolor_mask_<zone>.png in its folder). When the tool opens on a model that has them, a popup
// asks whether to load them.
using System;
using System.IO;
using UnityEngine;
using Core = BodyTextureRecolorCore;

public partial class BodyTextureRecolor
{
    const string DataFile = "recolor.json";
    const int LoadWinId = 0x52C6;
    bool _loadPrompt;

    [Serializable] class RuleSetData { public Core.Rule[] zones = new Core.Rule[0]; }

    [Serializable]
    class RecolorData
    {
        public int version = 1;
        public int maskW, maskH;
        public bool toonSeeded, bothSeeded;
        public RuleSetData[] sets;   // indexed by kind: diffuse, toon, (triple: empty), diffuse + toon
    }

    string DataDir()
    {
        if (_chara == null || _chara.CharaEntry == null || string.IsNullOrEmpty(_chara.CustomModelName)) return null;
        return CustomModelRegistry.ModelFolder(_chara.CharaEntry.Id, _chara.CustomModelName);
    }

    bool HasSavedRecolorData()
    {
        string dir = DataDir();
        return dir != null && File.Exists(Path.Combine(dir, DataFile));
    }

    static string MaskFile(string dir, int zone)
    {
        return Path.Combine(dir, "recolor_mask_" + zone + ".png");
    }

    void SaveRecolorData(int charaId, string modelName)
    {
        string dir = CustomModelRegistry.ModelFolder(charaId, modelName);
        var data = new RecolorData
        {
            maskW = _maskW, maskH = _maskH,
            toonSeeded = _toonSeeded, bothSeeded = _bothSeeded,
            sets = new RuleSetData[TabCount]
        };
        for (int k = 0; k < TabCount; k++)
            data.sets[k] = new RuleSetData { zones = _rules[k] ?? new Core.Rule[0] };
        File.WriteAllText(Path.Combine(dir, DataFile), JsonUtility.ToJson(data, true));

        for (int z = 0; z < Core.ZoneCount; z++)
        {
            string file = MaskFile(dir, z);
            if (File.Exists(file)) File.Delete(file);   // never keep a zone that was cleared
            if (!_layerUsed[z]) continue;
            var px = new Color32[_maskW * _maskH];
            byte[] layer = _layers[z];
            for (int i = 0; i < px.Length; i++) { byte v = layer[i]; px[i] = new Color32(v, v, v, 255); }
            var t = new Texture2D(_maskW, _maskH, TextureFormat.RGBA32, false, true);
            t.SetPixels32(px);
            File.WriteAllBytes(file, t.EncodeToPNG());
            Destroy(t);
        }
    }

    /// <summary>Loads rules and masks saved with the model (the textures themselves are the ones already on the character).</summary>
    void LoadRecolorData()
    {
        string dir = DataDir();
        if (dir == null) return;
        try
        {
            RecolorData data = JsonUtility.FromJson<RecolorData>(File.ReadAllText(Path.Combine(dir, DataFile)));
            for (int k = 0; k < TabCount && data.sets != null && k < data.sets.Length; k++)
            {
                if (_rules[k] == null || data.sets[k].zones == null) continue;
                for (int z = 0; z < Core.ZoneCount && z < data.sets[k].zones.Length; z++)
                    if (data.sets[k].zones[z] != null) _rules[k][z].CopyFrom(data.sets[k].zones[z]);
            }
            _toonSeeded = data.toonSeeded;
            _bothSeeded = data.bothSeeded;

            int masks = 0;
            bool sizeOk = data.maskW == _maskW && data.maskH == _maskH;
            _layers = new byte[Core.ZoneCount][];
            for (int z = 0; z < Core.ZoneCount && sizeOk; z++)
            {
                string file = MaskFile(dir, z);
                if (!File.Exists(file)) continue;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                if (t.LoadImage(File.ReadAllBytes(file)) && t.width == _maskW && t.height == _maskH)
                {
                    Color32[] px = t.GetPixels32();
                    var layer = new byte[px.Length];
                    for (int i = 0; i < px.Length; i++) layer[i] = px[i].r > 127 ? (byte)255 : (byte)0;
                    _layers[z] = layer;
                    masks++;
                }
                Destroy(t);
            }
            RefreshLayerUsed();
            _resultDirty = true;
            _status = "Datos del recolor cargados (" + masks + " máscara(s))." + (sizeOk ? "" : " La máscara guardada tiene otro tamaño: no se cargó.");
        }
        catch (Exception ex)
        {
            _status = "No se pudieron cargar los datos del recolor: " + ex.Message;
            Debug.LogError("[BodyTextureRecolor] " + ex);
        }
    }

    // ------------------------------------------------------------------ "Cargar Mask" (mask editor)
    bool _pendingMaskLoad;

    /// <summary>
    /// Asks for a folder and loads every recolor_mask_&lt;zone&gt;.png found there into the mask editor canvases
    /// (0 = Negro, 1 = Rojo, ...). Undoable; the editor still needs Aceptar. Called from Update (modal dialog).
    /// </summary>
    void LoadMasksFromFolder()
    {
        if (!_maskOpen) return;
        string start = DataDir();
        if (start == null || !Directory.Exists(start))
            start = Path.Combine(Application.dataPath, "Resources", "CustomModels");
        string dir = PickFolder("Carpeta con recolor_mask_<zona>.png", start);
        if (string.IsNullOrEmpty(dir)) return;

        int loaded = 0;
        string resized = "";
        for (int z = 0; z < Core.ZoneCount; z++)
        {
            string file = MaskFile(dir, z);
            if (!File.Exists(file)) continue;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!t.LoadImage(File.ReadAllBytes(file))) continue;
                Color32[] px = t.GetPixels32();
                int tw = t.width, th = t.height;
                bool same = tw == _maskW && th == _maskH;
                if (!same) resized += (resized.Length > 0 ? ", " : "") + Core.ZoneNames[z] + " (" + tw + "×" + th + ")";
                var layer = new byte[_maskW * _maskH];
                for (int y = 0; y < _maskH; y++)
                {
                    int sy = same ? y : Mathf.Min(th - 1, (int)((long)y * th / _maskH));
                    for (int x = 0; x < _maskW; x++)
                    {
                        int sx = same ? x : Mathf.Min(tw - 1, (int)((long)x * tw / _maskW));
                        layer[y * _maskW + x] = px[sy * tw + sx].r > 127 ? (byte)255 : (byte)0;
                    }
                }
                PushUndo(z);
                _edit[z] = layer;
                _editUsed[z] = Core.LayerUsed(layer);
                loaded++;
            }
            finally { Destroy(t); }
        }

        if (loaded > 0)
        {
            _editDirty = true;
            _maskCancelArmed = false;
            _maskDispZone = -1;
            _dispFull = true;
        }
        _status = loaded > 0
            ? "Máscaras cargadas de " + dir + ": " + loaded + " zona(s)." + (resized.Length > 0 ? " Reescaladas: " + resized + "." : "") + " Pulsa Aceptar en el editor."
            : "No hay ningún recolor_mask_<zona>.png en " + dir + ".";
        Debug.Log("[BodyTextureRecolor] " + _status);
    }

    static string PickFolder(string title, string start)
    {
#if UNITY_EDITOR
        return UnityEditor.EditorUtility.OpenFolderPanel(title, start, "");
#elif UNITY_STANDALONE
        string[] paths = SFB.StandaloneFileBrowser.OpenFolderPanel(title, start, false);
        return paths != null && paths.Length > 0 ? paths[0] : null;
#else
        return null;
#endif
    }

    Rect LoadPromptRect()
    {
        const float w = 440f, h = 150f;
        return new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
    }

    void DrawLoadPromptWindow()
    {
        GUI.ModalWindow(LoadWinId, LoadPromptRect(), DrawLoadPrompt, "Recolor guardado", WindowStyle);
    }

    void DrawLoadPrompt(int id)
    {
        GUILayout.BeginArea(new Rect(14, 28, 412, 110));
        GUILayout.Label("El modelo \"" + (_chara != null ? _chara.CustomModelName : "") + "\" tiene datos de un recolor anterior " +
                        "(máscaras, colores a sustituir y nuevos, modos y ajustes).\n¿Desea cargar los datos previos?");
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Sí, cargar", GUILayout.Width(110), GUILayout.Height(26))) { _loadPrompt = false; LoadRecolorData(); }
        if (GUILayout.Button("No", GUILayout.Width(90), GUILayout.Height(26))) _loadPrompt = false;
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }
}
