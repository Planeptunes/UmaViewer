using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

// Controles de los ajustes del live. Los objetos viven en la escena `LiveScene` (YAML): este
// componente solo los rellena, sincroniza sus valores con `Config` y cablea los eventos. Los
// desplegables de FPS y de render scale ademas se pueden escribir (ver TypeInDropdown).
public class LiveGraphicsSettingsUI : MonoBehaviour
{
    // Presets del panel del viewer, mas 0 = Unlimited para el live.
    private static readonly int[] FrameRatePresets = { 30, 60, 90, 120, 180, 240, 0 };
    private static readonly int[] RenderScalePresets = { 50, 75, 100, 150, 200 };
    private static readonly string[] MsaaOptions = { "Off", "2x", "4x", "8x" };

    // (VIDEO SPEED) Velocidades del live (Time.timeScale). 1 = normal, <1 = camara lenta.
    private static readonly float[] VideoSpeedValues = { 0.1f, 0.25f, 0.5f, 0.75f, 1f, 1.5f, 2f };
    private static readonly string[] VideoSpeedLabels = { "0.10x", "0.25x", "0.50x", "0.75x", "1.00x", "1.50x", "2.00x" };

    private const int MinRenderScale = 50;
    private const int MaxRenderScale = 200;

    public Dropdown FrameRateDropdown;
    public Dropdown RenderScaleDropdown;
    public Dropdown MsaaDropdown;
    public Toggle PostEffectsToggle;

    private TypeInDropdown _frameRate;
    private TypeInDropdown _renderScale;
    private Dropdown _videoSpeedDropdown;
    private bool _applied;

    // Idempotente: lo llama LiveViewerUI cuando Config ya existe.
    public void Apply()
    {
        if (_applied) return;
        _applied = true;

        if (FrameRateDropdown != null)
        {
            _frameRate = new LegacyTypeInDropdown(FrameRateDropdown, FrameRatePresets, 0, 0, "", FrameRateLabel, ApplyFrameRate);
            _frameRate.SetValue(Config.Instance.TargetFrameRate);
        }

        if (RenderScaleDropdown != null)
        {
            _renderScale = new LegacyTypeInDropdown(RenderScaleDropdown, RenderScalePresets, MinRenderScale, MaxRenderScale, "%", ApplyRenderScale);
            _renderScale.SetValue(Config.Instance.RenderScalePercent);
        }

        FillChoice(MsaaDropdown, MsaaOptions, Config.Instance.AntiAliasing, delegate (int value)
        {
            Config.Instance.AntiAliasing = value;
            UISettingsGraphics.SaveGraphics();
        });

        if (PostEffectsToggle != null)
        {
            PostEffectsToggle.SetIsOnWithoutNotify(Config.Instance.PostEffects);
            PostEffectsToggle.onValueChanged.AddListener(delegate (bool value)
            {
                Config.Instance.PostEffects = value;
                Config.Instance.UpdateConfig(false);
                UISettingsGraphics.ApplyLivePostEffects();
                SetPostEffectRowsInteractable(value);
                UpdatePostEffectCaption();
            });
        }

        WirePostEffectRows();

        // Solo se listan los post efectos que la canción cargada usa de verdad (ver LivePostEffectUsage).
        Gallop.RenderPipeline.LivePostEffectUsage.Changed += ApplyPostEffectRelevance;
        ApplyPostEffectRelevance();

        //Aplica el estado guardado nada mas entrar al live (el interruptor puede venir apagado).
        UISettingsGraphics.ApplyLivePostEffects();
        if (PostEffectsToggle != null) SetPostEffectRowsInteractable(Config.Instance.PostEffects);

        CreateVideoSpeedRow();
        SetupSettingsToggleButtons();
        RearrangePostEffectRow();
    }

    // =========================================================================================
    // Fila "Post effects": el tick pasa a ir justo detras del texto y el desplegable "Post effects list"
    // (que estaba en la fila de debajo, con su propia etiqueta) sube a esta misma fila y se alarga hacia
    // la izquierda hasta casi tocar el tick. Se hace por codigo: el desplegable se cuelga de la fila del
    // tick (asi el layout del panel ya no le reserva una fila) y se oculta la etiqueta de la fila vieja.
    // =========================================================================================
    private const float PostEffectsGap = 8f;
    private const float PostEffectsDropdownHeight = 26f;

    private void RearrangePostEffectRow()
    {
        if (PostEffectsToggle == null || PostEffectListButton == null) return;

        var row = PostEffectsToggle.transform as RectTransform;
        var check = PostEffectsToggle.targetGraphic != null
            ? PostEffectsToggle.targetGraphic.rectTransform : null;
        var dropdown = PostEffectListButton.transform as RectTransform;
        if (row == null || check == null || dropdown == null) return;

        // Ancho del texto "Post effects" (etiqueta de la fila, alineada a la izquierda).
        var label = row.GetComponentInChildren<Text>(true);
        float textWidth = label != null ? label.preferredWidth : 110f;
        float labelLeft = label != null
            ? label.rectTransform.anchoredPosition.x - label.rectTransform.sizeDelta.x * label.rectTransform.pivot.x
            : 0f;

        // Tick: anclado a la izquierda, justo despues de la "s".
        check.anchorMin = check.anchorMax = new Vector2(0f, 0.5f);
        check.pivot = new Vector2(0f, 0.5f);
        float checkLeft = labelLeft + textWidth + PostEffectsGap;
        check.anchoredPosition = new Vector2(checkLeft, 0f);
        float dropdownLeft = checkLeft + check.sizeDelta.x + PostEffectsGap;

        // Desplegable: hijo de la fila del tick, de casi tocar el tick hasta el borde derecho.
        var oldRow = dropdown.parent;
        dropdown.SetParent(row, false);
        dropdown.anchorMin = new Vector2(0f, 0.5f);
        dropdown.anchorMax = new Vector2(1f, 0.5f);
        dropdown.pivot = new Vector2(0.5f, 0.5f);
        dropdown.offsetMin = new Vector2(dropdownLeft, -PostEffectsDropdownHeight * 0.5f);
        dropdown.offsetMax = new Vector2(0f, PostEffectsDropdownHeight * 0.5f);
        dropdown.SetAsLastSibling();

        // La fila vieja ya no pinta nada: se oculta (su etiqueta "Post effects list" sobra).
        if (oldRow != null && oldRow != row) oldRow.gameObject.SetActive(false);
    }

    // =========================================================================================
    // Botones Open / Close del panel de ajustes (Canvas/Trigger/Bottom/Setting_Btns). Open despliega el
    // panel y se oculta; Close (en el mismo sitio) lo repliega y vuelve a mostrar Open. Al desplegar, el
    // boton visible sube a Y 10 con alto 45; al plegar vuelve a Y 0 y alto 25. Se aplica a los dos, que
    // ocupan el mismo hueco.
    // =========================================================================================
    private const float SettingsBtnClosedY = 0f, SettingsBtnClosedHeight = 25f;
    private const float SettingsBtnOpenY = 10f, SettingsBtnOpenHeight = 45f;
    private RectTransform _settingsOpenRect, _settingsCloseRect;

    private void SetupSettingsToggleButtons()
    {
        var group = GameObject.Find("Canvas/Trigger/Bottom/Setting_Btns");
        if (group == null) return;
        var open = group.transform.Find("Open");
        var close = group.transform.Find("Close");
        _settingsOpenRect = open as RectTransform;
        _settingsCloseRect = close as RectTransform;

        var openButton = open != null ? open.GetComponent<Button>() : null;
        if (openButton != null) openButton.onClick.AddListener(delegate { ApplySettingsToggleLayout(true); });
        var closeButton = close != null ? close.GetComponent<Button>() : null;
        if (closeButton != null) closeButton.onClick.AddListener(delegate { ApplySettingsToggleLayout(false); });

        // El Close de dentro del panel tambien lo repliega y vuelve a mostrar Open.
        // (El panel empieza inactivo: se busca con Transform.Find desde la raiz del canvas.)
        var panelClose = group.transform.root.Find("Panel/Close");
        var panelCloseButton = panelClose != null ? panelClose.GetComponent<Button>() : null;
        if (panelCloseButton != null)
            panelCloseButton.onClick.AddListener(delegate { ApplySettingsToggleLayout(false); });

        ApplySettingsToggleLayout(false);
    }

    // true mientras el panel de ajustes (Canvas/Panel) esta desplegado. La barra inferior no se oculta mientras.
    public bool IsSettingsPanelOpen
    {
        get
        {
            var panel = GameObject.Find("Canvas/Panel");
            return panel != null && panel.activeSelf;
        }
    }

    private void ApplySettingsToggleLayout(bool panelOpen)
    {
        ApplySettingsToggleRect(_settingsOpenRect, panelOpen);
        ApplySettingsToggleRect(_settingsCloseRect, panelOpen);
    }

    private static void ApplySettingsToggleRect(RectTransform rect, bool panelOpen)
    {
        if (rect == null) return;
        var position = rect.anchoredPosition;
        position.y = panelOpen ? SettingsBtnOpenY : SettingsBtnClosedY;
        rect.anchoredPosition = position;
        var size = rect.sizeDelta;
        size.y = panelOpen ? SettingsBtnOpenHeight : SettingsBtnClosedHeight;
        rect.sizeDelta = size;
    }

    // =========================================================================================
    // (VIDEO SPEED) Fila nueva "Video speed" clonada de la fila de "MAX fps".
    //
    // El panel es un ScrollRect (Viewport -> Content) con VerticalLayoutGroup + ContentSizeFitter,
    // asi que basta con anadir el clon como hermano de la fila original: el layout lo coloca y el
    // contenido crece solo (si no cabe, se desplaza con el scroll). No hace falta tocar la escena.
    // =========================================================================================
    private void CreateVideoSpeedRow()
    {
        if (FrameRateDropdown == null) return;

        Transform sourceRow = FrameRateDropdown.transform.parent;
        if (sourceRow == null || sourceRow.parent == null) return;

        GameObject rowClone = Instantiate(sourceRow.gameObject, sourceRow.parent, false);
        rowClone.name = "VideoSpeedRow";
        rowClone.transform.SetSiblingIndex(sourceRow.GetSiblingIndex() + 1);

        Dropdown dropdown = rowClone.GetComponentInChildren<Dropdown>(true);
        if (dropdown == null)
        {
            Destroy(rowClone);
            return;
        }

        RenameRowLabel(rowClone, dropdown, "Video speed");

        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string>(VideoSpeedLabels));
        dropdown.SetValueWithoutNotify(IndexOfVideoSpeed(Config.Instance.GetVideoSpeed()));
        dropdown.onValueChanged.AddListener(delegate (int value)
        {
            int index = Mathf.Clamp(value, 0, VideoSpeedValues.Length - 1);
            Config.Instance.VideoSpeed = VideoSpeedValues[index];
            Config.Instance.UpdateConfig(false);

            var director = Gallop.Live.Director.instance;
            if (director != null) director.ApplyVideoSpeed();
        });

        _videoSpeedDropdown = dropdown;
    }

    // =========================================================================================
    // (POST EFFECTS) Elemento unico con listado emergente (estilo Dropdown) y multiseleccion.
    //
    // Los objetos viven en la escena `LiveScene`:
    //  - `PostEffectListButton`  fila "Post effects list" (boton; caption = "N/16")
    //  - `PostEffectListPopup`   panel flotante fuera del Viewport (para que el RectMask2D no lo
    //                            recorte) con una fila label + tick por efecto
    //  - `PostEffectListBlocker` capa transparente a pantalla completa que cierra al clicar fuera
    // Aqui solo se cablean, se posiciona el panel bajo el boton y se sincroniza con `Config`.
    // Un efecto se dibuja solo si el interruptor global Y el suyo estan activos.
    // =========================================================================================
    [Header("Post effects list (scene objects)")]
    public Button PostEffectListButton;
    public Text PostEffectListCaption;
    public GameObject PostEffectListPopup;
    public GameObject PostEffectListBlocker;
    public Toggle[] PostEffectToggles;

    // The scene (LiveScene) only ships a row per effect up to Lens flare. Effects added to
    // PostEffectSelection.All later (Transmitted light) get their row cloned from the last one; the
    // popup content is a VerticalLayoutGroup + ContentSizeFitter, so the clone lays itself out.
    private void EnsurePostEffectToggles()
    {
        var ids = Gallop.RenderPipeline.PostEffectSelection.All;
        if (PostEffectToggles == null || PostEffectToggles.Length == 0 || PostEffectToggles.Length >= ids.Length) return;
        var template = PostEffectToggles[PostEffectToggles.Length - 1];
        if (template == null) return;

        var list = new System.Collections.Generic.List<Toggle>(PostEffectToggles);
        for (int i = list.Count; i < ids.Length; i++)
        {
            GameObject clone = Instantiate(template.gameObject, template.transform.parent);
            clone.name = "PostEffectToggle_" + ids[i];
            clone.transform.SetSiblingIndex(template.transform.parent.childCount - 1);
            clone.SetActive(true);
            var toggle = clone.GetComponent<Toggle>();
            if (toggle == null) { Destroy(clone); break; }
            toggle.onValueChanged.RemoveAllListeners();
            var text = clone.GetComponentInChildren<Text>(true);
            if (text != null) text.text = Gallop.RenderPipeline.PostEffectSelection.Label(ids[i]);
            list.Add(toggle);
        }
        PostEffectToggles = list.ToArray();
    }

    private void WirePostEffectRows()
    {
        EnsurePostEffectToggles();
        if (PostEffectListButton != null)
            PostEffectListButton.onClick.AddListener(TogglePostEffectList);

        if (PostEffectListBlocker != null)
        {
            var button = PostEffectListBlocker.GetComponent<Button>();
            if (button == null) button = PostEffectListBlocker.AddComponent<Button>();
            button.onClick.AddListener(ClosePostEffectList);
        }

        ClosePostEffectList();

        if (PostEffectToggles == null)
        {
            UpdatePostEffectCaption();
            return;
        }

        var ids = Gallop.RenderPipeline.PostEffectSelection.All;
        int count = Mathf.Min(PostEffectToggles.Length, ids.Length);
        for (int i = 0; i < count; i++)
        {
            Toggle toggle = PostEffectToggles[i];
            if (toggle == null) continue;

            Gallop.RenderPipeline.PostEffectId captured = ids[i];
            toggle.SetIsOnWithoutNotify(Gallop.RenderPipeline.PostEffectSelection.IsEnabled(captured));
            toggle.onValueChanged.AddListener(delegate (bool value)
            {
                Gallop.RenderPipeline.PostEffectSelection.SetEnabled(captured, value);
                UpdatePostEffectCaption();
            });
        }

        UpdatePostEffectCaption();
    }

    private void TogglePostEffectList()
    {
        if (PostEffectListPopup == null) return;
        if (PostEffectListPopup.activeSelf) ClosePostEffectList();
        else OpenPostEffectList();
    }

    private void OpenPostEffectList()
    {
        if (PostEffectListPopup == null) return;

        // El listado se despliega HACIA ARRIBA (debajo no hay sitio): mismo ancho que el boton del
        // listado y su borde inferior pegado al borde superior del boton.
        PlacePostEffectPopupAboveButton();

        if (PostEffectListBlocker != null) PostEffectListBlocker.SetActive(true);
        PostEffectListPopup.SetActive(true);
        PostEffectListPopup.transform.SetAsLastSibling();
    }

    private void PlacePostEffectPopupAboveButton()
    {
        var popup = PostEffectListPopup != null ? PostEffectListPopup.transform as RectTransform : null;
        var button = PostEffectListButton != null ? PostEffectListButton.transform as RectTransform : null;
        var parent = popup != null ? popup.parent as RectTransform : null;
        if (popup == null || button == null || parent == null) return;

        var corners = new Vector3[4];   // 0 = abajo-izq, 1 = arriba-izq, 2 = arriba-der, 3 = abajo-der
        button.GetWorldCorners(corners);
        Vector3 topLeft = parent.InverseTransformPoint(corners[1]);
        Vector3 topRight = parent.InverseTransformPoint(corners[2]);

        popup.anchorMin = popup.anchorMax = Vector2.zero;
        popup.pivot = Vector2.zero;
        popup.sizeDelta = new Vector2(topRight.x - topLeft.x, popup.sizeDelta.y);
        popup.anchoredPosition = new Vector2(topLeft.x - parent.rect.xMin, topLeft.y - parent.rect.yMin);
    }

    private void ClosePostEffectList()
    {
        if (PostEffectListPopup != null) PostEffectListPopup.SetActive(false);
        if (PostEffectListBlocker != null) PostEffectListBlocker.SetActive(false);
    }

    private void OnDestroy()
    {
        Gallop.RenderPipeline.LivePostEffectUsage.Changed -= ApplyPostEffectRelevance;
    }

    // Oculta las filas de los efectos con pista propia que esta canción no usa. El tick de un efecto
    // oculto no se toca: sigue valiendo lo que tuviera guardado.
    private void ApplyPostEffectRelevance()
    {
        if (PostEffectToggles == null) return;
        var ids = Gallop.RenderPipeline.PostEffectSelection.All;
        int count = Mathf.Min(PostEffectToggles.Length, ids.Length);
        for (int i = 0; i < count; i++)
        {
            Toggle toggle = PostEffectToggles[i];
            if (toggle == null) continue;
            toggle.gameObject.SetActive(Gallop.RenderPipeline.LivePostEffectUsage.IsRelevant(ids[i]));
        }
        UpdatePostEffectCaption();
    }

    // Texto del boton del listado: "PostEffect List: [X/Y]" con X = efectos activos e Y = efectos que se
    // listan para esta cancion (los ocultos no cuentan). Centrado en el boton.
    private const string PostEffectListLabel = "PostEffect List";

    private void UpdatePostEffectCaption()
    {
        if (PostEffectListCaption == null) return;
        var ids = Gallop.RenderPipeline.PostEffectSelection.All;
        // Con el interruptor general apagado no hay ningun efecto activo: X = 0.
        bool master = PostEffectsToggle == null || PostEffectsToggle.isOn;
        int total = 0, on = 0;
        if (PostEffectToggles != null)
        {
            for (int i = 0; i < PostEffectToggles.Length; i++)
            {
                if (PostEffectToggles[i] == null) continue;
                if (i < ids.Length && !Gallop.RenderPipeline.LivePostEffectUsage.IsRelevant(ids[i])) continue;
                total++;
                if (master && PostEffectToggles[i].isOn) on++;
            }
        }
        // La etiqueta ocupa todo el boton y no se recorta: con el overflow en Truncate, la linea de 14 pt
        // no cabe en los 13 px que le deja el layout de la escena y el texto desaparece.
        var captionRect = PostEffectListCaption.rectTransform;
        captionRect.offsetMin = Vector2.zero;
        captionRect.offsetMax = Vector2.zero;
        PostEffectListCaption.horizontalOverflow = HorizontalWrapMode.Overflow;
        PostEffectListCaption.verticalOverflow = VerticalWrapMode.Overflow;
        PostEffectListCaption.alignment = TextAnchor.MiddleCenter;
        PostEffectListCaption.text = PostEffectListLabel + ": [" + on + "/" + total + "]";
    }

    private void SetPostEffectRowsInteractable(bool interactable)
    {
        if (PostEffectListButton != null) PostEffectListButton.interactable = true;
        if (PostEffectToggles == null) return;
        for (int i = 0; i < PostEffectToggles.Length; i++)
        {
            if (PostEffectToggles[i] != null) PostEffectToggles[i].interactable = interactable;
        }
    }

    // La etiqueta de la fila es el texto que NO esta dentro del desplegable (el resto son el
    // caption y los items del propio Dropdown).
    private static void RenameRowLabel(GameObject row, Dropdown dropdown, string text)
    {
        var texts = row.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] == null || texts[i].transform.IsChildOf(dropdown.transform)) continue;
            texts[i].text = text;
            return;
        }

        var tmpTexts = row.GetComponentsInChildren<TMPro.TMP_Text>(true);
        for (int i = 0; i < tmpTexts.Length; i++)
        {
            if (tmpTexts[i] == null || tmpTexts[i].transform.IsChildOf(dropdown.transform)) continue;
            tmpTexts[i].text = text;
            return;
        }
    }

    private static int IndexOfVideoSpeed(float speed)
    {
        int best = 4;   // 1.00x
        float bestDelta = float.MaxValue;
        for (int i = 0; i < VideoSpeedValues.Length; i++)
        {
            float delta = Mathf.Abs(VideoSpeedValues[i] - speed);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = i;
            }
        }
        return best;
    }

    private void Update()
    {
        // El listado de post efectos se cierra con Escape (o clic fuera, via el blocker).
        if (PostEffectListPopup != null && PostEffectListPopup.activeSelf &&
            Input.GetKeyDown(KeyCode.Escape))
            ClosePostEffectList();

        // Un clic dentro de uno de los desplegables escribibles empieza a escuchar el teclado; un
        // clic fuera adopta lo que hubiera escrito.
        if (Input.GetMouseButtonDown(0))
        {
            HandleTypingClick(_frameRate);
            HandleTypingClick(_renderScale);
        }

        if (_frameRate != null) _frameRate.Update();
        if (_renderScale != null) _renderScale.Update();

        // Analiza la canción en cuanto hay timeline cargado (compara solo la referencia, es barato).
        var director = Gallop.Live.Director.instance;
        var control = director != null ? director._liveTimelineControl : null;
        if (control != null && control.data != null)
            Gallop.RenderPipeline.LivePostEffectUsage.Refresh(control.data);
    }

    // Vuelve a pintar el desplegable de FPS con el valor actual de Config.
    public void RefreshFrameRate()
    {
        if (_frameRate != null) _frameRate.SetValue(Config.Instance.TargetFrameRate);
    }

    private static void HandleTypingClick(TypeInDropdown field)
    {
        if (field == null) return;
        if (ContainsPointer(field.Rect)) field.BeginTyping();
        else if (field.Typing) field.Commit();
    }

    private static bool ContainsPointer(RectTransform rect)
    {
        if (rect == null) return false;
        var canvas = rect.GetComponentInParent<Canvas>();
        var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        return RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, cam);
    }

    private static string FrameRateLabel(int fps)
    {
        return fps == 0 ? "Unlimited" : fps.ToString(CultureInfo.InvariantCulture);
    }

    private void ApplyFrameRate(int fps)
    {
        if (Config.Instance.TargetFrameRate != fps)
        {
            Config.Instance.TargetFrameRate = fps;
            Config.Instance.UpdateConfig(false);
        }
        UmaViewerMain.ApplyFrameRateLimitLive();
        if (_frameRate != null) _frameRate.SetValue(fps);
    }

    private void ApplyRenderScale(int percent)
    {
        Config.Instance.RenderScalePercent = Mathf.Clamp(percent, MinRenderScale, MaxRenderScale);
        UISettingsGraphics.SaveGraphics();
        if (_renderScale != null) _renderScale.SetValue(Config.Instance.RenderScalePercent);
    }

    private static void FillChoice(Dropdown dropdown, string[] options, int value, UnityEngine.Events.UnityAction<int> change)
    {
        if (dropdown == null) return;
        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string>(options));
        dropdown.SetValueWithoutNotify(Mathf.Clamp(value, 0, options.Length - 1));
        dropdown.onValueChanged.AddListener(change);
    }
}
