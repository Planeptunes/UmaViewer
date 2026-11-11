// ============================================================================================
// TEMPORAL — SOLO PARA PROBAR. BORRAR ESTE FICHERO Y SU .meta AL REVERTIR.
//
// Genera los iconos de los trajes que hoy usan el icono por defecto, uno a uno:
//   1) crea la lista de trajes sin icono -> <ROOT>/docs/missing_icons.txt,
//   2) por cada traje: carga el modelo (camino directo) -> espera 1 s -> foto,
//   3) compone la foto sobre Resources/Designer.png y guarda el .png final en
//      <ROOT>/Assets/Resources/CostumeIcons/<clave>.png,
//   4) borra el personaje y pasa al siguiente, hasta agotar la lista.
//
// Clave del fichero: comun -> <costumeId> (p. ej. 0001_00_01); propio -> <charaId>_<costumeId>
// (p. ej. 1013_0055). La app los usa via CustomCostumeIcons (Resources/CostumeIcons).
//
// Ubicacion de los PNG (Resources/CostumeIcons):
//   - trajes comunes/extras (XXXX_XX_XX):  CostumeIcons/<costumeId>.png
//   - trajes propios de un personaje:      CostumeIcons/<charaId>/<charaId>_<costumeId>.png
//   - cabezas (TODAS, tengan o no icono del juego): CostumeIcons/<charaId>/<charaId>_<headId>_head.png
// Una cabeza se fotografia con zoom/encuadre a la cabeza y con el mismo marco (Designer.png).
// Los PNG ya existentes se saltan: para regenerar uno, borra su PNG.
//
// El boton "Canvas/UI/Panels/TemporalICON" dispara el lote. No toca ningun otro fichero.
// Fichero LOCAL: excluido de git (.git/info/exclude).
// ============================================================================================
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public class TemporalIcon : MonoBehaviour
{
    private const string ButtonPath = "Canvas/UI/Panels/Settings/Viewport/Content/Other/CreateMissingCodes";
    private const string ButtonName = "CreateMissingCodes";
    private const string BodyMeshName = "M_Body";
    private const int TempLayer = 30;
    private const int IconSize = 512;
    private const float CaptureRenderScale = 2.0f;  // URP renderScale durante la captura (200%)
    private const int CaptureMsaa = 8;              // MSAA maximo durante la captura
    private const float Padding = 1.10f;
    private const float Zoom = 1.90f;      // divisor del encuadre (calibrado en 1013)
    private const float UpShift = 0.175f;  // sube el modelo un 17.5% (baja la camara sin girarla)
    private const float HeadZoom = 1.5758f;     // semi-alto del encuadre = semi-alto de la CARA * HeadZoom (el pelo infla sus bounds)
    private const float HeadTopMargin = 0.10f; // hueco entre lo mas alto del pj (orejas/pelo) y el borde superior del icono (fraccion del alto)
    // Prueba de encuadre: maximo de elementos NUEVOS a generar por tipo (trajes / cabezas). 0 = sin limite.
    private const int BatchLimit = 0;

    private Button _button;
    private bool _running;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("[TemporalIcon]");
        DontDestroyOnLoad(go);
        go.AddComponent<TemporalIcon>();
    }

    private void Update()
    {
        if (_button != null) return;

        var row = GameObject.Find(ButtonPath);
        if (row != null) _button = row.GetComponentInChildren<Button>(true);
        if (_button == null) _button = FindTemporalButton();
        if (_button == null) return;

        _button.onClick = new Button.ButtonClickedEvent();
        _button.onClick.AddListener(OnButton);
    }

    private static Button FindTemporalButton()
    {
        // Incluye inactivos: el boton puede vivir en un panel (Settings) que aun no se ha abierto.
        foreach (var b in FindObjectsOfType<Button>(true))
        {
            if (b == null) continue;
            if (b.gameObject.name.IndexOf(ButtonName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return b;
            var t = b.transform;
            for (int i = 0; i < 4 && t != null; i++, t = t.parent)
            {
                if (t.name.IndexOf(ButtonName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return b;
            }
        }
        return null;
    }

    private const float PopupWidth = 900f;
    private const float PopupHeight = 280f;   // Panel (prefab: 420) = TextField/Text (TMP) de 200 de alto + botones
    private bool _doCostumes = true;
    private bool _doHeads = true;

    private void OnButton()
    {
        if (_running) return;
        ShowChoicePopup();
    }

    // Popup de eleccion: Costume + Head / Costumes / Head / Cancel. El prefab solo trae dos botones
    // (aceptar/rechazar), asi que se clona el de aceptar para los otros dos.
    private void ShowChoicePopup()
    {
        const string both = "Costume + Head";
        var popup = Popup.Create(
            "This tool creates icons for the costumes that have none and are using the default icon, " +
            "and for every head item, since heads currently reuse the costume icon and it is hard to tell " +
            "what each one is.\n\nWhat do you want to create?\n\n*This may take several minutes",
            PopupWidth, PopupHeight, both, delegate { StartBatch(true, true); }, "Cancel", null);
        if (popup == null) return;

        Button accept = null;
        foreach (var b in popup.GetComponentsInChildren<Button>(true))
        {
            var label = b.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (label != null && label.text == both) { accept = b; break; }
        }
        if (accept == null) return;

        // Orden final: Costume + Head | Costumes | Head | Cancel
        var costumesBtn = AddChoiceButton(popup, accept, "Costumes", true, false);
        var headBtn = AddChoiceButton(popup, accept, "Head", false, true);

        Button reject = null;
        foreach (var b in popup.GetComponentsInChildren<Button>(true))
        {
            var label = b.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (label != null && label.text == "Cancel") { reject = b; break; }
        }

        // Orden en la fila y ancho: 4 botones caben en el ancho del popup (el prefab los trae a 225 px).
        var ordered = new List<Button> { accept, costumesBtn, headBtn, reject };
        float buttonWidth = (PopupWidth - 20f) / ordered.Count - 2f;
        for (int i = 0; i < ordered.Count; i++)
        {
            if (ordered[i] == null) continue;
            ordered[i].transform.SetSiblingIndex(i);
            var rt = (RectTransform)ordered[i].transform;
            rt.sizeDelta = new Vector2(buttonWidth, rt.sizeDelta.y);
        }
    }

    private Button AddChoiceButton(Popup popup, Button template, string label, bool costumes, bool heads)
    {
        var clone = Instantiate(template.gameObject, template.transform.parent);
        clone.name = "Choice_" + label;
        var text = clone.GetComponentInChildren<TMPro.TMP_Text>(true);
        if (text != null) text.text = label;
        var button = clone.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();   // quita el OnAcceptButtonClick del prefab
        button.onClick.AddListener(delegate
        {
            Destroy(popup.gameObject);
            StartBatch(costumes, heads);
        });
        return button;
    }

    private void StartBatch(bool costumes, bool heads)
    {
        if (_running) return;
        _doCostumes = costumes;
        _doHeads = heads;
        StartCoroutine(RunBatch());
    }

    // ------------------------------------------------------------------ rutas

    private static string IconDir()      => Path.Combine(Application.dataPath, "Resources", "CostumeIcons");
    // Ruta final del PNG segun el tipo (ver cabecera).
    private static string IconFile(int charaId, string costumeId, bool isCommon, string headId = null)
    {
        if (!string.IsNullOrEmpty(headId))
            return Path.Combine(IconDir(), charaId.ToString(), charaId + "_" + headId + "_head.png");
        if (isCommon)
            return Path.Combine(IconDir(), costumeId + ".png");
        return Path.Combine(IconDir(), charaId.ToString(), charaId + "_" + costumeId + ".png");
    }

    private static string DesignerPath() => Path.Combine(Application.dataPath, "Resources", "Designer.png");
    private static string ListDir()      => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs"));
    private static string ListFile()     => Path.Combine(ListDir(), "missing_icons.txt");

    // ------------------------------------------------------------------ lote

    private struct Item
    {
        public int CharaId;
        public string CostumeId;
        public bool IsCommon;
        public string HeadId;      // no vacio = icono de CABEZA (CostumeId es el cuerpo con el que se carga)
        public bool IsHead { get { return !string.IsNullOrEmpty(HeadId); } }
    }

    private IEnumerator RunBatch()
    {
        _running = true;
        var popup = Popup.CreateProgress("Creating Icons");
        yield return null;

        // 0) esperar a que los datos esten cargados.
        float ready = 0f;
        while (ready < 60f)
        {
            var m = UmaViewerMain.Instance;
            if (m != null && m.Characters != null && m.Costumes != null &&
                m.Characters.Count > 0 && m.Costumes.Count > 50) break;
            ready += 0.25f;
            yield return new WaitForSeconds(0.25f);
        }
        var main = UmaViewerMain.Instance;
        var builder = UmaViewerBuilder.Instance;
        var ui = UmaViewerUI.Instance;
        if (main == null || builder == null || ui == null || main.Costumes == null || main.Costumes.Count <= 50)
        {
            Debug.LogWarning("[TemporalIcon] Datos no listos (Characters/Costumes).");
            if (popup != null) Destroy(popup.gameObject);
            _running = false;
            yield break;
        }

        Directory.CreateDirectory(IconDir());

        // 1) crear la lista de trajes sin icono.
        var items = CreateList(main, _doCostumes, _doHeads);

        // 2) por cada traje: cargar -> esperar 1 s -> foto -> png -> borrar.
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            CharaEntry chara = null;
            if (main.Characters != null)
                foreach (var cc in main.Characters) if (cc.Id == it.CharaId) { chara = cc; break; }
            if (chara == null)
            {
                Debug.LogWarning("[TemporalIcon] Personaje no encontrado: " + it.CharaId);
                continue;
            }

            string expected = "Chara_" + it.CharaId + "_" + it.CostumeId;
            bool done = false;
            UmaAssetManager.PreLoadAndRun(BuildCharaAssetList(chara), delegate
            {
                ui.StartCoroutine(builder.LoadUma(chara, it.CostumeId, false, it.IsHead ? it.HeadId : "", null));
                done = true;
            });
            float wait = 0f;
            while (!done && wait < 60f)
            {
                wait += 0.1f;
                yield return new WaitForSeconds(0.1f);
            }
            if (!done)
            {
                Debug.LogWarning("[TemporalIcon] No se pudo cargar " + expected);
                continue;
            }

            var c = builder.CurrentUMAContainer;
            if (c == null || c.name != expected)
            {
                Debug.LogWarning("[TemporalIcon] Contenedor inesperado: " + (c != null ? c.name : "null"));
                continue;
            }

            // 2a) esperar 1 segundo.
            yield return new WaitForSeconds(1f);

            // 2b) foto + .png final.
            RenderAndSave(c, it);

            // 4) borrar el personaje.
            builder.UnloadUma();
            builder.CurrentUMAContainer = null;
            yield return null;
        }

        if (popup != null) Destroy(popup.gameObject);
        _running = false;
    }

    // ------------------------------------------------------------------ lista

    private static List<Item> CreateList(UmaViewerMain main, bool doCostumes, bool doHeads)
    {
        var iconDefault = UmaViewerUI.Instance != null ? UmaViewerUI.Instance.CostumeIconDefault : null;
        string bodyPath = UmaDatabaseController.BodyPath;

        var items = new List<Item>();
        var lines = new List<string> { "# charaId,costumeId,isCommon,scale(cm)" };
        var seen = new HashSet<string>();
        var scaleCache = new Dictionary<int, string>();
        var hsbCache = new Dictionary<int, string[]>();

        // Candidatos para cargar (normales; 1013 primero). Sirven de "personaje de referencia".
        var normals = main.Characters.Where(c => c != null && !c.IsMob)
            .OrderBy(c => c.Id == 1013 ? 0 : 1).ToList();

        // Comunes (id XXXX_XX_XX): el icono no depende del personaje, pero el cuerpo SI (height/shape/
        // bust del personaje). Se busca el primer personaje que pueda cargarlo.
        var seenCommon = new HashSet<string>();
        int commonFallbacks = 0;   // comunes que NO pudo cargar 1013 (blacklist/cuerpo) y van a otro pj
        foreach (var entry in main.AbChara.Where(a =>
            doCostumes && a.Name.StartsWith(bodyPath) && !a.Name.Contains("/clothes/") && a.Name.Contains("pfb_bdy0")))
        {
            string id = Path.GetFileName(entry.Name);
            string[] split = id.Split('_');
            if (split.Length < 4) continue;
            id = split[1].Substring(3) + "_" + split[2] + "_" + split[3];
            if (!seenCommon.Add(id)) continue;

            string[] data = id.Split('_');
            int bodytype, bodysub;
            if (!int.TryParse(data[0], out bodytype) || !int.TryParse(data[1], out bodysub)) continue;
            var dress = main.Costumes.FirstOrDefault(a => a.BodyType == bodytype && a.BodyTypeSub == bodysub);

            if (BatchLimit > 0 && items.Count >= BatchLimit) break;             // prueba de encuadre
            if (!UsesDefault(dress, iconDefault)) continue;                     // ya tiene icono propio
            if (File.Exists(IconFile(0, id, true))) continue;                   // ya generado

            string bodyShort = id.Remove(id.LastIndexOf('_'));
            CharaEntry loader = null;
            foreach (var cand in normals)
            {
                string h, s, b;
                Hsb(cand, hsbCache, out h, out s, out b);
                string bodyKey = bodyPath + $"bdy{bodyShort}/pfb_bdy{id}_{h}_{s}_{b}";
                if (main.AbList.ContainsKey(bodyKey) && !CostumeBlacklist.IsBlacklisted(cand.Id, id))
                {
                    loader = cand;
                    break;
                }
            }
            if (loader == null) continue;                                       // ningun personaje puede
            if (loader.Id != 1013) commonFallbacks++;

            lines.Add(loader.Id + "," + id + ",1," + ScaleOf(loader, scaleCache));
            items.Add(new Item { CharaId = loader.Id, CostumeId = id, IsCommon = true });
        }

        // Propias (id corto), por personaje. Solo si el personaje tiene cuerpo y no esta en la blacklist.
        foreach (var chara in main.Characters)
        {
            if (!doCostumes) break;
            if (chara == null || chara.IsMob) continue;
            if (BatchLimit > 0 && items.Count >= BatchLimit) break;             // prueba de encuadre

            string nameVar = $"pfb_bdy{chara.Id}";
            foreach (var entry in main.AbChara.Where(a =>
                a.Name.StartsWith(bodyPath) && !a.Name.Contains("clothes") && a.Name.Contains(nameVar)))
            {
                string[] split = entry.Name.Split('_');
                string costumeId = split[split.Length - 1];
                int sub;
                if (!int.TryParse(costumeId, out sub)) sub = int.MinValue;
                var dress = main.Costumes.FirstOrDefault(a => a.CharaId == chara.Id && a.BodyTypeSub == sub);

                if (BatchLimit > 0 && items.Count >= BatchLimit) break;
                if (!UsesDefault(dress, iconDefault)) continue;
                if (File.Exists(IconFile(chara.Id, costumeId, false))) continue;
                if (!seen.Add(chara.Id + ":" + costumeId)) continue;
                if (!main.AbList.ContainsKey(bodyPath + $"bdy{chara.Id}_{costumeId}/pfb_bdy{chara.Id}_{costumeId}"))
                    continue;
                if (CostumeBlacklist.IsBlacklisted(chara.Id, costumeId)) continue;

                lines.Add(chara.Id + "," + costumeId + ",0," + ScaleOf(chara, scaleCache));
                items.Add(new Item { CharaId = chara.Id, CostumeId = costumeId, IsCommon = false });
            }
        }

        // Cabezas: TODAS las de cada personaje normal (aunque ya tengan icono del juego), cargadas con su primer traje propio valido (se ven cuello y hombros). Solo se salta lo ya generado.
        int heads = 0;
        foreach (var chara in normals)
        {
            if (!doHeads) break;
            if (BatchLimit > 0 && heads >= BatchLimit) break;                   // prueba de encuadre

            string headPrefix = UmaDatabaseController.HeadPath + "chr" + chara.Id + "_";
            string headVar = "/pfb_chr" + chara.Id + "_";
            string bodyCostume = null;
            foreach (var entry in main.AbChara.Where(a => a.Name.StartsWith(headPrefix)
                && !a.Name.Contains("clothes") && a.Name.Contains(headVar)))
            {
                if (BatchLimit > 0 && heads >= BatchLimit) break;
                string[] split = entry.Name.Split('_');
                string headId = split[split.Length - 1];
                if (File.Exists(IconFile(chara.Id, null, false, headId))) continue;

                if (bodyCostume == null) bodyCostume = FirstOwnBody(main, chara) ?? "";
                if (bodyCostume == "") break;                                   // sin cuerpo propio cargable

                lines.Add(chara.Id + "," + bodyCostume + ",0," + ScaleOf(chara, scaleCache) + ",head=" + headId);
                items.Add(new Item { CharaId = chara.Id, CostumeId = bodyCostume, IsCommon = false, HeadId = headId });
                heads++;
            }
        }

        Directory.CreateDirectory(ListDir());
        File.WriteAllLines(ListFile(), lines);
        return items;
    }

    // Primer traje propio cargable del personaje (cuerpo con el que se carga una cabeza). null = ninguno.
    private static string FirstOwnBody(UmaViewerMain main, CharaEntry chara)
    {
        string bodyPath = UmaDatabaseController.BodyPath;
        string nameVar = $"pfb_bdy{chara.Id}";
        foreach (var entry in main.AbChara.Where(a =>
            a.Name.StartsWith(bodyPath) && !a.Name.Contains("clothes") && a.Name.Contains(nameVar)))
        {
            string[] split = entry.Name.Split('_');
            string costumeId = split[split.Length - 1];
            if (!main.AbList.ContainsKey(bodyPath + $"bdy{chara.Id}_{costumeId}/pfb_bdy{chara.Id}_{costumeId}")) continue;
            if (CostumeBlacklist.IsBlacklisted(chara.Id, costumeId)) continue;
            return costumeId;
        }
        return null;
    }

    // (height, shape, bust) del personaje, cacheado. Necesarios para la ruta del cuerpo generico.
    private static void Hsb(CharaEntry chara, Dictionary<int, string[]> cache, out string height, out string shape, out string bust)
    {
        height = ""; shape = ""; bust = "";
        if (chara == null) return;
        string[] v;
        if (!cache.TryGetValue(chara.Id, out v))
        {
            v = new string[] { "", "", "" };
            try
            {
                var row = UmaDatabaseController.ReadCharaData(chara);
                if (row != null)
                {
                    v[0] = row["height"].ToString();
                    v[1] = row["shape"].ToString();
                    v[2] = row["bust"].ToString();
                }
            }
            catch { }
            cache[chara.Id] = v;
        }
        height = v[0]; shape = v[1]; bust = v[2];
    }

    // Altura del personaje en cm (columna `scale` de chara_data). Cacheado por charaId.
    private static string ScaleOf(CharaEntry chara, Dictionary<int, string> cache)
    {
        if (chara == null) return "";
        string v;
        if (cache.TryGetValue(chara.Id, out v)) return v;
        v = "";
        try
        {
            var row = UmaDatabaseController.ReadCharaData(chara);
            if (row != null) v = row["scale"].ToString();
        }
        catch { v = ""; }
        cache[chara.Id] = v;
        return v;
    }

    // true = el traje acabaria mostrando el icono por defecto (sin icono propio).
    private static bool UsesDefault(CostumeEntry dress, Sprite iconDefault)
    {
        if (dress == null) return true;
        if (dress.Icon == null) return true;
        return iconDefault != null && dress.Icon == iconDefault;
    }

    // ------------------------------------------------------------------ bundles

    // Reune los bundles de un personaje: cuerpo + cabeza + cola + localizador.
    private static List<UmaDatabaseEntry> BuildCharaAssetList(CharaEntry chara)
    {
        var main = UmaViewerMain.Instance;
        var ui = UmaViewerUI.Instance;
        var list = new List<UmaDatabaseEntry>();
        if (main == null) return list;

        list.AddRange(main.AbChara.Where(a => a.Name.StartsWith(UmaDatabaseController.BodyPath)
            && a.Name.Contains(chara.Id.ToString())));
        list.AddRange(main.AbChara.Where(a => a.Name.StartsWith(UmaDatabaseController.HeadPath)
            && a.Name.Contains(chara.Id.ToString())));

        string tail_path = ui != null ? ui.getCharaTailPath(chara) : null;
        if (!string.IsNullOrEmpty(tail_path))
            list.AddRange(main.AbChara.Where(a => a.Name.StartsWith(tail_path)));

        UmaDatabaseEntry locator;
        if (main.AbList.TryGetValue("3d/animator/drivenkeylocator", out locator))
            list.Add(locator);

        return list;
    }

    // ------------------------------------------------------------------ foto + icono

    private static void RenderAndSave(UmaContainerCharacter c, Item it)
    {
        bool head = it.IsHead;
        Transform body = c.transform.Find(BodyMeshName);
        if (body == null && c.Body != null) body = c.Body.transform;
        if (body == null) { Debug.LogWarning("[TemporalIcon] Sin cuerpo: " + c.name); return; }

        // Cabeza: se fotografian TODOS los renderers de malla del personaje salvo los del cuerpo y la cola
        // (cara, pelo, ojos, cejas, mejillas, boca, orejas...); la direccion frontal sigue siendo la del cuerpo.
        // Traje: solo el cuerpo (M_Body).
        Transform subject = body;
        var layers = new Dictionary<Transform, int>();
        var renderers = new List<Renderer>();
        if (head)
        {
            // Cabeza + cuerpo (cuello y hombros), sin cola ni efectos.
            foreach (var r in c.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                bool isBody = body != null && r.transform.IsChildOf(body);
                if (!isBody && !IsHeadPart(r.name)) continue;   // face, hair, eyes, mayu, cheek, mouth (no tail, tears...)
                renderers.Add(r);
                if (!layers.ContainsKey(r.transform)) layers[r.transform] = r.gameObject.layer;
                r.gameObject.layer = TempLayer;
            }
        }
        else
        {
            CaptureLayers(subject, layers);
            ApplyLayer(subject, TempLayer);
            renderers.AddRange(subject.GetComponentsInChildren<Renderer>(true));
        }

        Texture2D tex = null;
        GameObject camGo = null;
        try
        {
            Bounds fit = new Bounds(); bool hasFit = false;
            Bounds world = new Bounds(); bool hasWorld = false;
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled) continue;
                if (head && !IsFacePart(r.name)) continue;   // el encuadre usa solo la cara (el pelo infla sus bounds)
                if (!hasWorld) { world = r.bounds; hasWorld = true; } else world.Encapsulate(r.bounds);

                Bounds lb = r.localBounds;
                Vector3 ctr = lb.center, e = lb.extents;
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 corner = r.transform.TransformPoint(ctr + new Vector3(e.x * sx, e.y * sy, e.z * sz));
                    if (!hasFit) { fit = new Bounds(corner, Vector3.zero); hasFit = true; } else fit.Encapsulate(corner);
                }
            }
            if (!hasFit) { Debug.LogWarning("[TemporalIcon] Sin renderers visibles: " + c.name + (head ? " (head)" : "")); return; }

            Vector3 target = hasWorld ? world.center : fit.center;

            camGo = new GameObject("[TemporalIconCam]");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << TempLayer;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = fit.size.magnitude + 20f;

            Vector3 viewDir = (body != null ? body : c.transform).forward;          // SIEMPRE frontal
            viewDir.y = 0f;
            if (viewDir.sqrMagnitude < 0.0001f) viewDir = Vector3.forward;
            viewDir.Normalize();

            float dist = fit.size.magnitude + 5f;
            cam.transform.position = target + viewDir * dist;
            cam.transform.rotation = Quaternion.LookRotation(target - cam.transform.position, Vector3.up);

            if (head)
            {
                // Zoom por la cara; camara horizontal; la parte mas alta del pj (vertices reales, ya
                // horneados) queda a HeadTopMargin del borde superior del icono.
                if (hasWorld) { fit = world; target = world.center; }
                cam.orthographicSize = world.extents.y * HeadZoom;
                float topY = TopY(renderers.Where(r => r != null && IsHeadPart(r.name)));
                cam.transform.position = target + viewDir * dist;
                cam.transform.rotation = Quaternion.LookRotation(-viewDir, Vector3.up);
                Vector3 pos = cam.transform.position;
                pos.y = topY - cam.orthographicSize * (1f - 2f * HeadTopMargin);
                cam.transform.position = pos;
            }
            else
            {
                cam.orthographicSize = Mathf.Max(fit.extents.y, fit.extents.x) * Padding / Zoom;
                cam.transform.position += Vector3.down * (UpShift * 2f * cam.orthographicSize);
            }

            // Calidad de captura: renderScale 200% y MSAA maximo (se restauran justo despues).
            var urp = UniversalRenderPipeline.asset;
            float prevScale = urp != null ? urp.renderScale : 1f;
            int prevMsaa = urp != null ? urp.msaaSampleCount : 1;
            if (urp != null) { urp.renderScale = CaptureRenderScale; urp.msaaSampleCount = CaptureMsaa; }
            try { tex = RenderCam(cam, IconSize, IconSize); }
            finally
            {
                if (urp != null) { urp.renderScale = prevScale; urp.msaaSampleCount = prevMsaa; }
            }
        }
        finally
        {
            RestoreLayers(layers);
            if (camGo != null) Destroy(camGo);
        }

        if (tex == null) { Debug.LogWarning("[TemporalIcon] Render nulo: " + c.name); return; }

        // Copia de Designer.png (NO se edita la original); el modelo va encima.
        var bgTex = LoadTexture(DesignerPath());
        // Cabeza: el pj va DETRAS del marco (solo se ve dentro del interior del marco).
        bool[] inner = head && bgTex != null ? InnerMask(bgTex) : null;
        var output = CompositeOver(bgTex, tex, inner);
        if (bgTex != null) Destroy(bgTex);
        Destroy(tex);

        string file = IconFile(it.CharaId, it.CostumeId, it.IsCommon, it.HeadId);
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllBytes(file, output.EncodeToPNG());
        Destroy(output);
    }

    // ------------------------------------------------------------------ helpers

    // Y mundial mas alta de los vertices reales de los renderers (skinned: malla horneada con la pose actual).
    private static float TopY(IEnumerable<Renderer> rs)
    {
        float top = float.MinValue;
        foreach (var r in rs)
        {
            Mesh mesh = null;
            Matrix4x4 m;
            var sk = r as SkinnedMeshRenderer;
            if (sk != null)
            {
                mesh = new Mesh();
                sk.BakeMesh(mesh, true);
                m = Matrix4x4.TRS(sk.transform.position, sk.transform.rotation, Vector3.one);
            }
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh;
                m = r.transform.localToWorldMatrix;
            }
            if (mesh == null) continue;
            foreach (var v in mesh.vertices)
            {
                float y = m.MultiplyPoint3x4(v).y;
                if (y > top) top = y;
            }
            if (sk != null) Destroy(mesh);
        }
        return top;
    }

    // Partes que se fotografian en una cabeza.
    private static bool IsHeadPart(string n)
    {
        return n.StartsWith("M_Face") || n.StartsWith("M_Hair") || n.StartsWith("M_Eye")
            || n.StartsWith("M_Mayu") || n.StartsWith("M_Cheek") || n.StartsWith("M_Mouth");
    }

    // Partes que fijan el encuadre (sin pelo).
    private static bool IsFacePart(string n)
    {
        return n.StartsWith("M_Face") || n.StartsWith("M_Eye") || n.StartsWith("M_Mayu")
            || n.StartsWith("M_Cheek") || n.StartsWith("M_Mouth");
    }

    private static void CaptureLayers(Transform t, Dictionary<Transform, int> map)
    {
        if (t == null || map.ContainsKey(t)) return;
        map[t] = t.gameObject.layer;
        foreach (Transform child in t) CaptureLayers(child, map);
    }

    private static void ApplyLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform child in t) ApplyLayer(child, layer);
    }

    private static void RestoreLayers(Dictionary<Transform, int> map)
    {
        foreach (var kv in map) if (kv.Key != null) kv.Key.gameObject.layer = kv.Value;
    }

    private static Texture2D RenderCam(Camera cam, int width, int height)
    {
        var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Default, CaptureMsaa);
        var prevActive = RenderTexture.active;
        var prevTarget = cam.targetTexture;

        RenderTexture.active = rt;
        cam.targetTexture = rt;
        cam.Render();

        var tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();

        cam.targetTexture = prevTarget;
        RenderTexture.active = prevActive;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    private static Texture2D LoadTexture(string path)
    {
        if (!File.Exists(path)) return null;
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!t.LoadImage(File.ReadAllBytes(path))) { Destroy(t); return null; }
        return t;
    }

    // Interior del marco de Designer.png: relleno desde el centro por los pixeles claros y casi grises (el
    // fondo de puntos); la linea morada del marco los encierra. Fuera de el, el marco va por encima.
    private static bool[] InnerMask(Texture2D bg)
    {
        int w = bg.width, h = bg.height;
        Color32[] px = bg.GetPixels32();
        var mask = new bool[w * h];
        var stack = new Stack<int>();
        int start = (h / 2) * w + w / 2;
        mask[start] = true;
        stack.Push(start);
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            int x = i % w, y = i / w;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0);
                int ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int j = ny * w + nx;
                if (mask[j]) continue;
                Color32 c = px[j];
                int mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                if (mx - mn > 14 || mn < 200) continue;
                mask[j] = true;
                stack.Push(j);
            }
        }
        return mask;
    }

    private static Texture2D CompositeOver(Texture2D bg, Texture2D fg, bool[] inner = null)
    {
        int w = bg != null ? bg.width : fg.width;
        int h = bg != null ? bg.height : fg.height;

        Color32[] bgPx = bg != null ? bg.GetPixels32() : null;
        Color32[] fgPx = fg.GetPixels32();
        int fw = fg.width, fh = fg.height;

        var outPx = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            int sy = Mathf.Clamp((int)((float)y / h * fh), 0, fh - 1);
            for (int x = 0; x < w; x++)
            {
                int sx = Mathf.Clamp((int)((float)x / w * fw), 0, fw - 1);
                Color32 o = fgPx[sy * fw + sx];
                if (inner != null && !inner[y * w + x]) { outPx[y * w + x] = bgPx[y * w + x]; continue; }   // marco por encima
                if (o.a == 0)
                {
                    outPx[y * w + x] = bgPx != null ? bgPx[y * w + x] : new Color32(0, 0, 0, 0);
                    continue;
                }
                if (bgPx == null) { outPx[y * w + x] = o; continue; }

                Color32 b = bgPx[y * w + x];
                float a = o.a / 255f;
                outPx[y * w + x] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(o.r * a + b.r * (1f - a)), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(o.g * a + b.g * (1f - a)), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(o.b * a + b.b * (1f - a)), 0, 255),
                    255);
            }
        }

        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.SetPixels32(outPx);
        t.Apply();
        return t;
    }
}
