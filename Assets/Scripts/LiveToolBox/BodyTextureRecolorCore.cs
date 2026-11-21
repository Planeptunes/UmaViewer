// Herramienta local (no forma parte del visor publicado).
// Colour replacement used by BodyTextureRecolor (port of tools/recolor_swimsuit_gui.py).
using System;
using System.Collections.Generic;
using UnityEngine;

public static class BodyTextureRecolorCore
{
    public const int ZoneCount = 5;

    /// <summary>Brightness (fraction of the pixel's own) added at shade = 1, so a black keeps a faint relief instead of going flat.</summary>
    const float ShadeRange = 0.3f;

    public static readonly string[] ZoneNames = { "Negro", "Rojo", "Amarillo", "Verde", "Azul" };

    public static readonly Color32[] ZoneColors =
    {
        new Color32(0, 0, 0, 255),
        new Color32(237, 28, 36, 255),
        new Color32(255, 242, 0, 255),
        new Color32(34, 177, 76, 255),
        new Color32(63, 72, 204, 255),
    };

    /// <summary>One "colour to replace -> new colour" rule with its own thresholds.</summary>
    [Serializable]
    public class Rule
    {
        public Color32 src = new Color32(0x8B, 0xD8, 0xA0, 255);
        public Color32 dst = new Color32(0x3A, 0x7B, 0xD5, 255);
        /// <summary>false = Tono (hue and saturation, keeps brightness); true = Completo (hue, saturation and brightness).</summary>
        public bool full;
        public float tolDeg = 8f;
        public float minSat = 0.05f;
        public float minVal = 0.05f;
        public float rgbTol = 60f;
        /// <summary>Completo only: how much of the pixel's own shading is kept when the new colour is very dark (0 = flat).</summary>
        public float shade;

        public void CopyThresholdsFrom(Rule o)
        {
            tolDeg = o.tolDeg; minSat = o.minSat; minVal = o.minVal; rgbTol = o.rgbTol; shade = o.shade;
        }

        /// <summary>Copies everything: colours, mode and thresholds.</summary>
        public void CopyFrom(Rule o)
        {
            src = o.src; dst = o.dst; full = o.full;
            CopyThresholdsFrom(o);
        }
    }

    /// <summary>Source pixels with their HSV precomputed (the recolour runs many times on the same texture).</summary>
    public class Source
    {
        public int w, h;
        public bool srgb;
        public Color32[] px;
        public float[] H, S, V;   // H in degrees

        public Source(Color32[] pixels, int width, int height, bool isSrgb, bool withHsv = true)
        {
            px = pixels; w = width; h = height; srgb = isSrgb;
            if (!withHsv) return;   // painted textures (triple mask) are never recoloured
            int n = px.Length;
            H = new float[n]; S = new float[n]; V = new float[n];
            for (int i = 0; i < n; i++)
            {
                Color32 p = px[i];
                Color.RGBToHSV(new Color(p.r / 255f, p.g / 255f, p.b / 255f), out float hh, out float ss, out float vv);
                H[i] = hh * 360f; S[i] = ss; V[i] = vv;
            }
        }
    }

    public static bool LayerUsed(byte[] layer)
    {
        if (layer == null) return false;
        for (int i = 0; i < layer.Length; i++) if (layer[i] != 0) return true;
        return false;
    }

    /// <summary>
    /// Recolours `src`. Each rule only acts inside its zone layer (mask size mw x mh, 0 = not painted, any other
    /// value = painted; sampled with nearest scaling when the texture has another size). Without any painted
    /// layer only rule 0 (black zone) is applied, to the whole texture. All rules read the original pixels;
    /// where zones overlap the later zone wins. Untouched pixels keep their exact value; alpha is never changed.
    /// </summary>
    public static Color32[] Recolor(Source src, Rule[] rules, byte[][] layers, int mw, int mh, out int changed)
    {
        changed = 0;
        int w = src.w, h = src.h;
        var output = (Color32[])src.px.Clone();

        var zones = new List<int>();
        if (layers != null)
            for (int z = ZoneCount - 1; z >= 0; z--)   // later zone first: the first match wins
                if (z < layers.Length && LayerUsed(layers[z])) zones.Add(z);
        bool noMask = zones.Count == 0;
        if (noMask) zones.Add(0);

        int zc = zones.Count;
        var r = new Rule[zc];
        var sh = new float[zc]; var ss = new float[zc]; var sv = new float[zc];
        var dh = new float[zc]; var ds = new float[zc]; var dv = new float[zc];
        for (int k = 0; k < zc; k++)
        {
            r[k] = rules[zones[k]];
            Color.RGBToHSV(r[k].src, out float a, out float b, out float c);
            sh[k] = a * 360f; ss[k] = b; sv[k] = c;
            Color.RGBToHSV(r[k].dst, out a, out b, out c);
            dh[k] = a; ds[k] = b; dv[k] = c;   // dh stays 0..1 for HSVToRGB
        }

        bool sameSize = mw == w && mh == h;
        for (int y = 0; y < h; y++)
        {
            int my = sameSize ? y : Mathf.Min(mh - 1, (int)((long)y * mh / h));
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                int mi = sameSize ? i : my * mw + Mathf.Min(mw - 1, (int)((long)x * mw / w));
                float hue = src.H[i], sat = src.S[i], val = src.V[i];
                Color32 p = src.px[i];

                for (int k = 0; k < zc; k++)
                {
                    if (!noMask && layers[zones[k]][mi] == 0) continue;
                    Rule rule = r[k];
                    float nh, ns, nv;

                    if (rule.full)
                    {
                        float dist;
                        if (sv[k] > 1e-6f)
                        {
                            // Shading-invariant match: bring the pixel to the brightness of the colour to replace.
                            if (val < rule.minVal) continue;
                            float scale = sv[k] / Mathf.Max(val, 1e-6f);
                            float dr = p.r * scale - rule.src.r, dg = p.g * scale - rule.src.g, db = p.b * scale - rule.src.b;
                            dist = Mathf.Sqrt(dr * dr + dg * dg + db * db);
                        }
                        else
                        {
                            float dr = p.r - rule.src.r, dg = p.g - rule.src.g, db = p.b - rule.src.b;
                            dist = Mathf.Sqrt(dr * dr + dg * dg + db * db);
                        }
                        if (dist > rule.rgbTol) continue;
                        nh = dh[k];
                        // Saturation relative to the target, never carried over additively: the pixel's own tint
                        // (e.g. the warm tint of a black toon map) scales with the new colour's saturation, so an
                        // achromatic target (white / grey) gives a neutral result instead of amplifying the tint
                        // with the brightness boost (the hue of white is 0 = red).
                        ns = ss[k] > 0.02f
                            ? Mathf.Clamp01(sat * (ds[k] / ss[k]))
                            : Mathf.Clamp01(ds[k] * (1f + sat));
                        nv = sv[k] > 1e-6f ? Mathf.Clamp01(val * (dv[k] / sv[k]) + rule.shade * val * ShadeRange) : Mathf.Clamp01(val + dv[k]);
                    }
                    else
                    {
                        if (sat < rule.minSat || val < rule.minVal) continue;
                        float d = Mathf.Abs(hue - sh[k]);
                        d = Mathf.Min(d, 360f - d);
                        if (d > rule.tolDeg) continue;
                        nh = dh[k];
                        ns = ds[k];
                        nv = val;
                    }

                    Color o = Color.HSVToRGB(nh, ns, nv);
                    output[i] = new Color32((byte)Mathf.RoundToInt(o.r * 255f), (byte)Mathf.RoundToInt(o.g * 255f),
                                            (byte)Mathf.RoundToInt(o.b * 255f), p.a);
                    changed++;
                    break;
                }
            }
        }
        return output;
    }

    /// <summary>Scanline flood fill (4-connected) over `region`, from (x, y). Rows are stored bottom-up like Texture2D.</summary>
    public static bool[] FloodFill(bool[] region, int w, int h, int x, int y)
    {
        var output = new bool[region.Length];
        if (!region[y * w + x]) return output;
        var todo = (bool[])region.Clone();
        var stack = new Stack<int>();
        stack.Push(y * w + x);
        while (stack.Count > 0)
        {
            int idx = stack.Pop();
            int sy = idx / w, sx = idx % w;
            int row = sy * w;
            if (!todo[idx]) continue;
            int left = sx, right = sx;
            while (left > 0 && todo[row + left - 1]) left--;
            while (right < w - 1 && todo[row + right + 1]) right++;
            for (int k = left; k <= right; k++) { output[row + k] = true; todo[row + k] = false; }
            for (int ny = sy - 1; ny <= sy + 1; ny += 2)
            {
                if (ny < 0 || ny >= h) continue;
                int nrow = ny * w;
                bool prev = false;
                for (int k = left; k <= right; k++)
                {
                    bool cur = todo[nrow + k];
                    if (cur && !prev) stack.Push(nrow + k);
                    prev = cur;
                }
            }
        }
        return output;
    }

    public static string ToHex(Color32 c)
    {
        return "#" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2");
    }

    public static bool TryParseHex(string text, out Color32 c)
    {
        c = new Color32(0, 0, 0, 255);
        if (string.IsNullOrEmpty(text)) return false;
        string t = text.Trim();
        if (t.StartsWith("#")) t = t.Substring(1);
        if (t.Length != 6) return false;
        try
        {
            c = new Color32(Convert.ToByte(t.Substring(0, 2), 16), Convert.ToByte(t.Substring(2, 2), 16),
                            Convert.ToByte(t.Substring(4, 2), 16), 255);
            return true;
        }
        catch (FormatException) { return false; }
    }
}
