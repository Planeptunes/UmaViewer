// Herramienta local (no forma parte del visor publicado).
// BodyTextureRecolor: TripleMask editor. Paints directly on the _TripleMaskMap pixels (no zones, no rules). There is no
// palette: one colour button; pressing it turns on an eyedropper (in the same window) that takes the colour from the
// triple mask itself. The original texture is kept in memory (_orig / _src) so it can always be restored.
using System.Collections.Generic;
using UnityEngine;
using Core = BodyTextureRecolorCore;

public partial class BodyTextureRecolor
{
    const int TripleWinId = 0x52C4;
    const int TTBrush = 0, TTFill = 1;

    bool _tripleOpen, _tripleResizing, _tripleCancelArmed;
    Rect _tripleRect;
    Vector2 _pendingTripleSize;
    readonly ZoomView _tripleZoom = new ZoomView();

    bool _tripleEdited;                 // the committed edits differ from the original
    Texture2D _tripleView;              // = _result[KindTriple] while edited
    Color32[] _tripleCommitted;         // pixels of the accepted edits (null = original)

    Color32[] _tWork;                   // pixels being edited
    bool _tDirty;
    int _tTool = TTBrush;
    int _tBrush = 8;
    float _tTol = 12f;
    float _tOpacity;                    // original drawn over the canvas (0 = off)
    Color32 _tColor = new Color32(0, 0, 0, 255);
    bool _tPicking;                     // eyedropper active
    bool _tStroking;
    Vector2 _tLast;
    Vector2 _tHover = new Vector2(-1, -1);
    string _tInfo = "";

    Texture2D _tDisp;
    bool _tDispFull;
    int _tdMinX = int.MaxValue, _tdMinY = int.MaxValue, _tdMaxX = int.MinValue, _tdMaxY = int.MinValue;
    readonly List<Color32[]> _tUndo = new List<Color32[]>();

    void OpenTripleEditor()
    {
        Core.Source s = _src[KindTriple];
        if (!_initialized || s == null) { _status = "No hay TripleMask en M_Body."; return; }
        _tWork = (Color32[])(_tripleCommitted ?? s.px).Clone();
        _tDirty = false;
        _tripleCancelArmed = false;
        _tPicking = false;
        _tStroking = false;
        _tUndo.Clear();
        _tDispFull = true;
        _tripleZoom.fit = true;
        if (_tBrush <= 0 || _tBrush > Mathf.Max(s.w, s.h)) _tBrush = Mathf.Max(2, Mathf.Min(s.w, s.h) / 100);
        if (_tripleRect.width < 10) _tripleRect = DefaultBigRect();
        _pendingTripleSize = _tripleRect.size;
        _tripleOpen = true;
    }

    void CloseTripleEditor(bool accept)
    {
        if (accept && _tDirty) CommitTriple();
        _tWork = null;
        _tUndo.Clear();
        _tripleOpen = false;
    }

    void CommitTriple()
    {
        Core.Source s = _src[KindTriple];
        Texture2D t = _result[KindTriple];
        if (t == null || t.width != s.w || t.height != s.h)
        {
            t = NewTexture(s.w, s.h, s.srgb, _orig[KindTriple].name + "_painted", true);
            t.wrapMode = _orig[KindTriple].wrapMode;
            t.filterMode = _orig[KindTriple].filterMode;
            t.anisoLevel = _orig[KindTriple].anisoLevel;
            _result[KindTriple] = t;
        }
        t.SetPixels32(_tWork);
        t.Apply(true, false);   // stays readable (needed to save the PNG)
        _tripleView = t;
        _tripleCommitted = (Color32[])_tWork.Clone();
        _tripleEdited = true;
        _status = "TripleMask editada. Pulsa Aplicar para ponerla en el personaje.";
    }

    /// <summary>Drops the painted triple mask (the original is kept in memory, nothing else to reload).</summary>
    void DiscardTripleEdits()
    {
        _tripleEdited = false;
        _tripleCommitted = null;
        _tripleView = null;
        DropResult(KindTriple);
        if (_tripleOpen) { _tWork = null; _tripleOpen = false; _tUndo.Clear(); }
    }

    // ------------------------------------------------------------------ window
    void DrawTripleEditorWindow()
    {
        if (_pendingTripleSize.x > 0f) { _tripleRect.width = _pendingTripleSize.x; _tripleRect.height = _pendingTripleSize.y; }
        _tripleRect = GUI.Window(TripleWinId, _tripleRect, DrawTripleEditor,
            "Editor de TripleMask (se edita la textura directamente)", WindowStyle);
    }

    void DrawTripleEditor(int id)
    {
        Core.Source s = _src[KindTriple];
        if (s == null || _tWork == null) { _tripleOpen = false; return; }
        Event e = Event.current;
        int w = s.w, h = s.h;

        // ---- toolbar
        GUILayout.BeginArea(new Rect(6, 20, _tripleRect.width - 12, 70));
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(_tTool == TTBrush && !_tPicking, "Pincel (B)", GUI.skin.button, GUILayout.Width(80))) { _tTool = TTBrush; _tPicking = false; }
        if (GUILayout.Toggle(_tTool == TTFill && !_tPicking, "Cubo (G)", GUI.skin.button, GUILayout.Width(80))) { _tTool = TTFill; _tPicking = false; }
        GUILayout.Space(14);
        GUILayout.Label("Color", GUILayout.Width(38));
        Rect cb = GUILayoutUtility.GetRect(60, 24, GUILayout.Width(60), GUILayout.Height(24));
        Color32 shown = _tColor; shown.a = 255;
        if (ColorBox(cb, shown, _tPicking, 3)) _tPicking = !_tPicking;
        GUILayout.Label(_tPicking ? "Cuentagotas activo: clic en la textura" : Core.ToHex(_tColor) + " a" + _tColor.a, GUILayout.Width(210));
        GUILayout.Space(10);
        GUILayout.Label("Grosor", GUILayout.Width(44));
        int maxSize = Mathf.Max(64, Mathf.Min(w, h) / 6);
        _tBrush = Mathf.Clamp(Mathf.RoundToInt(GUILayout.HorizontalSlider(_tBrush, 1, maxSize, GUILayout.Width(140))), 1, maxSize);
        GUILayout.Label(_tBrush + " px", GUILayout.Width(56));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Cubo: tolerancia", GUILayout.Width(100));
        _tTol = Mathf.Round(GUILayout.HorizontalSlider(_tTol, 0f, 255f, GUILayout.Width(120)));
        GUILayout.Label(_tTol.ToString("F0"), GUILayout.Width(30));
        GUILayout.Space(14);
        GUILayout.Label("Original %", GUILayout.Width(66));
        _tOpacity = GUILayout.HorizontalSlider(_tOpacity, 0f, 1f, GUILayout.Width(120));
        GUILayout.Label(Mathf.RoundToInt(_tOpacity * 100f).ToString(), GUILayout.Width(30));
        GUILayout.EndHorizontal();
        GUILayout.Label("Botón de color / I: cuentagotas · Rueda: zoom · Botón central / Espacio+arrastrar: mover · [ ]: grosor · Ctrl+Z: deshacer · F: ajustar");
        GUILayout.EndArea();

        Rect area = new Rect(6, 92, _tripleRect.width - 12, _tripleRect.height - 92 - 36);
        _tripleZoom.Layout(area.size, w, h);

        // ---- keyboard
        if (e.type == EventType.KeyDown && GUIUtility.keyboardControl == 0)
        {
            bool used = true;
            switch (e.keyCode)
            {
                case KeyCode.B: _tTool = TTBrush; _tPicking = false; break;
                case KeyCode.G: _tTool = TTFill; _tPicking = false; break;
                case KeyCode.I: _tPicking = !_tPicking; break;
                case KeyCode.Escape: _tPicking = false; break;
                case KeyCode.LeftBracket: _tBrush = Mathf.Max(1, _tBrush - Mathf.Max(1, _tBrush / 7)); break;
                case KeyCode.RightBracket: _tBrush = _tBrush + Mathf.Max(1, _tBrush / 7); break;
                case KeyCode.F: _tripleZoom.Fit(area.size, w, h); break;
                case KeyCode.Z:
                    if (e.control || e.command) UndoTriple(); else used = false;
                    break;
                default: used = false; break;
            }
            if (used) e.Use();
        }

        // ---- canvas input (the stroke is polled from Input on Layout, like the mask editor)
        if (_tStroking && e.type == EventType.Layout)
        {
            Vector2 ipoll = _tripleZoom.ToImage(MouseGui() - area.position);
            if (Input.GetMouseButton(0)) TStrokeTo(new Vector2(ipoll.x, h - ipoll.y), w, h);
            else _tStroking = false;
        }

        if (!HandleViewInput(_tripleZoom, area))
        {
            Vector2 ip = _tripleZoom.ToImage(e.mousePosition - area.position);
            bool inImage = area.Contains(e.mousePosition) && ip.x >= 0 && ip.y >= 0 && ip.x < w && ip.y < h;
            if (e.type == EventType.Repaint || e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            {
                _tHover = area.Contains(e.mousePosition) ? ip : new Vector2(-1, -1);
                if (inImage)
                {
                    int hx = Mathf.FloorToInt(ip.x), hr = h - 1 - Mathf.FloorToInt(ip.y);
                    Color32 hc = _tWork[hr * w + hx];
                    _tInfo = "(" + hx + ", " + Mathf.FloorToInt(ip.y) + ") " + Core.ToHex(hc) + " a" + hc.a;
                }
                else _tInfo = "";
            }

            if (e.type == EventType.MouseDown && e.button == 0 && area.Contains(e.mousePosition))
            {
                GUI.FocusControl(null);
                if (_tPicking)
                {
                    if (inImage)
                    {
                        _tColor = _tWork[(h - 1 - Mathf.FloorToInt(ip.y)) * w + Mathf.FloorToInt(ip.x)];
                        _tPicking = false;
                    }
                }
                else if (_tTool == TTFill)
                {
                    if (inImage) TFillAt(Mathf.FloorToInt(ip.x), h - 1 - Mathf.FloorToInt(ip.y), w, h);
                }
                else
                {
                    PushTUndo();
                    _tStroking = true;
                    _tLast = new Vector2(ip.x, h - ip.y);
                    TDab(_tLast, w, h);
                }
                e.Use();
            }
            else if (_tStroking && (e.type == EventType.MouseDrag || e.type == EventType.MouseUp))
            {
                e.Use();
            }
        }

        // ---- canvas drawing
        if (e.type == EventType.Repaint) UpdateTripleDisplay(w, h);
        GUI.BeginGroup(area);
        GUI.color = new Color(0.17f, 0.17f, 0.17f);
        GUI.DrawTexture(new Rect(0, 0, area.width, area.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        Rect img = _tripleZoom.ImageRect(w, h);
        FilterMode fm = _tripleZoom.scale >= 1f ? FilterMode.Point : FilterMode.Bilinear;
        if (_tDisp != null)
        {
            _tDisp.filterMode = fm;
            GUI.DrawTexture(img, _tDisp, ScaleMode.StretchToFill, false);
        }
        Texture2D orig = _opaque[KindTriple];
        if (orig != null && _tOpacity > 0.001f)
        {
            orig.filterMode = fm;
            GUI.color = new Color(1f, 1f, 1f, _tOpacity);
            GUI.DrawTexture(img, orig, ScaleMode.StretchToFill, true);
            GUI.color = Color.white;
        }
        if (!_tPicking && _tTool == TTBrush && _tHover.x >= 0)
        {
            EnsureRing();
            Vector2 c = _tripleZoom.ToLocal(_tHover);
            float r = Mathf.Max(2f, _tBrush * 0.5f * _tripleZoom.scale);
            GUI.DrawTexture(new Rect(c.x - r, c.y - r, 2 * r, 2 * r), _ring, ScaleMode.StretchToFill, true);
        }
        GUI.EndGroup();

        // ---- bottom bar
        GUILayout.BeginArea(new Rect(6, _tripleRect.height - 32, _tripleRect.width - 30, 28));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Aceptar", GUILayout.Width(80))) { CloseTripleEditor(true); GUILayout.EndHorizontal(); GUILayout.EndArea(); return; }
        if (GUILayout.Button(_tripleCancelArmed ? "¿Descartar?" : "Cancelar", GUILayout.Width(90)))
        {
            if (!_tDirty || _tripleCancelArmed) { CloseTripleEditor(false); GUILayout.EndHorizontal(); GUILayout.EndArea(); return; }
            _tripleCancelArmed = true;
        }
        GUILayout.Space(10);
        GUI.enabled = _tUndo.Count > 0;
        if (GUILayout.Button("Deshacer", GUILayout.Width(80))) UndoTriple();
        GUI.enabled = true;
        if (GUILayout.Button("Restaurar original", GUILayout.Width(120)))
        {
            PushTUndo();
            System.Array.Copy(s.px, _tWork, s.px.Length);
            _tDirty = true;
            _tripleCancelArmed = false;
            _tDispFull = true;
        }
        GUILayout.Label(_tInfo + "  " + w + "×" + h, GUILayout.Width(250));
        GUILayout.FlexibleSpace();
        ZoomButtons(_tripleZoom, area.size, w, h);
        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        _pendingTripleSize = ResizeGrip(_tripleRect.size, new Vector2(760, 480), ref _tripleResizing);
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    // ------------------------------------------------------------------ painting
    void TMarkDirty(int x0, int y0, int x1, int y1)
    {
        _tDirty = true;
        _tripleCancelArmed = false;
        _tdMinX = Mathf.Min(_tdMinX, x0); _tdMinY = Mathf.Min(_tdMinY, y0);
        _tdMaxX = Mathf.Max(_tdMaxX, x1); _tdMaxY = Mathf.Max(_tdMaxY, y1);
    }

    /// <summary>Paints a hard disc of the brush size (the exact colour, alpha included) at texture coords (x, row).</summary>
    void TDab(Vector2 p, int w, int h)
    {
        float r = _tBrush * 0.5f;
        if (_tBrush <= 1)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            _tWork[y * w + x] = _tColor;
            TMarkDirty(x, y, x, y);
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
                if (dx * dx + dy * dy <= r2) _tWork[y * w + x] = _tColor;
            }
        }
        TMarkDirty(x0, y0, x1, y1);
    }

    void TStrokeTo(Vector2 p, int w, int h)
    {
        float d = Vector2.Distance(_tLast, p);
        if (d < 0.25f) return;
        float step = Mathf.Max(0.5f, _tBrush * 0.25f);
        int n = Mathf.Max(1, Mathf.CeilToInt(d / step));
        for (int i = 1; i <= n; i++) TDab(Vector2.Lerp(_tLast, p, i / (float)n), w, h);
        _tLast = p;
    }

    void TFillAt(int x, int row, int w, int h)
    {
        if (x < 0 || row < 0 || x >= w || row >= h) return;
        Color32 seed = _tWork[row * w + x];
        if (seed.r == _tColor.r && seed.g == _tColor.g && seed.b == _tColor.b && seed.a == _tColor.a) return;
        int tol = Mathf.RoundToInt(_tTol);
        var region = new bool[w * h];
        for (int i = 0; i < region.Length; i++)
        {
            Color32 c = _tWork[i];
            region[i] = Mathf.Abs(c.r - seed.r) <= tol && Mathf.Abs(c.g - seed.g) <= tol &&
                        Mathf.Abs(c.b - seed.b) <= tol && Mathf.Abs(c.a - seed.a) <= tol;
        }
        bool[] filled = Core.FloodFill(region, w, h, x, row);
        PushTUndo();
        for (int i = 0; i < filled.Length; i++) if (filled[i]) _tWork[i] = _tColor;
        _tDirty = true;
        _tripleCancelArmed = false;
        _tDispFull = true;
    }

    void PushTUndo()
    {
        _tUndo.Add((Color32[])_tWork.Clone());
        long total = (long)_tUndo.Count * _tWork.Length * 4;
        while (_tUndo.Count > 1 && total > UndoMaxBytes)
        {
            _tUndo.RemoveAt(0);
            total -= (long)_tWork.Length * 4;
        }
    }

    void UndoTriple()
    {
        if (_tUndo.Count == 0 || _tStroking) return;
        _tWork = _tUndo[_tUndo.Count - 1];
        _tUndo.RemoveAt(_tUndo.Count - 1);
        _tDirty = true;
        _tDispFull = true;
    }

    /// <summary>Display texture = the working pixels with alpha forced to 1 (only the dirty block is uploaded after a stroke).</summary>
    void UpdateTripleDisplay(int w, int h)
    {
        if (_tDisp == null || _tDisp.width != w || _tDisp.height != h)
        {
            _tDisp = NewTexture(w, h, _src[KindTriple].srgb, "RecolorTripleView", false);
            _tDisp.wrapMode = TextureWrapMode.Clamp;
            _tDispFull = true;
        }
        bool full = _tDispFull;
        if (!full && _tdMaxX < _tdMinX) return;

        int x0 = 0, y0 = 0, x1 = w - 1, y1 = h - 1;
        if (!full) { x0 = Mathf.Max(0, _tdMinX); y0 = Mathf.Max(0, _tdMinY); x1 = Mathf.Min(w - 1, _tdMaxX); y1 = Mathf.Min(h - 1, _tdMaxY); }
        int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
        var block = new Color32[bw * bh];
        for (int y = 0; y < bh; y++)
        {
            int src = (y0 + y) * w + x0, dst = y * bw;
            for (int x = 0; x < bw; x++)
            {
                Color32 p = _tWork[src + x];
                p.a = 255;
                block[dst + x] = p;
            }
        }
        _tDisp.SetPixels32(x0, y0, bw, bh, block);
        _tDisp.Apply(false, false);

        _tDispFull = false;
        _tdMinX = _tdMinY = int.MaxValue;
        _tdMaxX = _tdMaxY = int.MinValue;
    }
}
