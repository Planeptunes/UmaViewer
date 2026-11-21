"""
recolor_swimsuit.py
Changes the hue of green pixels to another hue (default: blue), keeping saturation and
brightness so shadows and folds are preserved. Works on diffuse and toon textures.

Usage:
  python tools/recolor_swimsuit.py diffuse.png diffuse_blue.png --region 0 220 260 490
  python tools/recolor_swimsuit.py toon.png toon_blue.png --region 0 220 260 490

Options:
  --region x0 y0 x1 y1   Only change pixels inside this rectangle (texture pixels, origin top-left).
  --mask mask.png        Black/white mask of the same size (more precise than the rectangle).
  --hue-min / --hue-max  Hue range considered "green" (degrees, 0-360). Default 70-170.
  --min-sat              Minimum saturation to touch a pixel. Default 0.15.
  --target-hue           Target hue (degrees). 210 = blue. Default 210.

Requires: pip install pillow numpy
"""
import argparse

import numpy as np
from PIL import Image


def hsv_to_rgb(h, s, v):
    """h in 0..1, s and v in 0..1 (numpy arrays). Returns r, g, b in 0..1."""
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


def main():
    ap = argparse.ArgumentParser(description="Recolor green pixels of a texture to another hue.")
    ap.add_argument("input")
    ap.add_argument("output")
    ap.add_argument("--region", nargs=4, type=int, metavar=("X0", "Y0", "X1", "Y1"))
    ap.add_argument("--mask")
    ap.add_argument("--hue-min", type=float, default=70)
    ap.add_argument("--hue-max", type=float, default=170)
    ap.add_argument("--min-sat", type=float, default=0.15)
    ap.add_argument("--target-hue", type=float, default=210)
    args = ap.parse_args()

    src = Image.open(args.input).convert("RGBA")
    w, h_px = src.size
    rgba = np.array(src)
    alpha = rgba[..., 3]

    hsv = np.array(src.convert("RGB").convert("HSV")).astype(np.float32)
    hue = hsv[..., 0] / 255.0 * 360.0
    sat = hsv[..., 1] / 255.0
    val = hsv[..., 2] / 255.0

    sel = (hue >= args.hue_min) & (hue <= args.hue_max) & (sat >= args.min_sat) & (alpha > 127)

    if args.region:
        x0, y0, x1, y1 = args.region
        box = np.zeros_like(sel)
        box[y0:y1 + 1, x0:x1 + 1] = True
        sel &= box

    if args.mask:
        m = np.array(Image.open(args.mask).convert("L").resize((w, h_px))).astype(np.float32) / 255.0
        sel &= m > 0.5

    new_hue = np.where(sel, args.target_hue, hue) / 360.0
    r, g, b = hsv_to_rgb(new_hue, sat, val)

    out = np.stack([r * 255, g * 255, b * 255, alpha.astype(np.float32)], axis=-1)
    out = np.clip(np.round(out), 0, 255).astype(np.uint8)
    Image.fromarray(out, "RGBA").save(args.output)

    print(f"{args.output}: {int(sel.sum())} of {hue.size} pixels recolored")


if __name__ == "__main__":
    main()
