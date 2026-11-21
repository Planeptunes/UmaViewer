// Herramienta local (no forma parte del visor publicado).
// BodyTextureRecolor: zoomable texture view, eyedropper window and colour picker window.
using UnityEngine;
using Core = BodyTextureRecolorCore;

public partial class BodyTextureRecolor
{
    // ------------------------------------------------------------------ zoomable view
    /// <summary>
    /// Image fitted to an area keeping its aspect ratio (re-fitted when the area changes, until the user zooms).
    /// Coordinates are local to the area; image coordinates are top-down (row 0 = top of the picture).
    /// </summary>
    class ZoomView
    {
        public float scale = 1f;
        public Vector2 offset;
        public bool fit = true;
        Vector2 _lastArea;
        Vector2 _panStart, _panOffset;
        public bool panning;
        public int panButton = 2;

        public void Layout(Vector2 area, int w, int h)
        {
            if (fit && (area != _lastArea || scale <= 0f)) Fit(area, w, h);
            _lastArea = area;
        }

        public void Fit(Vector2 area, int w, int h)
        {
            scale = Mathf.Min(area.x / w, area.y / h);
            offset = new Vector2((area.x - w * scale) * 0.5f, (area.y - h * scale) * 0.5f);
            fit = true;
        }

        public Rect ImageRect(int w, int h)
        {
            return new Rect(offset.x, offset.y, w * scale, h * scale);
        }

        public Vector2 ToImage(Vector2 local)
        {
            return (local - offset) / scale;
        }

        public Vector2 ToLocal(Vector2 image)
        {
            return offset + image * scale;
        }

        public void ZoomAt(Vector2 local, float factor)
        {
            Vector2 ip = ToImage(local);
            scale = Mathf.Clamp(scale * factor, 0.02f, 64f);
            offset = local - ip * scale;
            fit = false;
        }

        public void StartPan(Vector2 local)
        {
            panning = true;
            _panStart = local;
            _panOffset = offset;
        }

        public void Pan(Vector2 local)
        {
            offset = _panOffset + (local - _panStart);
            fit = false;
        }
    }

    /// <summary>Mouse position in the current GUI coordinates (window-local inside a window function).</summary>
    static Vector2 MouseGui()
    {
        Vector3 m = Input.mousePosition;
        return GUIUtility.ScreenToGUIPoint(new Vector2(m.x, Screen.height - m.y));
    }

    /// <summary>
    /// Wheel zoom and middle-button (or Space + left) pan. The drag is polled from Input on every Layout event
    /// (the window does not always get MouseDrag/MouseUp). Returns true when the event was used.
    /// </summary>
    static bool HandleViewInput(ZoomView view, Rect area)
    {
        Event e = Event.current;
        if (view.panning && e.type == EventType.Layout)
        {
            if (Input.GetMouseButton(view.panButton)) view.Pan(MouseGui() - area.position);
            else view.panning = false;
        }

        Vector2 local = e.mousePosition - area.position;
        bool inside = area.Contains(e.mousePosition);
        bool spaceHeld = Input.GetKey(KeyCode.Space);

        switch (e.type)
        {
            case EventType.ScrollWheel:
                if (!inside) return false;
                view.ZoomAt(local, e.delta.y < 0 ? 1.25f : 0.8f);
                e.Use();
                return true;
            case EventType.MouseDown:
                if (inside && (e.button == 2 || (e.button == 0 && spaceHeld)))
                {
                    view.panButton = e.button;
                    view.StartPan(local);
                    e.Use();
                    return true;
                }
                break;
            case EventType.MouseDrag:
            case EventType.MouseUp:
                if (view.panning) { e.Use(); return true; }
                break;
        }
        return view.panning;
    }

    void ZoomButtons(ZoomView view, Vector2 area, int w, int h)
    {
        if (GUILayout.Button("−", GUILayout.Width(26))) view.ZoomAt(area * 0.5f, 0.8f);
        if (GUILayout.Button("+", GUILayout.Width(26))) view.ZoomAt(area * 0.5f, 1.25f);
        if (GUILayout.Button("Ajustar", GUILayout.Width(60))) view.Fit(area, w, h);
        if (GUILayout.Button("1:1", GUILayout.Width(36))) view.ZoomAt(area * 0.5f, 1f / view.scale);
        GUILayout.Label(Mathf.RoundToInt(view.scale * 100f) + "%", GUILayout.Width(50));
    }

    /// <summary>Resize grip in the bottom-right corner of a window (drag polled from Input); returns the new size.</summary>
    static Vector2 ResizeGrip(Vector2 size, Vector2 min, ref bool resizing)
    {
        Rect grip = new Rect(size.x - 18, size.y - 18, 18, 18);
        GUI.Label(grip, "◢");
        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && grip.Contains(e.mousePosition))
        {
            resizing = true;
            e.Use();
        }
        else if (resizing && e.type == EventType.Layout)
        {
            if (Input.GetMouseButton(0))
            {
                Vector2 m = MouseGui();
                size = new Vector2(Mathf.Max(min.x, m.x + 9), Mathf.Max(min.y, m.y + 9));
            }
            else resizing = false;
        }
        else if (resizing && (e.type == EventType.MouseDrag || e.type == EventType.MouseUp))
        {
            e.Use();
        }
        return size;
    }

    static Rect DefaultBigRect()
    {
        float w = Mathf.Min(Screen.width - 40, 1100), h = Mathf.Min(Screen.height - 40, 860);
        return new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
    }

    // ------------------------------------------------------------------ eyedropper
    bool _eyeOpen, _eyeResizing;
    Rect _eyeRect;
    int _eyeKind, _eyeZone, _eyeTex;   // _eyeKind: rule set; _eyeTex: texture shown (the diffuse for "Diffuse + Toon")
    bool _eyeForNew;   // true: the picked colour is the new colour (dst) instead of the colour to replace (src)
    readonly ZoomView _eyeView = new ZoomView();
    bool _eyePicked;
    int _eyeX, _eyeY;           // texture pixel (row 0 = bottom, like Texture2D)
    Color32 _eyeColor;
    string _eyeHover = "";

    void OpenEyedropper(int kind, int zone, bool forNew = false)
    {
        int tex = kind == KindBoth ? KindDiffuse : kind;
        if (_src[tex] == null) return;
        _eyeTex = tex;
        _eyeForNew = forNew;
        _eyeKind = kind;
        _eyeZone = zone;
        _eyePicked = false;
        _eyeHover = "";
        _eyeView.fit = true;
        if (_eyeRect.width < 10) _eyeRect = DefaultBigRect();
        _pendingEyeSize = _eyeRect.size;
        _eyeOpen = true;
    }

    void DrawEyedropperWindow()
    {
        // (window functions run after OnGUI: the grip size of the previous event is applied here)
        if (_pendingEyeSize.x > 0f) { _eyeRect.width = _pendingEyeSize.x; _eyeRect.height = _pendingEyeSize.y; }
        _eyeRect = GUI.ModalWindow(EyeWinId, _eyeRect, DrawEyedropper,
            "Cuentagotas · " + KindNames[_eyeKind] + " · Zona " + Core.ZoneNames[_eyeZone] + (_eyeForNew ? " · color nuevo" : " · color a sustituir"), WindowStyle);
    }

    void DrawEyedropper(int id)
    {
        Core.Source s = _src[_eyeTex];
        Texture2D tex = _opaque[_eyeTex];
        if (s == null || tex == null) { _eyeOpen = false; return; }
        Event e = Event.current;

        GUILayout.Label("Clic izquierdo: previsualizar color · Clic derecho: aceptar · Rueda: zoom · Botón central / Espacio+arrastrar: mover · Esc: cancelar");
        Rect area = new Rect(6, 42, _eyeRect.width - 12, _eyeRect.height - 42 - 34);
        _eyeView.Layout(area.size, s.w, s.h);

        // Left-button drag keeps previewing: polled from Input on Layout (MouseDrag does not always arrive).
        if (_eyeDragging && e.type == EventType.Layout)
        {
            if (Input.GetMouseButton(0)) EyePickAt(_eyeView.ToImage(MouseGui() - area.position), s);
            else _eyeDragging = false;
        }

        if (!HandleViewInput(_eyeView, area))
        {
            Vector2 local = e.mousePosition - area.position;
            Vector2 ip = _eyeView.ToImage(local);
            bool inImage = area.Contains(e.mousePosition) && ip.x >= 0 && ip.y >= 0 && ip.x < s.w && ip.y < s.h;
            int px = Mathf.FloorToInt(ip.x), row = s.h - 1 - Mathf.FloorToInt(ip.y);

            if (e.type == EventType.Repaint || e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
                _eyeHover = inImage ? "Cursor: (" + px + ", " + (s.h - 1 - row) + ") " + Core.ToHex(s.px[row * s.w + px]) : "";

            if (inImage && e.type == EventType.MouseDown && e.button == 0)
            {
                EyePickAt(ip, s);
                _eyeDragging = true;
                e.Use();
            }
            else if (_eyeDragging && (e.type == EventType.MouseDrag || e.type == EventType.MouseUp))
            {
                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 1 && area.Contains(e.mousePosition))
            {
                if (!_eyePicked && inImage)
                {
                    _eyePicked = true;
                    _eyeColor = s.px[row * s.w + px];
                    _eyeColor.a = 255;
                }
                e.Use();
                AcceptEyedropper();
                return;
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _eyeOpen = false;
                e.Use();
                return;
            }
            else if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                e.Use();
                AcceptEyedropper();
                return;
            }
        }

        // canvas
        GUI.BeginGroup(area);
        GUI.color = new Color(0.17f, 0.17f, 0.17f);
        GUI.DrawTexture(new Rect(0, 0, area.width, area.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        tex.filterMode = _eyeView.scale >= 1f ? FilterMode.Point : FilterMode.Bilinear;
        GUI.DrawTexture(_eyeView.ImageRect(s.w, s.h), tex, ScaleMode.StretchToFill, false);
        if (_eyePicked)
        {
            Vector2 c = _eyeView.ToLocal(new Vector2(_eyeX + 0.5f, (s.h - 1 - _eyeY) + 0.5f));
            float r = Mathf.Max(_eyeView.scale * 0.5f, 5f);
            GUI.color = Color.black;
            DrawFrame(new Rect(c.x - r - 1, c.y - r - 1, 2 * r + 2, 2 * r + 2), 3);
            GUI.color = Color.white;
            DrawFrame(new Rect(c.x - r, c.y - r, 2 * r, 2 * r), 1);
        }
        GUI.EndGroup();

        // bottom bar
        GUILayout.BeginArea(new Rect(6, _eyeRect.height - 30, _eyeRect.width - 30, 26));
        GUILayout.BeginHorizontal();
        GUILayout.Label("Color:", GUILayout.Width(44));
        Rect sw = GUILayoutUtility.GetRect(50, 22, GUILayout.Width(50), GUILayout.Height(22));
        ColorBox(sw, _eyePicked ? _eyeColor : new Color32(60, 60, 60, 255), false, 1);
        GUILayout.Label(_eyePicked ? Core.ToHex(_eyeColor) + "  (" + _eyeX + ", " + (s.h - 1 - _eyeY) + ")" : "—", GUILayout.Width(170));
        GUI.enabled = _eyePicked;
        if (GUILayout.Button("Aceptar", GUILayout.Width(80))) AcceptEyedropper();
        GUI.enabled = true;
        if (GUILayout.Button("Cancelar", GUILayout.Width(80))) _eyeOpen = false;
        GUILayout.Label(_eyeHover, GUILayout.Width(220));
        ZoomButtons(_eyeView, area.size, s.w, s.h);
        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        Vector2 size = ResizeGrip(_eyeRect.size, new Vector2(500, 360), ref _eyeResizing);
        _pendingEyeSize = size;
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    Vector2 _pendingEyeSize;
    bool _eyeDragging;

    /// <summary>Previews the pixel at image coords (top-down); ignored outside the texture.</summary>
    void EyePickAt(Vector2 ip, Core.Source s)
    {
        if (ip.x < 0 || ip.y < 0 || ip.x >= s.w || ip.y >= s.h) return;
        int px = Mathf.FloorToInt(ip.x), row = s.h - 1 - Mathf.FloorToInt(ip.y);
        _eyePicked = true;
        _eyeX = px; _eyeY = row;
        _eyeColor = s.px[row * s.w + px];
        _eyeColor.a = 255;
    }

    void AcceptEyedropper()
    {
        if (!_eyePicked) return;
        if (_eyeForNew) _rules[_eyeKind][_eyeZone].dst = _eyeColor;
        else _rules[_eyeKind][_eyeZone].src = _eyeColor;
        _resultDirty = true;
        _eyeOpen = false;
    }

    // ------------------------------------------------------------------ colour picker (HSV square + hue bar + hex + RGB)
    bool _pickerOpen;
    Rect _pickerRect = new Rect(200, 120, 420, 360);
    int _pickerKind, _pickerZone;
    float _ph, _ps, _pv;
    Color32 _pickerOld;
    string _pickerHex = "";
    Texture2D _svTex, _hueTex;
    float _svTexHue = -1f;
    int _pickerDrag;   // 0 none, 1 SV, 2 hue

    void OpenPicker(int kind, int zone)
    {
        _pickerKind = kind;
        _pickerZone = zone;
        _pickerOld = _rules[kind][zone].dst;
        Color.RGBToHSV(_pickerOld, out _ph, out _ps, out _pv);
        _pickerHex = Core.ToHex(_pickerOld);
        _pickerDrag = 0;
        _pickerRect.x = Mathf.Clamp(_pickerRect.x, 0, Mathf.Max(0, Screen.width - _pickerRect.width));
        _pickerRect.y = Mathf.Clamp(_pickerRect.y, 0, Mathf.Max(0, Screen.height - _pickerRect.height));
        _pickerOpen = true;
    }

    Color32 PickerColor()
    {
        Color c = Color.HSVToRGB(_ph, _ps, _pv);
        return new Color32((byte)Mathf.RoundToInt(c.r * 255f), (byte)Mathf.RoundToInt(c.g * 255f), (byte)Mathf.RoundToInt(c.b * 255f), 255);
    }

    void EnsurePickerTextures()
    {
        const int n = 128;
        if (_hueTex == null)
        {
            _hueTex = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "RecolorHue" };
            for (int y = 0; y < n; y++) _hueTex.SetPixel(0, y, Color.HSVToRGB(y / (float)(n - 1), 1f, 1f));
            _hueTex.Apply(false);
            _owned.Add(_hueTex);
        }
        if (_svTex == null)
        {
            _svTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "RecolorSV" };
            _owned.Add(_svTex);
        }
        if (!Mathf.Approximately(_svTexHue, _ph))
        {
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = Color.HSVToRGB(_ph, x / (float)(n - 1), y / (float)(n - 1));
            _svTex.SetPixels32(px);
            _svTex.Apply(false);
            _svTexHue = _ph;
        }
    }

    void DrawPickerWindow()
    {
        _pickerRect = GUI.ModalWindow(PickerWinId, _pickerRect, DrawPicker,
            "Color nuevo · " + KindNames[_pickerKind] + " · Zona " + Core.ZoneNames[_pickerZone], WindowStyle);
    }

    void DrawPicker(int id)
    {
        EnsurePickerTextures();
        Event e = Event.current;
        Rect sv = new Rect(10, 26, 220, 220);
        Rect hue = new Rect(240, 26, 24, 220);

        GUI.DrawTexture(sv, _svTex, ScaleMode.StretchToFill, false);
        GUI.DrawTexture(hue, _hueTex, ScaleMode.StretchToFill, false);

        // markers
        Vector2 m = new Vector2(sv.x + _ps * sv.width, sv.yMax - _pv * sv.height);
        GUI.color = Color.black; DrawFrame(new Rect(m.x - 5, m.y - 5, 10, 10), 2);
        GUI.color = Color.white; DrawFrame(new Rect(m.x - 4, m.y - 4, 8, 8), 1);
        float hy = hue.yMax - _ph * hue.height;
        GUI.color = Color.black; GUI.DrawTexture(new Rect(hue.x - 3, hy - 2, hue.width + 6, 4), Texture2D.whiteTexture);
        GUI.color = Color.white; GUI.DrawTexture(new Rect(hue.x - 2, hy - 1, hue.width + 4, 2), Texture2D.whiteTexture);

        // Click sets the value; the drag is polled from Input on Layout (MouseDrag does not always arrive).
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (sv.Contains(e.mousePosition)) _pickerDrag = 1;
            else if (hue.Contains(e.mousePosition)) _pickerDrag = 2;
            if (_pickerDrag != 0)
            {
                GUI.FocusControl(null);
                PickerSetFrom(e.mousePosition, sv, hue);
                e.Use();
            }
        }
        else if (_pickerDrag != 0 && e.type == EventType.Layout)
        {
            if (Input.GetMouseButton(0)) PickerSetFrom(MouseGui(), sv, hue);
            else _pickerDrag = 0;
        }
        else if (_pickerDrag != 0 && (e.type == EventType.MouseDrag || e.type == EventType.MouseUp))
        {
            e.Use();
        }

        // right column: old/new, hex, RGB
        GUILayout.BeginArea(new Rect(276, 26, 134, 300));
        GUILayout.Label("Anterior / nuevo");
        Rect sw = GUILayoutUtility.GetRect(120, 40, GUILayout.Width(120), GUILayout.Height(40));
        ColorBox(new Rect(sw.x, sw.y, 60, 40), _pickerOld, false, 1);
        ColorBox(new Rect(sw.x + 60, sw.y, 60, 40), PickerColor(), false, 1);

        GUILayout.Label("HEX");
        string hex = GUILayout.TextField(_pickerHex, 7, GUILayout.Width(120));
        if (hex != _pickerHex)
        {
            _pickerHex = hex;
            if (Core.TryParseHex(hex, out Color32 parsed)) Color.RGBToHSV(parsed, out _ph, out _ps, out _pv);
        }

        Color32 cur = PickerColor();
        int r = RgbSlider("R", cur.r), g = RgbSlider("G", cur.g), b = RgbSlider("B", cur.b);
        if (r != cur.r || g != cur.g || b != cur.b)
        {
            Color.RGBToHSV(new Color32((byte)r, (byte)g, (byte)b, 255), out _ph, out _ps, out _pv);
            _pickerHex = Core.ToHex(PickerColor());
        }
        GUILayout.EndArea();

        GUILayout.BeginArea(new Rect(10, _pickerRect.height - 34, _pickerRect.width - 20, 28));
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Aceptar", GUILayout.Width(90)))
        {
            _rules[_pickerKind][_pickerZone].dst = PickerColor();
            _resultDirty = true;
            _pickerOpen = false;
        }
        if (GUILayout.Button("Cancelar", GUILayout.Width(90))) _pickerOpen = false;
        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { _pickerOpen = false; e.Use(); }
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    void PickerSetFrom(Vector2 p, Rect sv, Rect hue)
    {
        if (_pickerDrag == 1)
        {
            _ps = Mathf.Clamp01((p.x - sv.x) / sv.width);
            _pv = Mathf.Clamp01((sv.yMax - p.y) / sv.height);
        }
        else if (_pickerDrag == 2)
        {
            _ph = Mathf.Clamp01((hue.yMax - p.y) / hue.height);
        }
        _pickerHex = Core.ToHex(PickerColor());
    }

    static int RgbSlider(string label, byte value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(14));
        int v = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, 0f, 255f, GUILayout.Width(80)));
        GUILayout.Label(v.ToString(), GUILayout.Width(28));
        GUILayout.EndHorizontal();
        return v;
    }
}
