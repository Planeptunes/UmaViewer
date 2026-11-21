"""
recolor_swimsuit_gui.py
Graphical tool: replace colours in a texture by other colours, keeping the shading (shadows and folds).

Zones: there are 5 zones (black, red, yellow, green, blue). Every zone has its own mask layer and its own
rule "colour to replace -> new colour", applied only inside the area painted in that zone's layer.
Unpainted = untouched. Layers may overlap: where several zones select the same pixel, the later zone
(right-most column) wins. Without any mask, only the black rule is applied to the whole texture.

Rule modes:
  - Tono: pixels whose hue is close to the colour to replace take the hue and saturation of the new colour;
    their brightness is kept.
  - Completo: pixels whose RGB, brought to the brightness of the colour to replace (so its shadows and
    highlights match too), is close to it are shifted so that the colour to replace
    becomes exactly the new colour (hue, saturation and brightness); shading is kept relatively. Works for
    white, grey and black, and for changes of brightness (e.g. yellow -> black, white -> brown).

  - Colour boxes: left click opens the eyedropper over the input texture (left click previews,
    right click accepts); right click opens the system colour picker.
  - Mask editor ("Crear..."): one canvas per zone, white with the input texture at 50% opacity as a template.
    Brush, eraser, bucket, zoom and pan. The masks live in memory and have the size of the texture.

Run:
  python tools/recolor_swimsuit_gui.py

Requires: pip install pillow numpy   (tkinter and colorchooser ship with the standard Python installer)
"""
import colorsys
import math
import os
import re
import tkinter as tk
from tkinter import colorchooser, filedialog, messagebox, ttk

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageTk

PREVIEW_MAX = 360
HEX_RE = re.compile(r"^#?([0-9a-fA-F]{6})$")
RESAMPLE = getattr(Image, "Resampling", Image)
UNDO_MAX_BYTES = 512 * 1024 * 1024
# (name, display colour). The first zone is the one applied to the whole texture when there is no mask.
ZONES = [
    ("Negro", (0, 0, 0)),
    ("Rojo", (237, 28, 36)),
    ("Amarillo", (255, 242, 0)),
    ("Verde", (34, 177, 76)),
    ("Azul", (63, 72, 204)),
]
MODE_HUE = "Tono"
MODE_FULL = "Completo"
DEFAULT_SRC_HEX = "#8BD8A0"
DEFAULT_DST_HEX = "#3A7BD5"
DEFAULT_PARAMS = {"tol_deg": 30.0, "min_sat": 0.05, "min_val": 0.05, "rgb_tol": 60.0}
# (key, label, min, max, step) of the "Qué píxeles cambian" sliders, and the modes each one affects.
PARAM_SLIDERS = [
    ("tol_deg", "Tolerancia de tono (°)", 0, 180, 1),
    ("min_sat", "Saturación mínima (0-1)", 0.0, 1.0, 0.01),
    ("min_val", "Brillo mínimo (0-1)", 0.0, 1.0, 0.01),
    ("rgb_tol", "Tolerancia de color (RGB)", 0, 442, 1),
]
PARAM_MODES = {
    "tol_deg": ("Tono",),
    "min_sat": ("Tono",),
    "min_val": ("Tono", "Completo"),
    "rgb_tol": ("Completo",),
}


def hex_to_rgb(text):
    m = HEX_RE.match(text.strip())
    if not m:
        raise ValueError(f"Color no válido: '{text}'. Usa formato #RRGGBB.")
    h = m.group(1)
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def rgb_to_hex(rgb):
    return "#%02X%02X%02X" % tuple(int(round(c)) for c in rgb)


def layer_used(layer):
    """True when a zone layer ("L" image, 255 = painted) has at least one painted pixel."""
    return layer is not None and layer.getbbox() is not None


def zones_used(masks):
    """Indices of the zones whose layer has painted pixels."""
    if not masks:
        return []
    return [i for i, layer in enumerate(masks) if layer_used(layer)]


def recolor(src_img, rules, masks=None):
    """
    rules: list of (src_rgb, dst_rgb, mode, params), one per zone (same order as ZONES); params is a dict with
    the zone's own thresholds: tol_deg, min_sat, min_val, rgb_tol.
    masks: list of "L" images (255 = painted), one per zone, or None.
    Each rule only acts inside its zone layer. Without any painted layer, only the first rule is applied,
    to the whole texture. All rules read the original pixels; where zones overlap the later one wins.
    Returns (image, changed_count).
    """
    src = src_img.convert("RGBA")
    w, h_px = src.size
    rgba = np.array(src)
    alpha = rgba[..., 3]
    rgb = rgba[..., :3].astype(np.float32)

    hsv = np.array(src.convert("RGB").convert("HSV")).astype(np.float32)
    hue = hsv[..., 0] / 255.0 * 360.0
    sat = hsv[..., 1] / 255.0
    val = hsv[..., 2] / 255.0

    valid = alpha > 127

    used = zones_used(masks)
    if used:
        jobs = []
        for i in used:
            layer = masks[i]
            if layer.size != (w, h_px):
                layer = layer.resize((w, h_px), RESAMPLE.NEAREST)
            jobs.append((rules[i], np.array(layer) > 127))
    else:
        jobs = [(rules[0], True)]

    new_h = hue.copy()
    new_s = sat.copy()
    new_v = val.copy()
    changed = np.zeros_like(valid)
    for (src_rgb, dst_rgb, mode, params), area in jobs:
        tol_deg, min_sat = params["tol_deg"], params["min_sat"]
        min_val, rgb_tol = params["min_val"], params["rgb_tol"]
        sh, ss, sv = colorsys.rgb_to_hsv(*[c / 255.0 for c in src_rgb])
        dh, ds, dv = colorsys.rgb_to_hsv(*[c / 255.0 for c in dst_rgb])
        if mode == MODE_FULL:
            target = np.array(src_rgb, np.float32)
            if sv > 1e-6:
                # Shading-invariant match: bring every pixel to the brightness of the colour to replace
                # before measuring the RGB distance, so shadows and highlights of that colour are selected.
                scale = np.where(val > 1e-6, sv / np.maximum(val, 1e-6), 0.0)
                dist = np.sqrt(((rgb * scale[..., None] - target) ** 2).sum(axis=-1))
                sel = valid & area & (val >= min_val) & (dist <= rgb_tol)
            else:
                dist = np.sqrt(((rgb - target) ** 2).sum(axis=-1))
                sel = valid & area & (dist <= rgb_tol)
            new_h[sel] = dh * 360.0
            new_s[sel] = np.clip(sat[sel] + (ds - ss), 0.0, 1.0)
            if sv > 1e-6:
                new_v[sel] = np.clip(val[sel] * (dv / sv), 0.0, 1.0)
            else:
                new_v[sel] = np.clip(val[sel] + dv, 0.0, 1.0)
        else:
            d = np.abs(hue - sh * 360.0)
            d = np.minimum(d, 360.0 - d)
            sel = valid & area & (sat >= min_sat) & (val >= min_val) & (d <= tol_deg)
            new_h[sel] = dh * 360.0
            new_s[sel] = ds
            new_v[sel] = val[sel]
        changed |= sel

    r, g, b = hsv_to_rgb(new_h / 360.0, new_s, new_v)
    out = np.stack([r * 255, g * 255, b * 255, alpha.astype(np.float32)], axis=-1)
    out = np.clip(np.round(out), 0, 255).astype(np.uint8)
    # Untouched pixels keep their exact original value (the 8-bit HSV round trip is lossy).
    out = np.where(changed[..., None], out, rgba)
    return Image.fromarray(out, "RGBA"), int(changed.sum())


def hsv_to_rgb(h, s, v):
    i = np.floor(h * 6).astype(int) % 6
    f = h * 6 - np.floor(h * 6)
    p = v * (1 - s)
    q = v * (1 - f * s)
    t = v * (1 - (1 - f) * s)
    conds = [i == 0, i == 1, i == 2, i == 3, i == 4, i == 5]
    r = np.select(conds, [v, q, p, p, t, v])
    g = np.select(conds, [t, v, v, q, p, p])
    b = np.select(conds, [p, p, t, v, v, q])
    return r, g, b


def flood_region(region, x, y):
    """Scanline flood fill (4-connected) over a boolean array. Returns the filled boolean array."""
    h, w = region.shape
    out = np.zeros_like(region)
    if not region[y, x]:
        return out
    todo = region.copy()
    stack = [(x, y)]
    while stack:
        sx, sy = stack.pop()
        row = todo[sy]
        if not row[sx]:
            continue
        blocked = np.flatnonzero(~row[:sx])
        left = int(blocked[-1]) + 1 if blocked.size else 0
        blocked = np.flatnonzero(~row[sx:])
        right = sx + int(blocked[0]) - 1 if blocked.size else w - 1
        out[sy, left:right + 1] = True
        row[left:right + 1] = False
        for ny in (sy - 1, sy + 1):
            if 0 <= ny < h:
                seg = todo[ny, left:right + 1]
                if seg.any():
                    prev = np.concatenate(([False], seg[:-1]))
                    starts = np.flatnonzero(seg & ~prev)
                    stack.extend((left + int(s), ny) for s in starts)
    return out


class SwatchButton(tk.Label):
    """Colour box that works as a button: left click = eyedropper over the input texture,
    right click = system colour picker."""

    def __init__(self, parent, default_hex, title, image_path_getter, on_activate=None, eyedropper=True):
        super().__init__(parent, width=6, height=2, relief="sunken", bd=2, cursor="hand2")
        self.title_text = title
        self.image_path_getter = image_path_getter
        self.on_activate = on_activate
        self._rgb = hex_to_rgb(default_hex)
        self.config(bg=rgb_to_hex(self._rgb))
        # eyedropper=True: left click = eyedropper, right click = colour picker.
        # eyedropper=False: any click = colour picker.
        left = self._eyedrop if eyedropper else self._pick
        self.bind("<Button-1>", lambda e: (self._activate(), left()))
        self.bind("<Button-3>", lambda e: (self._activate(), self._pick()))

    def _activate(self):
        if self.on_activate:
            self.on_activate()

    def set_rgb(self, rgb):
        self._rgb = tuple(int(c) for c in rgb)
        self.config(bg=rgb_to_hex(self._rgb))

    def rgb(self):
        return self._rgb

    def _pick(self):
        rgb, _ = colorchooser.askcolor(color=rgb_to_hex(self._rgb), title=self.title_text, parent=self)
        if rgb:
            self.set_rgb(rgb)

    def _eyedrop(self):
        path = self.image_path_getter() if self.image_path_getter else ""
        if not path or not os.path.isfile(path):
            messagebox.showwarning("Falta textura", "Elige primero una textura de entrada.", parent=self)
            return
        try:
            EyedropperWindow(self.winfo_toplevel(), path, "Cuentagotas: " + self.title_text, self.set_rgb)
        except Exception as ex:
            messagebox.showerror("Error", str(ex), parent=self)


class ZoomView(tk.Toplevel):
    """
    Toplevel with a canvas that shows an image fitted to the window (aspect ratio kept) and re-fits on
    resize. Mouse wheel zooms around the cursor; middle button or Space+left drag pans.
    Subclasses implement compose() and the mouse hooks.
    """
    ZOOM_STEP = 1.25
    MIN_SCALE = 0.01
    MAX_SCALE = 64.0

    def __init__(self, master, title, img_size):
        super().__init__(master)
        self.title(title)
        self.transient(master)
        self.img_w, self.img_h = img_size
        self.scale = 1.0
        self.px = 0.0
        self.py = 0.0
        self.fit_mode = True
        self._photo = None
        self._redraw_job = None
        self._pan = None
        self._left_pans = False
        self._space = False

        self.columnconfigure(0, weight=1)
        self.rowconfigure(1, weight=1)
        self.toolbar = ttk.Frame(self, padding=(6, 6, 6, 2))
        self.toolbar.grid(row=0, column=0, sticky="we")
        self.canvas = tk.Canvas(self, bg="#2b2b2b", highlightthickness=0, cursor="crosshair")
        self.canvas.grid(row=1, column=0, sticky="nsew")
        self.statusbar = ttk.Frame(self, padding=(6, 2, 6, 6))
        self.statusbar.grid(row=2, column=0, sticky="we")

        zoom = ttk.Frame(self.statusbar)
        zoom.pack(side="right")
        ttk.Button(zoom, text="−", width=3, command=lambda: self.zoom_center(1 / self.ZOOM_STEP)).pack(side="left")
        ttk.Button(zoom, text="+", width=3, command=lambda: self.zoom_center(self.ZOOM_STEP)).pack(side="left")
        ttk.Button(zoom, text="Ajustar", command=self.fit).pack(side="left", padx=(4, 0))
        ttk.Button(zoom, text="1:1", width=4, command=lambda: self.zoom_center(1.0 / self.scale)).pack(side="left", padx=(4, 0))
        self.zoom_var = tk.StringVar(value="")
        ttk.Label(zoom, textvariable=self.zoom_var, width=7, anchor="e").pack(side="left", padx=(6, 0))

        self._img_item = self.canvas.create_image(0, 0, anchor="nw")

        self.canvas.bind("<Configure>", self._on_configure)
        self.bind("<MouseWheel>", self._on_wheel)
        self.canvas.bind("<ButtonPress-2>", self._pan_start)
        self.canvas.bind("<B2-Motion>", self._pan_move)
        self.canvas.bind("<ButtonRelease-2>", self._pan_end)
        self.canvas.bind("<ButtonPress-1>", self._left_press)
        self.canvas.bind("<B1-Motion>", self._left_drag)
        self.canvas.bind("<ButtonRelease-1>", self._left_release)
        self.canvas.bind("<Motion>", lambda e: self.on_motion(*self.to_image(e.x, e.y), e))
        self.canvas.bind("<Leave>", lambda e: self.on_leave())
        self.bind("<KeyPress-space>", lambda e: setattr(self, "_space", True))
        self.bind("<KeyRelease-space>", lambda e: setattr(self, "_space", False))
        self.bind("<Key-f>", lambda e: self.fit())
        self.bind("<plus>", lambda e: self.zoom_center(self.ZOOM_STEP))
        self.bind("<KP_Add>", lambda e: self.zoom_center(self.ZOOM_STEP))
        self.bind("<minus>", lambda e: self.zoom_center(1 / self.ZOOM_STEP))
        self.bind("<KP_Subtract>", lambda e: self.zoom_center(1 / self.ZOOM_STEP))

        sw, sh = self.winfo_screenwidth(), self.winfo_screenheight()
        s = min(1.0, sw * 0.8 / self.img_w, sh * 0.7 / self.img_h)
        self.geometry(f"{max(820, int(self.img_w * s) + 12)}x{int(self.img_h * s) + 150}")
        self.minsize(520, 400)
        self.after(50, self.focus_force)

    # ---- hooks for subclasses
    def compose(self, box, size, resample):
        raise NotImplementedError

    def on_left_press(self, ix, iy, event):
        pass

    def on_left_drag(self, ix, iy, event):
        pass

    def on_left_release(self, ix, iy, event):
        pass

    def on_motion(self, ix, iy, event):
        pass

    def on_leave(self):
        pass

    def draw_overlay(self):
        pass

    # ---- coordinates
    def to_image(self, cx, cy):
        return (cx - self.px) / self.scale, (cy - self.py) / self.scale

    def to_canvas(self, ix, iy):
        return self.px + ix * self.scale, self.py + iy * self.scale

    def in_image(self, ix, iy):
        return 0 <= ix < self.img_w and 0 <= iy < self.img_h

    def _canvas_size(self):
        return max(1, self.canvas.winfo_width()), max(1, self.canvas.winfo_height())

    # ---- view
    def fit(self):
        cw, ch = self._canvas_size()
        s = min(cw / self.img_w, ch / self.img_h)
        self.scale = s
        self.px = (cw - self.img_w * s) / 2
        self.py = (ch - self.img_h * s) / 2
        self.fit_mode = True
        self.request_redraw()

    def zoom_at(self, cx, cy, factor):
        new = min(self.MAX_SCALE, max(self.MIN_SCALE, self.scale * factor))
        ix, iy = self.to_image(cx, cy)
        self.scale = new
        self.px = cx - ix * new
        self.py = cy - iy * new
        self.fit_mode = False
        self.request_redraw()

    def zoom_center(self, factor):
        cw, ch = self._canvas_size()
        self.zoom_at(cw / 2, ch / 2, factor)

    def _on_configure(self, _event):
        if self.fit_mode:
            self.fit()
        else:
            self.request_redraw()

    def _on_wheel(self, event):
        cx = self.canvas.winfo_pointerx() - self.canvas.winfo_rootx()
        cy = self.canvas.winfo_pointery() - self.canvas.winfo_rooty()
        self.zoom_at(cx, cy, self.ZOOM_STEP if event.delta > 0 else 1 / self.ZOOM_STEP)

    def _pan_start(self, e):
        self._pan = (e.x, e.y, self.px, self.py)
        self.canvas.config(cursor="fleur")

    def _pan_move(self, e):
        if self._pan is None:
            return
        sx, sy, opx, opy = self._pan
        self.px = opx + (e.x - sx)
        self.py = opy + (e.y - sy)
        self.fit_mode = False
        self.request_redraw()

    def _pan_end(self, _e):
        self._pan = None
        self.canvas.config(cursor=self.default_cursor())

    def default_cursor(self):
        return "crosshair"

    def _left_press(self, e):
        self.canvas.focus_set()
        if self._space:
            self._left_pans = True
            self._pan_start(e)
            return
        self._left_pans = False
        self.on_left_press(*self.to_image(e.x, e.y), e)

    def _left_drag(self, e):
        if self._left_pans:
            self._pan_move(e)
        else:
            self.on_left_drag(*self.to_image(e.x, e.y), e)

    def _left_release(self, e):
        if self._left_pans:
            self._left_pans = False
            self._pan_end(e)
        else:
            self.on_left_release(*self.to_image(e.x, e.y), e)

    def request_redraw(self):
        if self._redraw_job is None:
            self._redraw_job = self.after_idle(self._redraw)

    def _redraw(self):
        self._redraw_job = None
        self.zoom_var.set(f"{self.scale * 100:.0f}%")
        cw, ch = self._canvas_size()
        s = self.scale
        ix0 = max(0.0, -self.px / s)
        iy0 = max(0.0, -self.py / s)
        ix1 = min(float(self.img_w), (cw - self.px) / s)
        iy1 = min(float(self.img_h), (ch - self.py) / s)
        if ix1 <= ix0 or iy1 <= iy0:
            self.canvas.itemconfig(self._img_item, image="")
            self.draw_overlay()
            return
        bx0, by0 = int(math.floor(ix0)), int(math.floor(iy0))
        bx1, by1 = int(math.ceil(ix1)), int(math.ceil(iy1))
        size = (max(1, int(round((bx1 - bx0) * s))), max(1, int(round((by1 - by0) * s))))
        resample = RESAMPLE.NEAREST if s >= 1.0 else RESAMPLE.BILINEAR
        img = self.compose((bx0, by0, bx1, by1), size, resample)
        self._photo = ImageTk.PhotoImage(img)
        self.canvas.coords(self._img_item, self.px + bx0 * s, self.py + by0 * s)
        self.canvas.itemconfig(self._img_item, image=self._photo)
        self.draw_overlay()

    @staticmethod
    def resize_view(img, box, size, resample):
        gap = None if resample == RESAMPLE.NEAREST else 2.0
        return img.resize(size, resample, box=box, reducing_gap=gap)


class EyedropperWindow(ZoomView):
    """Left click previews the colour of a pixel of the input texture; right click accepts it."""

    def __init__(self, master, image_path, title, on_accept):
        src = Image.open(image_path).convert("RGBA")
        super().__init__(master, title, src.size)
        self.src = src
        bg = Image.new("RGBA", src.size, (60, 60, 60, 255))
        bg.alpha_composite(src)
        self.display = bg.convert("RGB")
        self.on_accept = on_accept
        self.picked = None  # (x, y, (r, g, b))

        ttk.Label(self.toolbar, text="Clic izquierdo: previsualizar color  ·  Clic derecho: aceptar  ·  "
                                     "Rueda: zoom  ·  Botón central / Espacio+arrastrar: mover  ·  Esc: cancelar"
                  ).pack(side="left")

        ttk.Label(self.statusbar, text="Color:").pack(side="left")
        self.swatch = tk.Label(self.statusbar, width=6, relief="sunken", bg="#3c3c3c")
        self.swatch.pack(side="left", padx=4)
        self.hex_var = tk.StringVar(value="—")
        ttk.Label(self.statusbar, textvariable=self.hex_var, width=26).pack(side="left")
        ttk.Button(self.statusbar, text="Aceptar", command=self._accept).pack(side="left", padx=(8, 0))
        ttk.Button(self.statusbar, text="Cancelar", command=self.destroy).pack(side="left", padx=4)
        self.hover_var = tk.StringVar(value="")
        ttk.Label(self.statusbar, textvariable=self.hover_var, foreground="#666").pack(side="left", padx=(12, 0))

        self.canvas.bind("<ButtonPress-3>", self._right_click)
        self.bind("<Escape>", lambda e: self.destroy())
        self.bind("<Return>", lambda e: self._accept())
        self._marker = [self.canvas.create_rectangle(0, 0, 0, 0, outline="#000000", width=3, state="hidden"),
                        self.canvas.create_rectangle(0, 0, 0, 0, outline="#FFFFFF", width=1, state="hidden")]

    def compose(self, box, size, resample):
        return self.resize_view(self.display, box, size, resample)

    def _sample(self, ix, iy):
        if not self.in_image(ix, iy):
            return None
        x, y = int(ix), int(iy)
        r, g, b, a = self.src.getpixel((x, y))
        return x, y, (r, g, b), a

    def _pick(self, ix, iy):
        res = self._sample(ix, iy)
        if res is None:
            return False
        x, y, rgb, a = res
        self.picked = (x, y, rgb)
        hx = rgb_to_hex(rgb)
        self.swatch.config(bg=hx)
        self.hex_var.set(f"{hx}  ({x}, {y})" + ("  [transparente]" if a == 0 else ""))
        self.draw_overlay()
        return True

    def on_left_press(self, ix, iy, event):
        self._pick(ix, iy)

    def on_left_drag(self, ix, iy, event):
        self._pick(ix, iy)

    def on_motion(self, ix, iy, event):
        res = self._sample(ix, iy)
        self.hover_var.set("" if res is None else f"Cursor: ({res[0]}, {res[1]}) {rgb_to_hex(res[2])}")

    def _right_click(self, e):
        if self.picked is None:
            self._pick(*self.to_image(e.x, e.y))
        self._accept()

    def _accept(self):
        if self.picked is None:
            return
        self.on_accept(self.picked[2])
        self.destroy()

    def draw_overlay(self):
        if self.picked is None:
            return
        x, y, _ = self.picked
        cx, cy = self.to_canvas(x + 0.5, y + 0.5)
        r = max(self.scale / 2, 5)
        for item in self._marker:
            self.canvas.coords(item, cx - r, cy - r, cx + r, cy + r)
            self.canvas.itemconfig(item, state="normal")
            self.canvas.tag_raise(item)


class MaskEditor(ZoomView):
    """
    Paints one mask layer per zone, the size of the input texture. Each zone has its own canvas: selecting
    a zone shows its layer (white = not painted, zone colour = painted) over the texture template.
    Brush paints, eraser clears, bucket fills, always on the active layer.
    The layers are kept in memory and handed back through on_accept(list_of_L_images_or_None).
    """

    def __init__(self, master, template_path, masks, on_accept):
        tmpl = Image.open(template_path).convert("RGBA")
        super().__init__(master, "Editor de máscara", tmpl.size)
        bg = Image.new("RGBA", tmpl.size, (255, 255, 255, 255))
        bg.alpha_composite(tmpl)
        self.template = bg.convert("RGB")
        self._template_arr = None
        self.on_accept = on_accept

        self.layers = []
        for i in range(len(ZONES)):
            layer = masks[i] if masks and i < len(masks) else None
            if layer is None:
                layer = Image.new("L", tmpl.size, 0)
            else:
                layer = layer.convert("L")
                if layer.size != tmpl.size:
                    layer = layer.resize(tmpl.size, RESAMPLE.NEAREST)
                layer = layer.copy()
            self.layers.append(layer)
        self.zone_index = 0
        self.draw = ImageDraw.Draw(self.layers[0])

        self.undo_stack = []
        self.dirty = False
        self._stroke_before = None
        self._last = None
        self._cursor = None
        self._tmpl_cache_key = None
        self._tmpl_cache = None

        self.tool_var = tk.StringVar(value="brush")
        self.size_var = tk.IntVar(value=max(2, min(self.img_w, self.img_h) // 100))
        self.fill_src_var = tk.StringVar(value="Máscara")
        self.fill_tol_var = tk.IntVar(value=24)
        self.opacity_var = tk.IntVar(value=50)

        self._build_toolbar()
        self._build_statusbar()

        self._cursor_items = [self.canvas.create_oval(0, 0, 0, 0, outline="#FFFFFF", state="hidden"),
                              self.canvas.create_oval(0, 0, 0, 0, outline="#000000", dash=(2, 2), state="hidden")]

        self.bind("<Control-z>", lambda e: self.undo())
        self.bind("<Control-Z>", lambda e: self.undo())
        self.bind("<Control-s>", lambda e: self.accept())
        self.bind("<Key-b>", lambda e: self._set_tool("brush"))
        self.bind("<Key-e>", lambda e: self._set_tool("eraser"))
        self.bind("<Key-g>", lambda e: self._set_tool("fill"))
        for i in range(len(ZONES)):
            self.bind(f"<Key-{i + 1}>", lambda e, i=i: self._select_zone(i))
        self.bind("<bracketleft>", lambda e: self._change_size(-1))
        self.bind("<bracketright>", lambda e: self._change_size(1))
        self.protocol("WM_DELETE_WINDOW", self._on_close)

    # ---- UI
    def _build_toolbar(self):
        row1 = ttk.Frame(self.toolbar)
        row1.pack(side="top", fill="x")
        for text, value in (("Pincel (B)", "brush"), ("Borrador (E)", "eraser"), ("Cubo (G)", "fill")):
            ttk.Radiobutton(row1, text=text, value=value, variable=self.tool_var, style="Toolbutton",
                            command=self._tool_changed).pack(side="left", padx=(0, 2))

        ttk.Separator(row1, orient="vertical").pack(side="left", fill="y", padx=8)
        ttk.Label(row1, text="Zona").pack(side="left", padx=(0, 4))
        self.zone_boxes = []
        for i, (name, rgb) in enumerate(ZONES):
            box = tk.Label(row1, width=4, height=1, bg=rgb_to_hex(rgb), cursor="hand2",
                           highlightthickness=3, bd=0)
            box.pack(side="left", padx=2)
            box.bind("<Button-1>", lambda e, i=i: self._select_zone(i))
            self.zone_boxes.append(box)
        self._refresh_zone_boxes()

        ttk.Separator(row1, orient="vertical").pack(side="left", fill="y", padx=8)
        ttk.Label(row1, text="Grosor").pack(side="left")
        max_size = max(64, min(self.img_w, self.img_h) // 6)
        tk.Scale(row1, from_=1, to=max_size, orient="horizontal", length=160, variable=self.size_var,
                 showvalue=True, command=lambda _v: self.draw_overlay()).pack(side="left", padx=4)

        row2 = ttk.Frame(self.toolbar)
        row2.pack(side="top", fill="x", pady=(4, 0))
        ttk.Label(row2, text="Cubo: rellenar según").pack(side="left")
        ttk.Combobox(row2, textvariable=self.fill_src_var, values=("Máscara", "Textura"), state="readonly",
                     width=9).pack(side="left", padx=4)
        ttk.Label(row2, text="Tolerancia").pack(side="left")
        tk.Scale(row2, from_=0, to=255, orient="horizontal", length=120, variable=self.fill_tol_var,
                 showvalue=True).pack(side="left", padx=4)
        ttk.Separator(row2, orient="vertical").pack(side="left", fill="y", padx=8)
        ttk.Label(row2, text="Plantilla %").pack(side="left")
        tk.Scale(row2, from_=0, to=100, orient="horizontal", length=120, variable=self.opacity_var,
                 showvalue=True, command=lambda _v: self.request_redraw()).pack(side="left", padx=4)

        ttk.Label(self.toolbar, foreground="#666",
                  text="Cada zona tiene su propio lienzo (● = tiene algo pintado)  ·  1-5: zona  ·  Rueda: zoom  ·  Botón central / "
                       "Espacio+arrastrar: mover  ·  [ ]: grosor  ·  Ctrl+Z: deshacer  ·  F: ajustar"
                  ).pack(side="top", anchor="w", pady=(4, 0))

    def _build_statusbar(self):
        ttk.Button(self.statusbar, text="Aceptar", command=self.accept).pack(side="left")
        ttk.Button(self.statusbar, text="Cancelar", command=self._on_close).pack(side="left", padx=4)
        ttk.Button(self.statusbar, text="Deshacer", command=self.undo).pack(side="left", padx=(12, 0))
        ttk.Button(self.statusbar, text="Limpiar zona", command=self._clear).pack(side="left", padx=4)
        self.pos_var = tk.StringVar(value=f"{self.img_w}×{self.img_h}")
        ttk.Label(self.statusbar, textvariable=self.pos_var, foreground="#666").pack(side="left", padx=(12, 0))

    def _refresh_zone_boxes(self):
        for i, box in enumerate(self.zone_boxes):
            sel = i == self.zone_index
            color = "#FF8C00" if sel else "#BBBBBB"
            r, g, b = ZONES[i][1]
            fg = "#000000" if (0.299 * r + 0.587 * g + 0.114 * b) > 140 else "#FFFFFF"
            box.config(highlightbackground=color, highlightcolor=color, fg=fg,
                       text="●" if layer_used(self.layers[i]) else "")

    def _select_zone(self, index):
        """Switches to the canvas (layer) of another zone."""
        if self._stroke_before is not None:
            return
        self.zone_index = index
        self.draw = ImageDraw.Draw(self.layers[index])
        self._refresh_zone_boxes()
        self.request_redraw()

    @property
    def mask(self):
        """Layer of the active zone ("L", 255 = painted)."""
        return self.layers[self.zone_index]

    @property
    def zone_color(self):
        return ZONES[self.zone_index][1]

    def _set_tool(self, tool):
        self.tool_var.set(tool)
        self._tool_changed()

    def _tool_changed(self):
        self.canvas.config(cursor=self.default_cursor())
        self.draw_overlay()

    def default_cursor(self):
        return "tcross" if self.tool_var.get() == "fill" else "crosshair"

    def _change_size(self, direction):
        v = self.size_var.get()
        step = max(1, int(v * 0.15))
        self.size_var.set(max(1, v + direction * step))
        self.draw_overlay()

    # ---- rendering
    def compose(self, box, size, resample):
        key = (box, size, resample)
        if self._tmpl_cache_key != key:
            self._tmpl_cache = self.resize_view(self.template, box, size, resample)
            self._tmpl_cache_key = key
        layer = self.resize_view(self.mask, box, size, resample)
        painted = Image.new("RGB", size, self.zone_color)
        canvas = Image.new("RGB", size, (255, 255, 255))
        canvas.paste(painted, (0, 0), layer)
        return Image.blend(canvas, self._tmpl_cache, self.opacity_var.get() / 100.0)

    def draw_overlay(self):
        tool = self.tool_var.get()
        if self._cursor is None or tool == "fill":
            for item in self._cursor_items:
                self.canvas.itemconfig(item, state="hidden")
            return
        ix, iy = self._cursor
        cx, cy = self.to_canvas(ix, iy)
        r = max(1.5, self.size_var.get() / 2 * self.scale)
        for item in self._cursor_items:
            self.canvas.coords(item, cx - r, cy - r, cx + r, cy + r)
            self.canvas.itemconfig(item, state="normal")
            self.canvas.tag_raise(item)

    def on_motion(self, ix, iy, event):
        self._cursor = (ix, iy)
        if self.in_image(ix, iy):
            self.pos_var.set(f"Zona {ZONES[self.zone_index][0]}  ·  ({int(ix)}, {int(iy)})  ·  {self.img_w}×{self.img_h}")
        self.draw_overlay()

    def on_leave(self):
        self._cursor = None
        self.draw_overlay()

    # ---- painting on the active layer (no anti-aliasing: a layer only holds 0 or 255)
    def _paint_value(self):
        return 255 if self.tool_var.get() == "brush" else 0

    def _dab(self, x, y, value):
        size = self.size_var.get()
        if size <= 1:
            self.draw.point((int(x), int(y)), fill=value)
            return
        r = size / 2.0
        self.draw.ellipse((x - r, y - r, x + r, y + r), fill=value)

    def on_left_press(self, ix, iy, event):
        if self.tool_var.get() == "fill":
            self._fill(ix, iy)
            return
        self._stroke_before = self.mask.copy()
        value = self._paint_value()
        self._dab(ix, iy, value)
        self._last = (ix, iy)
        self.dirty = True
        self.request_redraw()

    def on_left_drag(self, ix, iy, event):
        self._cursor = (ix, iy)
        if self._last is None:
            return
        value = self._paint_value()
        size = self.size_var.get()
        self.draw.line([self._last, (ix, iy)], fill=value, width=max(1, size))
        self._dab(ix, iy, value)
        self._last = (ix, iy)
        self.request_redraw()

    def on_left_release(self, ix, iy, event):
        if self._stroke_before is not None:
            bbox = ImageChops.difference(self._stroke_before, self.mask).getbbox()
            if bbox:
                self._push_undo(self.zone_index, bbox, self._stroke_before.crop(bbox))
            self._stroke_before = None
            self._refresh_zone_boxes()
        self._last = None

    def _fill(self, ix, iy):
        """Bucket: fills the active layer. 'Máscara' = connected area of the same state (painted or not)
        in this zone's canvas; 'Textura' = connected area of similar colour in the template."""
        if not self.in_image(ix, iy):
            return
        x, y = int(ix), int(iy)
        arr = np.array(self.mask)
        if self.fill_src_var.get() == "Textura":
            if self._template_arr is None:
                self._template_arr = np.array(self.template)
            ref = self._template_arr.astype(np.int16)
            seed = ref[y, x]
            region = np.all(np.abs(ref - seed) <= int(self.fill_tol_var.get()), axis=-1)
        else:
            region = arr == arr[y, x]
        self.config(cursor="watch")
        self.update_idletasks()
        try:
            filled = flood_region(region, x, y)
        finally:
            self.config(cursor="")
        filled &= arr != 255
        if not filled.any():
            return
        ys = np.flatnonzero(filled.any(axis=1))
        xs = np.flatnonzero(filled.any(axis=0))
        bbox = (int(xs[0]), int(ys[0]), int(xs[-1]) + 1, int(ys[-1]) + 1)
        self._push_undo(self.zone_index, bbox, self.mask.crop(bbox))
        arr[filled] = 255
        self._set_layer(self.zone_index, Image.fromarray(arr, "L"))

    def _set_layer(self, index, img):
        self.layers[index] = img
        if index == self.zone_index:
            self.draw = ImageDraw.Draw(img)
        self.dirty = True
        self._refresh_zone_boxes()
        self.request_redraw()

    def _push_undo(self, index, bbox, crop):
        self.undo_stack.append((index, bbox, crop))
        total = sum(c.width * c.height for _, _, c in self.undo_stack)
        while len(self.undo_stack) > 1 and total > UNDO_MAX_BYTES:
            _, _, old = self.undo_stack.pop(0)
            total -= old.width * old.height

    def undo(self):
        """Undoes the last change, of whichever zone, and shows that zone's canvas."""
        if not self.undo_stack or self._stroke_before is not None:
            return
        index, bbox, crop = self.undo_stack.pop()
        self.layers[index].paste(crop, bbox[:2])
        self.dirty = True
        if index != self.zone_index:
            self._select_zone(index)
        else:
            self._refresh_zone_boxes()
            self.request_redraw()

    def _clear(self):
        """Clears only the canvas of the active zone."""
        if not layer_used(self.mask):
            return
        self._push_undo(self.zone_index, (0, 0, self.img_w, self.img_h), self.mask.copy())
        self._set_layer(self.zone_index, Image.new("L", (self.img_w, self.img_h), 0))

    # ---- accept / close
    def accept(self):
        masks = [layer if layer_used(layer) else None for layer in self.layers]
        self.on_accept(masks if any(m is not None for m in masks) else None)
        self.dirty = False
        self.destroy()

    def _on_close(self):
        if self.dirty:
            ans = messagebox.askyesnocancel("Máscara sin aplicar", "¿Aplicar los cambios de la máscara antes de cerrar?",
                                            parent=self)
            if ans is None:
                return
            if ans:
                self.accept()
                return
        self.destroy()


class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("Recolor de traje (cualquier color -> otro color)")
        self.resizable(False, False)

        self.result_img = None
        self.preview_photo = None
        self.masks = None  # one "L" layer per zone (255 = painted), or None

        frm = ttk.Frame(self, padding=10)
        frm.grid(row=0, column=0, sticky="nsew")

        # --- files
        self.in_var = tk.StringVar()
        self.out_var = tk.StringVar()
        self._row_file(frm, 0, "Textura de entrada", self.in_var, self._pick_input)
        self._row_file(frm, 1, "Guardar como", self.out_var, self._pick_output)
        ttk.Label(frm, text="Máscara (opcional)").grid(row=2, column=0, sticky="w")
        mrow = ttk.Frame(frm)
        mrow.grid(row=2, column=1, columnspan=2, sticky="we", padx=6)
        self.mask_btn = ttk.Button(mrow, text="Crear...", command=self._edit_mask)
        self.mask_btn.pack(side="left")
        self.mask_clear_btn = ttk.Button(mrow, text="Borrar", command=self._clear_mask)
        self.mask_clear_btn.pack(side="left", padx=4)
        self.mask_info = tk.StringVar()
        ttk.Label(mrow, textvariable=self.mask_info, foreground="#666").pack(side="left", padx=(8, 0))
        self._refresh_mask_row()

        # --- colours (one column per mask zone)
        cols = ttk.LabelFrame(frm, text="Colores", padding=8)
        cols.grid(row=3, column=0, columnspan=3, sticky="we", pady=(10, 0))
        ttk.Label(cols, text="Máscara a sustituir").grid(row=0, column=0, sticky="w", padx=(0, 10))
        ttk.Label(cols, text="Color a sustituir").grid(row=1, column=0, sticky="w", padx=(0, 10))
        ttk.Label(cols, text="Color nuevo").grid(row=2, column=0, sticky="w", padx=(0, 10))
        ttk.Label(cols, text="Modo").grid(row=3, column=0, sticky="w", padx=(0, 10))
        self.src_fields = []
        self.dst_fields = []
        self.mode_vars = []
        self.zone_headers = []
        self.active_zone = 0
        # Thresholds of "Qué píxeles cambian", one set per zone (column).
        self.zone_params = [dict(DEFAULT_PARAMS) for _ in ZONES]
        for i, (name, rgb) in enumerate(ZONES):
            col = i + 1
            header = tk.Label(cols, width=8, height=2, bg=rgb_to_hex(rgb), bd=0, highlightthickness=4,
                              cursor="hand2")
            header.grid(row=0, column=col, padx=8, pady=4)
            header.bind("<Button-1>", lambda e, i=i: self._select_zone(i))
            activate = (lambda i=i: self._select_zone(i))
            src = SwatchButton(cols, DEFAULT_SRC_HEX, f"Zona {name}: color a sustituir", self.in_var.get, activate)
            src.grid(row=1, column=col, padx=8, pady=4)
            dst = SwatchButton(cols, DEFAULT_DST_HEX, f"Zona {name}: color nuevo", self.in_var.get, activate,
                               eyedropper=False)
            dst.grid(row=2, column=col, padx=8, pady=4)
            mode = tk.StringVar(value=MODE_HUE)
            combo = ttk.Combobox(cols, textvariable=mode, values=(MODE_HUE, MODE_FULL), state="readonly", width=9)
            combo.grid(row=3, column=col, padx=8, pady=4)
            combo.bind("<Button-1>", lambda e, i=i: self._select_zone(i), add="+")
            combo.bind("<<ComboboxSelected>>", lambda e, i=i: self._select_zone(i), add="+")
            self.zone_headers.append(header)
            self.src_fields.append(src)
            self.dst_fields.append(dst)
            self.mode_vars.append(mode)
        ttk.Label(cols, foreground="#666",
                  text="Clic en la fila de máscara: seleccionar zona (sus ajustes se muestran abajo).\n"
                       "Color a sustituir: cuentagotas sobre la textura  ·  Color nuevo: selector de color.\n"
                       "Tono: cambia tono y saturación, conserva el brillo.  Completo: también el brillo "
                       "(sirve para blanco, gris, negro).\n"
                       "Sin máscara se aplica sólo la columna negra a toda la textura."
                  ).grid(row=4, column=0, columnspan=len(ZONES) + 1, sticky="w", pady=(6, 0))

        # --- tolerance (values of the active zone)
        self.tol_frame = ttk.LabelFrame(frm, text="Qué píxeles cambian", padding=8)
        self.tol_frame.grid(row=4, column=0, columnspan=3, sticky="we", pady=(10, 0))
        self._loading_params = False
        self.param_vars = {}
        self.param_widgets = {}
        for row, (key, label, lo, hi, step) in enumerate(PARAM_SLIDERS):
            var, widgets = self._spin(self.tol_frame, row, label, lo, hi, DEFAULT_PARAMS[key], step=step)
            var.trace_add("write", lambda *_a, k=key: self._param_changed(k))
            self.param_vars[key] = var
            self.param_widgets[key] = widgets
        ttk.Button(self.tol_frame, text="Aplicar a todas las zonas", command=self._params_to_all
                   ).grid(row=len(PARAM_SLIDERS), column=0, columnspan=2, sticky="w", pady=(6, 0))
        for mode in self.mode_vars:
            mode.trace_add("write", lambda *_a: self._refresh_param_state())
        self._select_zone(0)

        # --- buttons and status
        btns = ttk.Frame(frm)
        btns.grid(row=6, column=0, columnspan=3, sticky="we", pady=(10, 0))
        ttk.Button(btns, text="Vista previa", command=self._preview).pack(side="left")
        ttk.Button(btns, text="Guardar", command=self._save).pack(side="left", padx=8)
        self.status = tk.StringVar(value="Elige una textura de entrada.")
        ttk.Label(frm, textvariable=self.status).grid(row=7, column=0, columnspan=3, sticky="w", pady=(6, 0))

        self.canvas = tk.Label(frm, relief="sunken")
        self.canvas.grid(row=8, column=0, columnspan=3, pady=(8, 0))

    # ---- widget helpers
    def _row_file(self, parent, row, label, var, command):
        ttk.Label(parent, text=label).grid(row=row, column=0, sticky="w")
        ttk.Entry(parent, textvariable=var, width=50).grid(row=row, column=1, sticky="we", padx=6)
        ttk.Button(parent, text="Examinar...", command=command).grid(row=row, column=2)

    def _spin(self, parent, row, label, lo, hi, default, step=1):
        """Slider (tk.Scale) with its value shown on the right. Returns (DoubleVar, (label, scale))."""
        lbl = ttk.Label(parent, text=label, width=36)
        lbl.grid(row=row, column=0, sticky="w")
        var = tk.DoubleVar(value=float(default))
        scale = tk.Scale(parent, from_=lo, to=hi, resolution=step, orient="horizontal", length=240,
                         variable=var, showvalue=True)
        scale.grid(row=row, column=1, sticky="we", padx=6, pady=2)
        return var, (lbl, scale)

    # ---- active zone and its thresholds
    def _select_zone(self, index):
        """Makes a column active: highlights it and loads its thresholds into the sliders."""
        self.active_zone = index
        for i, header in enumerate(self.zone_headers):
            color = "#FF8C00" if i == index else "#000000"
            header.config(highlightbackground=color, highlightcolor=color)
        self._loading_params = True
        try:
            for key, var in self.param_vars.items():
                var.set(self.zone_params[index][key])
        finally:
            self._loading_params = False
        self.tol_frame.config(text=f"Qué píxeles cambian — Zona {ZONES[index][0]}")
        self._refresh_param_state()

    def _param_changed(self, key):
        if self._loading_params:
            return
        try:
            self.zone_params[self.active_zone][key] = float(self.param_vars[key].get())
        except (tk.TclError, ValueError):
            pass

    def _refresh_param_state(self):
        """Greys out the sliders that do not affect the mode of the active zone."""
        mode = self.mode_vars[self.active_zone].get()
        for key, (lbl, scale) in self.param_widgets.items():
            enabled = mode in PARAM_MODES[key]
            lbl.state(["!disabled"] if enabled else ["disabled"])
            scale.config(state="normal" if enabled else "disabled", fg="#000000" if enabled else "#A0A0A0")

    def _params_to_all(self):
        src = dict(self.zone_params[self.active_zone])
        for i in range(len(ZONES)):
            self.zone_params[i] = dict(src)
        self.status.set(f"Ajustes de la zona {ZONES[self.active_zone][0]} copiados a todas las zonas.")

    # ---- file pickers
    def _pick_input(self):
        path = filedialog.askopenfilename(title="Textura de entrada", filetypes=[("PNG", "*.png"), ("Todas", "*.*")])
        if not path:
            return
        self.in_var.set(path)
        base, ext = os.path.splitext(path)
        self.out_var.set(base + "_recolor" + (ext or ".png"))
        self.status.set("Entrada elegida. Pulsa Vista previa.")

    def _pick_output(self):
        path = filedialog.asksaveasfilename(title="Guardar como", defaultextension=".png", filetypes=[("PNG", "*.png")])
        if path:
            self.out_var.set(path)

    # ---- mask
    def _refresh_mask_row(self):
        used = zones_used(self.masks)
        if not used:
            self.mask_btn.config(text="Crear...")
            self.mask_clear_btn.state(["disabled"])
            self.mask_info.set("Sin máscara: se aplica la columna negra a toda la textura.")
        else:
            self.mask_btn.config(text="Editar...")
            self.mask_clear_btn.state(["!disabled"])
            self.mask_info.set("Zonas en uso: " + ", ".join(ZONES[i][0] for i in used))

    def _edit_mask(self):
        path = self.in_var.get()
        if not path or not os.path.isfile(path):
            messagebox.showwarning("Falta textura", "Elige primero una textura de entrada: se usa como plantilla.")
            return
        try:
            MaskEditor(self, path, self.masks, self._mask_accepted)
        except Exception as ex:
            messagebox.showerror("Error", str(ex))

    def _mask_accepted(self, masks):
        self.masks = masks
        self._refresh_mask_row()
        self.status.set("Máscara aplicada." if masks is not None else "Máscara vacía: se descarta.")

    def _clear_mask(self):
        self.masks = None
        self._refresh_mask_row()

    # ---- actions
    def _run(self):
        if not self.in_var.get():
            raise ValueError("Elige una textura de entrada.")
        src = Image.open(self.in_var.get())
        rules = [(s.rgb(), d.rgb(), m.get(), dict(p))
                 for s, d, m, p in zip(self.src_fields, self.dst_fields, self.mode_vars, self.zone_params)]
        return recolor(
            src,
            rules,
            masks=self.masks,
        )

    def _preview(self):
        try:
            img, changed = self._run()
        except Exception as ex:
            messagebox.showerror("Error", str(ex))
            return
        self.result_img = img
        thumb = img.copy()
        thumb.thumbnail((PREVIEW_MAX, PREVIEW_MAX))
        bg = Image.new("RGBA", thumb.size, (60, 60, 60, 255))
        bg.alpha_composite(thumb)
        self.preview_photo = ImageTk.PhotoImage(bg.convert("RGB"))
        self.canvas.config(image=self.preview_photo, width=thumb.size[0], height=thumb.size[1])
        self.status.set(f"Vista previa: {changed} píxeles cambiados de {img.size[0] * img.size[1]}.")

    def _save(self):
        if not self.out_var.get():
            messagebox.showwarning("Falta destino", "Elige dónde guardar el resultado.")
            return
        try:
            img, changed = self._run()
        except Exception as ex:
            messagebox.showerror("Error", str(ex))
            return
        img.save(self.out_var.get())
        self.status.set(f"Guardado ({changed} píxeles cambiados): {self.out_var.get()}")


if __name__ == "__main__":
    App().mainloop()
