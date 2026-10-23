// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// 240x240 drawing surface in RGB565, the same pixel format as the display of
// the cube. Text uses the VGA font like the firmware (view_common.cpp).

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SysopTV.Tray
{
    internal enum Align { Left, Center, Right }

    internal sealed class Canvas
    {
        internal const int Size = 240;
        internal readonly ushort[] Pixels = new ushort[Size * Size];

        // Same conversion as sysoptvRgb() in config.h of the firmware.
        internal static ushort Rgb(int r, int g, int b)
        {
            return (ushort)(((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3));
        }

        internal Canvas Clone()
        {
            var c = new Canvas();
            Array.Copy(Pixels, c.Pixels, Pixels.Length);
            return c;
        }

        internal void Clear(ushort colour)
        {
            for (int i = 0; i < Pixels.Length; i++)
            {
                Pixels[i] = colour;
            }
        }

        internal void FillRect(int x, int y, int w, int h, ushort colour)
        {
            int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
            int x1 = Math.Min(Size, x + w), y1 = Math.Min(Size, y + h);
            for (int yy = y0; yy < y1; yy++)
            {
                for (int xx = x0; xx < x1; xx++)
                {
                    Pixels[yy * Size + xx] = colour;
                }
            }
        }

        internal void DrawRect(int x, int y, int w, int h, ushort colour)
        {
            FillRect(x, y, w, 1, colour);
            FillRect(x, y + h - 1, w, 1, colour);
            FillRect(x, y, 1, h, colour);
            FillRect(x + w - 1, y, 1, h, colour);
        }

        // Two nested rectangles like drawDoubleFrame() in the firmware.
        internal void DoubleFrame(int x, int y, int w, int h, ushort colour)
        {
            DrawRect(x, y, w, h, colour);
            DrawRect(x + 3, y + 3, w - 6, h - 6, colour);
        }

        private static int Glyph(char c)
        {
            if (c >= 32 && c < 127)
            {
                return c - 32;
            }
            int i = Array.IndexOf(VgaFont.Extra, c);
            return i >= 0 ? 95 + i : '?' - 32;
        }

        internal static int TextWidth(string s, int scale)
        {
            return s.Length * 8 * scale;
        }

        // Like vgaText() in view_common.cpp: y is the top edge, clearWidth > 0
        // clears that width (aligned like the text) with bg first.
        internal void Text(string s, int x, int y, Align align, int scale, ushort fg, ushort bg, int clearWidth = 0)
        {
            int n = s.Length;
            int cw = 8 * scale;
            while (n > 0 && n * cw > Size)
            {
                n--;
            }
            int w = n * cw;
            int left = Left(x, w, align);
            if (left < 0)
            {
                left = 0;
            }
            if (left + w > Size)
            {
                left = Size - w;
            }
            if (clearWidth > w)
            {
                int cl = Left(x, clearWidth, align);
                FillRect(cl, y, clearWidth, 16 * scale, bg);
            }
            for (int g = 0; g < n; g++)
            {
                byte[] glyph = VgaFont.Glyphs[Glyph(s[g])];
                for (int row = 0; row < 16; row++)
                {
                    for (int b = 0; b < 8; b++)
                    {
                        ushort c = (glyph[row] & (0x80 >> b)) != 0 ? fg : bg;
                        FillRect(left + g * cw + b * scale, y + row * scale, scale, scale, c);
                    }
                }
            }
        }

        private static int Left(int x, int w, Align align)
        {
            if (align == Align.Center)
            {
                return x - w / 2;
            }
            return align == Align.Right ? x - w : x;
        }

        internal Bitmap ToBitmap(int scale)
        {
            int s = Size * scale;
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var row = new int[s];
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    ushort p = Pixels[(y / scale) * Size + x / scale];
                    int r = (p >> 11) & 0x1F, g = (p >> 5) & 0x3F, b = p & 0x1F;
                    row[x] = unchecked((int)0xFF000000) | ((r * 255 / 31) << 16) | ((g * 255 / 63) << 8) | (b * 255 / 31);
                }
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, s);
            }
            bmp.UnlockBits(data);
            return bmp;
        }
    }
}
