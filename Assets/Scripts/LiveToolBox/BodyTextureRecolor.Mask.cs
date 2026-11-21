// Herramienta local (no forma parte del visor publicado).
// BodyTextureRecolor: mask editor. One canvas (layer) per zone, the size of the diffuse; the diffuse is drawn on
// top as a template. Brush, eraser, bucket (by mask or by texture colour), zoom and pan, undo.
using System.Collections.Generic;
using UnityEngine;
using Core = BodyTextureRecolorCore;

public partial class BodyTextureRecolor
{
    const int ToolBrush = 0, ToolEraser = 1, ToolFill = 2, ToolWand = 3;
    const long UndoMaxBytes = 512L * 1024 * 1024;

    bool _maskOpen, _maskResizing, _maskCancelArmed;
    Rect _maskRect;
    Vector2 _pendingMaskSize;
    readonly ZoomView _maskView = new ZoomView();

    byte[][] _edit = new byte[Core.ZoneCount][];
    readonly bool[] _editUsed = new bool[Core.ZoneCount];
    int _editZone;
    bool _editDirty;

    int _tool = ToolBrush;
    int _brushSize = 8;
    bool _fillByTexture;
    float _fillTol = 24f;
    float _templateOpacity = 0.5f;

    Texture2D _maskDisp, _ring;
    Color32[] _maskDispPx;
    int _maskDispZone = -1;
    bool _dispFull;
    int _dMinX = int.MaxValue, _dMinY = int.MaxValue, _dMaxX = int.MinValue, _dMaxY = int.MinValue;   // dirty box of the display (texture rows)

    readonly List<KeyValuePair<int, byte[]>> _undo = new List<KeyValuePair<int, byte[]>>();
    bool _stroking;
    Vector2 _lastPt;          // texture coords (x, row)
    Vector2 _maskHover = new Vector2(-1, -1);   // image coords (top-down), for the brush cursor
    string _maskPos = "";

    void OpenMaskEditor()
    {
        if (!_initialized) return;
        for (int z = 0; z < Core.ZoneCount; z++)
        {
            _edit[z] = _layers[z] != null ? (byte[])_layers[z].Clone() : null;
            _editUsed[z] = _layerUsed[z];
        }
        _editZone = _zone;
        _editDirty = false;
        _maskCancelArmed = false;
        _undo.Clear();
        _stroking = false;
        _maskDispZone = -1;
        _maskView.fit = true;
        if (_brushSize <= 0 || _brushSize > Mathf.Max(_maskW, _maskH)) _brushSize = Mathf.Max(2, Mathf.Min(_maskW, _maskH) / 100);
        if (_maskRect.width < 10) _maskRect = DefaultBigRect();
        _pendingMaskSize = _maskRect.size;
        _maskOpen = true;
    }

    void CloseMaskEditor(bool accept)
    {
        if (accept)
        {
            for (int z = 0; z < Core.ZoneCount; z++) _layers[z] = _editUsed[z] ? _edit[z] : null;
            RefreshLayerUsed();
            _resultDirty = true;
            _status = "Máscara aplicada. Pulsa Vista previa.";
        }
        _edit = new byte[Core.ZoneCount][];
        _undo.Clear();
        _maskOpen = false;
    }

    void DrawMaskEditorWindow()
    {
        if (_pendingMaskSize.x > 0f) { _maskRect.width = _pendingMaskSize.x; _maskRect.height = _pendingMaskSize.y; }
        _maskRect = GUI.Window(MaskWinId, _maskRect, DrawMaskEditor,
            "Editor de máscara · Zona " + Core.ZoneNames[_editZone] + " (cada zona tiene su propio lienzo)", WindowStyle);
    }

    void DrawMaskEditor(int id)
    {
        Event e = Event.current;
        int w = _maskW, h = _maskH;

        // ---- toolbar
        GUILayout.BeginArea(new Rect(6, 20, _maskRect.width - 12, 70));
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(_tool == ToolBrush, "Pincel (B)", GUI.skin.button, GUILayout.Width(80))) _tool = ToolBrush;
        if (GUILayout.Toggle(_tool == ToolEraser, "Borrador (E)", GUI.skin.button, GUILayout.Width(90))) _tool = ToolEraser;
        if (GUILayout.Toggle(_tool == ToolFill, "Cubo (G)", GUI.skin.button, GUILayout.Width(80))) _tool = ToolFill;
        if (GUILayout.Toggle(_tool == ToolWand, "Varita (W)", GUI.skin.button, GUILayout.Width(86))) _tool = ToolWand;
        GUILayout.Space(14);
        GUILayout.Label("Zona", GUILayout.Width(34));
        for (int z = 0; z < Core.ZoneCount; z++)
        {
            Rect r = GUILayoutUtility.GetRect(40, 22, GUILayout.Width(40), GUILayout.Height(22));
            if (ColorBox(r, Core.ZoneColors[z], z == _editZone, 3, _editUsed[z] ? "●" : null)) SelectEditZone(z);
        }
        GUILayout.Space(14);
        GUILayout.Label("Grosor", GUILayout.Width(44));
        int maxSize = Mathf.Max(64, Mathf.Min(w, h) / 6);
        _brushSize = Mathf.Clamp(Mathf.RoundToInt(GUILayout.HorizontalSlider(_brushSize, 1, maxSize, GUILayout.Width(160))), 1, maxSize);
        GUILayout.Label(_brushSize + " px", GUILayout.Width(56));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Cubo: rellenar según", GUILayout.Width(130));
        if (GUILayout.Toggle(!_fillByTexture, "Máscara", GUI.skin.button, GUILayout.Width(70))) _fillByTexture = false;
        if (GUILayout.Toggle(_fillByTexture, "Textura", GUI.skin.button, GUILayout.Width(70))) _fillByTexture = true;
        GUILayout.Label("Tolerancia", GUILayout.Width(66));
        _fillTol = Mathf.Round(GUILayout.HorizontalSlider(_fillTol, 0f, 255f, GUILayout.Width(120)));
        GUILayout.Label(_fillTol.ToString("F0"), GUILayout.Width(30));
        GUILayout.Space(14);
        GUILayout.Label("Plantilla %", GUILayout.Width(70));
        _templateOpacity = GUILayout.HorizontalSlider(_templateOpacity, 0f, 1f, GUILayout.Width(120));
        GUILayout.Label(Mathf.RoundToInt(_templateOpacity * 100f).ToString(), GUILayout.Width(30));
        GUILayout.EndHorizontal();
        GUILayout.Label("Varita: el grosor es la diferencia de color admitida (1 = idéntico) · 1-5: zona · Rueda: zoom · Botón central / Espacio+arrastrar: mover · [ ]: grosor · Ctrl+Z: deshacer · F: ajustar");
        GUILayout.EndArea();

        Rect area = new Rect(6, 92, _maskRect.width - 12, _maskRect.height - 92 - 36);
        _maskView.Layout(area.size, w, h);

        // ---- keyboard
        if (e.type == EventType.KeyDown && GUIUtility.keyboardControl == 0)
        {
            bool used = true;
            switch (e.keyCode)
            {
                case KeyCode.B: _tool = ToolBrush; break;
                case KeyCode.E: _tool = ToolEraser; break;
                case KeyCode.G: _tool = ToolFill; break;
                case KeyCode.W: _tool = ToolWand; break;
                case KeyCode.Alpha1: case KeyCode.Keypad1: SelectEditZone(0); break;
                case KeyCode.Alpha2: case KeyCode.Keypad2: SelectEditZone(1); break;
                case KeyCode.Alpha3: case KeyCode.Keypad3: SelectEditZone(2); break;
                case KeyCode.Alpha4: case KeyCode.Keypad4: SelectEditZone(3); break;
                case KeyCode.Alpha5: case KeyCode.Keypad5: SelectEditZone(4); break;
                case KeyCode.LeftBracket: _brushSize = Mathf.Max(1, _brushSize - Mathf.Max(1, _brushSize / 7)); break;
                case KeyCode.RightBracket: _brushSize = _brushSize + Mathf.Max(1, _brushSize / 7); break;
                case KeyCode.F: _maskView.Fit(area.size, w, h); break;
                case KeyCode.Z:
                    if (e.control || e.command) UndoMask(); else used = false;
                    break;
                default: used = false; break;
            }
            if (used) e.Use();
        }

        // ---- canvas input
        // The stroke is polled from Input on every Layout event: the window does not always receive
        // MouseDrag/MouseUp after the first click, which left only a dot.
        if (_stroking && e.type == EventType.Layout)
        {
            Vector2 ipoll = _maskView.ToImage(MouseGui() - area.position);
            if (Input.GetMouseButton(0)) StrokeTo(new Vector2(ipoll.x, h - ipoll.y), _tool == ToolBrush ? (byte)255 : (byte)0);
            else EndStroke();
        }

        if (!HandleViewInput(_maskView, area))
        {
            Vector2 local = e.mousePosition - area.position;
            Vector2 ip = _maskView.ToImage(local);
            if (e.type == EventType.Repaint || e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            {
                _maskHover = area.Contains(e.mousePosition) ? ip : new Vector2(-1, -1);
                _maskPos = ip.x >= 0 && ip.y >= 0 && ip.x < w && ip.y < h ? "(" + (int)ip.x + ", " + (int)ip.y + ")" : "";
            }
            Vector2 tp = new Vector2(ip.x, h - ip.y);   // texture coords: row 0 = bottom

            if (e.type == EventType.MouseDown && e.button == 0 && area.Contains(e.mousePosition))
            {
                GUI.FocusControl(null);
                if (_tool == ToolFill)
                {
                    FillAt(Mathf.FloorToInt(tp.x), Mathf.FloorToInt(tp.y));
                }
                else if (_tool == ToolWand)
                {
                    WandAt(Mathf.FloorToInt(tp.x), Mathf.FloorToInt(tp.y));
                }
                else
                {
                    PushUndo(_editZone);
                    _stroking = true;
                    _lastPt = tp;
                    Dab(tp, _tool == ToolBrush ? (byte)255 : (byte)0);
                }
                e.Use();
            }
            else if (_stroking && (e.type == EventType.MouseDrag || e.type == EventType.MouseUp))
            {
                e.Use();   // handled by the Layout poll above
            }
        }

        // ---- canvas drawing
        if (e.type == EventType.Repaint) UpdateMaskDisplay();
        GUI.BeginGroup(area);
        GUI.color = new Color(0.17f, 0.17f, 0.17f);
        GUI.DrawTexture(new Rect(0, 0, area.width, area.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        Rect img = _maskView.ImageRect(w, h);
        FilterMode fm = _maskView.scale >= 1f ? FilterMode.Point : FilterMode.Bilinear;
        if (_maskDisp != null)
        {
            _maskDisp.filterMode = fm;
            GUI.DrawTexture(img, _maskDisp, ScaleMode.StretchToFill, false);
        }
        Texture2D tpl = _opaque[TemplateKind()];
        if (tpl != null && _templateOpacity > 0.001f)
        {
            tpl.filterMode = fm;
            GUI.color = new Color(1f, 1f, 1f, _templateOpacity);
            GUI.DrawTexture(img, tpl, ScaleMode.StretchToFill, true);
            GUI.color = Color.white;
        }
        if (_tool != ToolFill && _tool != ToolWand && _maskHover.x >= 0)
        {
            EnsureRing();
            Vector2 c = _maskView.ToLocal(_maskHover);
            float r = Mathf.Max(2f, _brushSize * 0.5f * _maskView.scale);
            GUI.DrawTexture(new Rect(c.x - r, c.y - r, 2 * r, 2 * r), _ring, ScaleMode.StretchToFill, true);
        }
        GUI.EndGroup();

        // ---- bottom bar
        GUILayout.BeginArea(new Rect(6, _maskRect.height - 32, _maskRect.width - 30, 28));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Aceptar", GUILayout.Width(80))) { CloseMaskEditor(true); GUILayout.EndHorizontal(); GUILayout.EndArea(); return; }
        if (GUILayout.Button(_maskCancelArmed ? "¿Descartar?" : "Cancelar", GUILayout.Width(90)))
        {
            if (!_editDirty || _maskCancelArmed) { CloseMaskEditor(false); GUILayout.EndHorizontal(); GUILayout.EndArea(); return; }
            _maskCancelArmed = true;
        }
        GUILayout.Space(10);
        GUI.enabled = _undo.Count > 0;
        if (GUILayout.Button("Deshacer", GUILayout.Width(80))) UndoMask();
        GUI.enabled = _editUsed[_editZone];
        if (GUILayout.Button("Limpiar zona", GUILayout.Width(100)))
        {
            PushUndo(_editZone);
            _edit[_editZone] = null;
            _editUsed[_editZone] = false;
            _dispFull = true;
        }
        GUI.enabled = !_stroking;
        if (GUILayout.Button("Cargar Mask", GUILayout.Width(100))) _pendingMaskLoad = true;   // dialog opened from Update
        GUI.enabled = true;
        GUILayout.Label(_maskPos + "  " + w + "×" + h, GUILayout.Width(180));
        GUILayout.FlexibleSpace();
        ZoomButtons(_maskView, area.size, w, h);
        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        _pendingMaskSize = ResizeGrip(_maskRect.size, new Vector2(760, 480), ref _maskResizing);
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    /// <summary>The map shown under the mask canvas: the one being edited (toon or diffuse).</summary>
    int TemplateKind()
    {
        return _kind == KindToon && _opaque[KindToon] != null && _src[KindToon] != null ? KindToon : KindDiffuse;
    }

    static int TemplateIndex(Core.Source s, int x, int y, int w, int h)
    {
        if (s.w == w && s.h == h) return y * w + x;
        return Mathf.Min(s.h - 1, (int)((long)y * s.h / h)) * s.w + Mathf.Min(s.w - 1, (int)((long)x * s.w / w));
    }

    void SelectEditZone(int z)
    {
        if (_stroking || z == _editZone) return;
        _editZone = z;
        _zone = z;
        _maskDispZone = -1;   // rebuild the display with this zone's canvas
    }

    byte[] EditLayer(int z)
    {
        if (_edit[z] == null) _edit[z] = new byte[_maskW * _maskH];
        return _edit[z];
    }

    void MarkDirty(int x0, int y0, int x1, int y1)
    {
        _editDirty = true;
        _maskCancelArmed = false;
        _dMinX = Mathf.Min(_dMinX, x0); _dMinY = Mathf.Min(_dMinY, y0);
        _dMaxX = Mathf.Max(_dMaxX, x1); _dMaxY = Mathf.Max(_dMaxY, y1);
    }

    /// <summary>Paints a disc of the brush size at texture coords (x, row).</summary>
    void Dab(Vector2 p, byte value)
    {
        int w = _maskW, h = _maskH;
        byte[] layer = EditLayer(_editZone);
        float r = _brushSize * 0.5f;
        if (_brushSize <= 1)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            layer[y * w + x] = value;
            MarkDirty(x, y, x, y);
            return;
        }
        int x0 = Mathf.Max(0, Mathf.FloorToInt(p.x - r)), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(p.x + r));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(p.y - r)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(p.y + r));
        if (x0 > x1 || y0 > y1) return;
        float r2 = r * r;
        for (int y = y0; y <= y1; y++)
        {
            float dy = y + 0.5f - p.y;
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - p.x;
                if (dx * dx + dy * dy <= r2) layer[y * w + x] = value;
            }
        }
        MarkDirty(x0, y0, x1, y1);
    }

    void EndStroke()
    {
        _stroking = false;
        _editUsed[_editZone] = Core.LayerUsed(_edit[_editZone]);
    }

    void StrokeTo(Vector2 p, byte value)
    {
        float d = Vector2.Distance(_lastPt, p);
        if (d < 0.25f) return;   // mouse still: nothing new to paint
        float step = Mathf.Max(0.5f, _brushSize * 0.25f);
        int n = Mathf.Max(1, Mathf.CeilToInt(d / step));
        for (int i = 1; i <= n; i++) Dab(Vector2.Lerp(_lastPt, p, i / (float)n), value);
        _lastPt = p;
    }

    void FillAt(int x, int y)
    {
        int w = _maskW, h = _maskH;
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        byte[] layer = _edit[_editZone];
        var region = new bool[w * h];
        if (_fillByTexture)
        {
            Core.Source ts = _src[TemplateKind()];
            Color32[] px = ts.px;
            bool same = ts.w == w && ts.h == h;   // the toon can have another size than the mask
            Color32 seed = px[TemplateIndex(ts, x, y, w, h)];
            int tol = Mathf.RoundToInt(_fillTol);
            for (int i = 0; i < region.Length; i++)
            {
                Color32 c = px[same ? i : TemplateIndex(ts, i % w, i / w, w, h)];
                region[i] = Mathf.Abs(c.r - seed.r) <= tol && Mathf.Abs(c.g - seed.g) <= tol && Mathf.Abs(c.b - seed.b) <= tol;
            }
        }
        else
        {
            byte seed = layer != null ? layer[y * w + x] : (byte)0;
            for (int i = 0; i < region.Length; i++) region[i] = (layer != null ? layer[i] : (byte)0) == seed;
        }

        bool[] filled = Core.FloodFill(region, w, h, x, y);
        bool any = false;
        for (int i = 0; i < filled.Length && !any; i++) if (filled[i] && (layer == null || layer[i] == 0)) any = true;
        if (!any) return;

        PushUndo(_editZone);
        layer = EditLayer(_editZone);
        for (int i = 0; i < filled.Length; i++) if (filled[i]) layer[i] = 255;
        _editUsed[_editZone] = true;
        _editDirty = true;
        _maskCancelArmed = false;
        _dispFull = true;
    }

    /// <summary>
    /// Magic wand: paints, in the active zone's canvas, every pixel connected to (x, row) whose colour in the template
    /// (the map under the canvas) is within the allowed difference of the clicked pixel. The brush size is that
    /// difference: 1 = identical colour, N = total difference |dR|+|dG|+|dB| up to N-1. Unlimited reach. The template
    /// is never modified.
    /// </summary>
    void WandAt(int x, int y)
    {
        int w = _maskW, h = _maskH;
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        Core.Source ts = _src[TemplateKind()];
        Color32[] px = ts.px;
        bool same = ts.w == w && ts.h == h;
        Color32 seed = px[TemplateIndex(ts, x, y, w, h)];
        int tol = Mathf.Max(0, _brushSize - 1);
        var region = new bool[w * h];
        for (int i = 0; i < region.Length; i++)
        {
            Color32 c = px[same ? i : TemplateIndex(ts, i % w, i / w, w, h)];
            region[i] = Mathf.Abs(c.r - seed.r) + Mathf.Abs(c.g - seed.g) + Mathf.Abs(c.b - seed.b) <= tol;
        }

        bool[] filled = Core.FloodFill(region, w, h, x, y);
        byte[] layer = _edit[_editZone];
        bool any = false;
        for (int i = 0; i < filled.Length && !any; i++) if (filled[i] && (layer == null || layer[i] == 0)) any = true;
        if (!any) return;

        PushUndo(_editZone);
        layer = EditLayer(_editZone);
        for (int i = 0; i < filled.Length; i++) if (filled[i]) layer[i] = 255;
        _editUsed[_editZone] = true;
        _editDirty = true;
        _maskCancelArmed = false;
        _dispFull = true;
    }

    void PushUndo(int zone)
    {
        _undo.Add(new KeyValuePair<int, byte[]>(zone, _edit[zone] != null ? (byte[])_edit[zone].Clone() : null));
        long total = 0;
        foreach (var u in _undo) total += u.Value != null ? u.Value.Length : 0;
        while (_undo.Count > 1 && total > UndoMaxBytes)
        {
            total -= _undo[0].Value != null ? _undo[0].Value.Length : 0;
            _undo.RemoveAt(0);
        }
    }

    /// <summary>Undoes the last change, of whichever zone, and shows that zone's canvas.</summary>
    void UndoMask()
    {
        if (_undo.Count == 0 || _stroking) return;
        var last = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);
        _edit[last.Key] = last.Value;
        _editUsed[last.Key] = Core.LayerUsed(last.Value);
        _editDirty = true;
        if (last.Key != _editZone) SelectEditZone(last.Key);
        _dispFull = true;
    }

    /// <summary>Display texture of the active zone's canvas: zone colour where painted, white elsewhere.</summary>
    void UpdateMaskDisplay()
    {
        int w = _maskW, h = _maskH;
        if (_maskDisp == null || _maskDisp.width != w || _maskDisp.height != h)
        {
            _maskDisp = NewTexture(w, h, true, "RecolorMaskView", false);
            _maskDisp.wrapMode = TextureWrapMode.Clamp;
            _maskDispPx = new Color32[w * h];
            _maskDispZone = -1;
        }

        bool full = _dispFull || _maskDispZone != _editZone;
        if (!full && _dMaxX < _dMinX) return;

        int x0 = 0, y0 = 0, x1 = w - 1, y1 = h - 1;
        if (!full) { x0 = Mathf.Max(0, _dMinX); y0 = Mathf.Max(0, _dMinY); x1 = Mathf.Min(w - 1, _dMaxX); y1 = Mathf.Min(h - 1, _dMaxY); }

        byte[] layer = _edit[_editZone];
        Color32 on = Core.ZoneColors[_editZone], off = new Color32(255, 255, 255, 255);
        for (int y = y0; y <= y1; y++)
        {
            int row = y * w;
            for (int x = x0; x <= x1; x++)
                _maskDispPx[row + x] = layer != null && layer[row + x] != 0 ? on : off;
        }
        _maskDisp.SetPixels32(_maskDispPx);
        _maskDisp.Apply(false, false);

        _maskDispZone = _editZone;
        _dispFull = false;
        _dMinX = _dMinY = int.MaxValue;
        _dMaxX = _dMaxY = int.MinValue;
    }

    void EnsureRing()
    {
        if (_ring != null) return;
        const int n = 64;
        _ring = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "RecolorBrushRing" };
        var px = new Color32[n * n];
        float c = (n - 1) * 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                if (d > c - 1.2f && d <= c) px[y * n + x] = new Color32(0, 0, 0, 255);
                else if (d > c - 2.6f && d <= c - 1.2f) px[y * n + x] = new Color32(255, 255, 255, 255);
                else px[y * n + x] = new Color32(0, 0, 0, 0);
            }
        _ring.SetPixels32(px);
        _ring.Apply(false);
        _owned.Add(_ring);
    }
}
