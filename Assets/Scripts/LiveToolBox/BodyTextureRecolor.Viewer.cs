// Herramienta local (no forma parte del visor publicado).
// BodyTextureRecolor: read-only enlarged view of a preview (diffuse / toon / triple mask) with zoom and pan.
using UnityEngine;

public partial class BodyTextureRecolor
{
    const int ViewWinId = 0x52C5;

    bool _viewOpen, _viewResizing;
    Rect _viewRect;
    Vector2 _pendingViewSize;
    int _viewKind;
    readonly ZoomView _viewZoom = new ZoomView();

    /// <summary>The texture a preview slot shows right now (recoloured result when there is one, else the original).</summary>
    Texture PreviewTexture(int k)
    {
        if (k == KindTriple) return _tripleEdited && _tripleView != null ? (Texture)_tripleView : _opaque[k];
        return _result[k] != null ? (Texture)_result[k] : _opaque[k];
    }

    void OpenViewer(int kind)
    {
        if (PreviewTexture(kind) == null) return;
        _viewKind = kind;
        _viewZoom.fit = true;
        if (_viewRect.width < 10) _viewRect = DefaultBigRect();
        _pendingViewSize = _viewRect.size;
        _viewOpen = true;
    }

    void DrawViewerWindow()
    {
        if (_pendingViewSize.x > 0f) { _viewRect.width = _pendingViewSize.x; _viewRect.height = _pendingViewSize.y; }
        _viewRect = GUI.ModalWindow(ViewWinId, _viewRect, DrawViewer, "Vista ampliada · " + KindNames[_viewKind] + " (sólo lectura)", WindowStyle);
    }

    void DrawViewer(int id)
    {
        Texture tex = PreviewTexture(_viewKind);
        if (tex == null) { _viewOpen = false; return; }
        Event e = Event.current;
        int w = tex.width, h = tex.height;

        GUI.Label(new Rect(6, 20, _viewRect.width - 12, 20), "Rueda: zoom · Botón central / Espacio+arrastrar: mover · F: ajustar · Esc: cerrar");
        Rect area = new Rect(6, 42, _viewRect.width - 12, _viewRect.height - 42 - 34);
        _viewZoom.Layout(area.size, w, h);

        if (!HandleViewInput(_viewZoom, area) && e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Escape) { _viewOpen = false; e.Use(); return; }
            if (e.keyCode == KeyCode.F) { _viewZoom.Fit(area.size, w, h); e.Use(); }
        }

        GUI.BeginGroup(area);
        GUI.color = new Color(0.17f, 0.17f, 0.17f);
        GUI.DrawTexture(new Rect(0, 0, area.width, area.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        tex.filterMode = _viewZoom.scale >= 1f ? FilterMode.Point : FilterMode.Bilinear;
        GUI.DrawTexture(_viewZoom.ImageRect(w, h), tex, ScaleMode.StretchToFill, false);
        GUI.EndGroup();

        GUILayout.BeginArea(new Rect(6, _viewRect.height - 30, _viewRect.width - 30, 26));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Cerrar", GUILayout.Width(80))) { _viewOpen = false; GUILayout.EndHorizontal(); GUILayout.EndArea(); return; }
        GUILayout.Label(w + "×" + h, GUILayout.Width(100));
        GUILayout.FlexibleSpace();
        ZoomButtons(_viewZoom, area.size, w, h);
        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        _pendingViewSize = ResizeGrip(_viewRect.size, new Vector2(500, 360), ref _viewResizing);
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }
}
