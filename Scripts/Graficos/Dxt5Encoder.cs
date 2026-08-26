using System;
using Godot;

namespace TowerOfArgentum.Graficos;

/// <summary>
/// Compresor DXT5 (BC3) en C# puro, estilo range-fit con inset (stb_dxt).
/// Existe porque los templates de EXPORT de Godot no traen etcpak — fuera del
/// editor Image.Compress(S3Tc) FALLA y el fallback RGBA8 multiplica la VRAM x4-6.
/// Lo usa MakeTexture (MegaAtlas) como plan B, y la calidad de texturas Clásica
/// (x2) que genera sus páginas en la PC del jugador. Calidad: levemente inferior
/// a etcpak; imperceptible sobre el arte suave AnimeSharp reescalado.
/// </summary>
public static class Dxt5Encoder
{
    /// <summary>img debe estar SIN comprimir (se convierte a RGBA8 si hace falta).
    /// Devuelve el buffer DXT5 (bloques 4x4 de 16 bytes, alpha+color) o null.</summary>
    public static byte[]? Encode(Image img)
    {
        if (img.IsCompressed()) return null;
        if (img.GetFormat() != Image.Format.Rgba8)
        {
            img.Convert(Image.Format.Rgba8);
            if (img.GetFormat() != Image.Format.Rgba8) return null;
        }
        int w = img.GetWidth(), h = img.GetHeight();
        if (w <= 0 || h <= 0) return null;
        byte[] src = img.GetData();
        int bw = (w + 3) / 4, bh = (h + 3) / 4;
        var dst = new byte[bw * bh * 16];
        var px = new byte[16 * 4];   // bloque 4x4 RGBA (clamp en bordes)
        int o = 0;
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                for (int y = 0; y < 4; y++)
                {
                    int sy = Math.Min(by * 4 + y, h - 1);
                    for (int x = 0; x < 4; x++)
                    {
                        int sx = Math.Min(bx * 4 + x, w - 1);
                        int si = (sy * w + sx) * 4, di = (y * 4 + x) * 4;
                        px[di] = src[si]; px[di + 1] = src[si + 1];
                        px[di + 2] = src[si + 2]; px[di + 3] = src[si + 3];
                    }
                }
                EncodeAlphaBlock(px, dst, o);
                EncodeColorBlock(px, dst, o + 8);
                o += 16;
            }
        return dst;
    }

    private static void EncodeAlphaBlock(byte[] px, byte[] dst, int o)
    {
        byte amin = 255, amax = 0;
        for (int i = 0; i < 16; i++)
        {
            byte a = px[i * 4 + 3];
            if (a < amin) amin = a;
            if (a > amax) amax = a;
        }
        dst[o] = amax; dst[o + 1] = amin;   // a0 > a1 => modo de 8 niveles
        if (amax == amin) return;            // índices en 0 = a0, exacto

        Span<int> lut = stackalloc int[8];
        lut[0] = amax; lut[1] = amin;
        for (int i = 1; i <= 6; i++) lut[i + 1] = ((7 - i) * amax + i * amin) / 7;

        ulong bits = 0;
        for (int i = 0; i < 16; i++)
        {
            int a = px[i * 4 + 3], best = 0, bd = int.MaxValue;
            for (int k = 0; k < 8; k++)
            {
                int d = Math.Abs(a - lut[k]);
                if (d < bd) { bd = d; best = k; }
            }
            bits |= (ulong)best << (3 * i);
        }
        for (int b = 0; b < 6; b++) dst[o + 2 + b] = (byte)(bits >> (8 * b));
    }

    private static void EncodeColorBlock(byte[] px, byte[] dst, int o)
    {
        int rmin = 255, gmin = 255, bmin = 255, rmax = 0, gmax = 0, bmax = 0;
        for (int i = 0; i < 16; i++)
        {
            int r = px[i * 4], g = px[i * 4 + 1], b = px[i * 4 + 2];
            if (r < rmin) rmin = r; if (r > rmax) rmax = r;
            if (g < gmin) gmin = g; if (g > gmax) gmax = g;
            if (b < bmin) bmin = b; if (b > bmax) bmax = b;
        }
        // inset 1/16 de la caja: menos ringing en gradientes (técnica stb_dxt)
        int ir = (rmax - rmin) >> 4, ig = (gmax - gmin) >> 4, ib = (bmax - bmin) >> 4;
        rmin = Math.Min(255, rmin + ir); rmax = Math.Max(0, rmax - ir);
        gmin = Math.Min(255, gmin + ig); gmax = Math.Max(0, gmax - ig);
        bmin = Math.Min(255, bmin + ib); bmax = Math.Max(0, bmax - ib);

        ushort c0 = To565(rmax, gmax, bmax), c1 = To565(rmin, gmin, bmin);
        if (c0 < c1) (c0, c1) = (c1, c0);   // c0 > c1 = modo 4 colores (con alpha aparte)
        dst[o] = (byte)c0; dst[o + 1] = (byte)(c0 >> 8);
        dst[o + 2] = (byte)c1; dst[o + 3] = (byte)(c1 >> 8);
        if (c0 == c1) return;                // índices en 0, color exacto

        // paleta expandida 565->888 (como la ve el hardware)
        Span<int> pr = stackalloc int[4];
        Span<int> pg = stackalloc int[4];
        Span<int> pb = stackalloc int[4];
        From565(c0, out pr[0], out pg[0], out pb[0]);
        From565(c1, out pr[1], out pg[1], out pb[1]);
        pr[2] = (2 * pr[0] + pr[1]) / 3; pg[2] = (2 * pg[0] + pg[1]) / 3; pb[2] = (2 * pb[0] + pb[1]) / 3;
        pr[3] = (pr[0] + 2 * pr[1]) / 3; pg[3] = (pg[0] + 2 * pg[1]) / 3; pb[3] = (pb[0] + 2 * pb[1]) / 3;

        uint bits = 0;
        for (int i = 0; i < 16; i++)
        {
            int r = px[i * 4], g = px[i * 4 + 1], b = px[i * 4 + 2];
            int best = 0, bd = int.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                int dr = r - pr[k], dg = g - pg[k], db = b - pb[k];
                int d = dr * dr + dg * dg + db * db;
                if (d < bd) { bd = d; best = k; }
            }
            bits |= (uint)best << (2 * i);
        }
        dst[o + 4] = (byte)bits; dst[o + 5] = (byte)(bits >> 8);
        dst[o + 6] = (byte)(bits >> 16); dst[o + 7] = (byte)(bits >> 24);
    }

    private static ushort To565(int r, int g, int b)
        => (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));

    private static void From565(ushort c, out int r, out int g, out int b)
    {
        int r5 = (c >> 11) & 31, g6 = (c >> 5) & 63, b5 = c & 31;
        r = (r5 << 3) | (r5 >> 2);
        g = (g6 << 2) | (g6 >> 4);
        b = (b5 << 3) | (b5 >> 2);
    }
}
