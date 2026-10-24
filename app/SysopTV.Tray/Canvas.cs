// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// 240x240 drawing surface in RGB565, the same pixel format as the display of
// the cube. Text in the VGA font works like vgaText() of the firmware
// (view_common.cpp), text in the TFT_eSPI fonts like drawString() there.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SysopTV.Tray
{
    internal enum Align { Left, Center, Right }

    // Text datum of TFT_eSPI: where x and y sit on the text.
    internal enum Datum { TL, TC, TR, ML, MC, MR }

    internal sealed class Canvas
    {
        internal const int Size = 240;
        internal readonly ushort[] Pixels = new ushort[Size * Size];
        private int clipX0, clipY0, clipX1 = Size, clipY1 = Size;

        // Same conversion as sysoptvRgb() in config.h of the firmware.
        internal static ushort Rgb(int r, int g, int b)
        {
            return (ushort)(((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3));
        }

        internal Canvas Clone()
        {
            var c = new Canvas();
            Array.Copy(Pixels, c.Pixels, Pixels.Length);
            c.Cards.AddRange(Cards);
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
            int x0 = Math.Max(clipX0, x), y0 = Math.Max(clipY0, y);
            int x1 = Math.Min(clipX1, x + w), y1 = Math.Min(clipY1, y + h);
            for (int yy = y0; yy < y1; yy++)
            {
                for (int xx = x0; xx < x1; xx++)
                {
                    Pixels[yy * Size + xx] = colour;
                }
            }
        }

        // Drawing only inside this area, like setViewport(..., false) in the firmware.
        internal void SetClip(int x, int y, int w, int h)
        {
            clipX0 = Math.Max(0, x);
            clipY0 = Math.Max(0, y);
            clipX1 = Math.Min(Size, x + w);
            clipY1 = Math.Min(Size, y + h);
        }

        internal void ResetClip()
        {
            clipX0 = clipY0 = 0;
            clipX1 = clipY1 = Size;
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

        // VGA text in double width (16 x 16 pixels per character), for the receipt.
        internal void TextWide(string s, int x, int y, Align align, ushort fg, ushort bg)
        {
            int left = Left(x, s.Length * 16, align);
            for (int g = 0; g < s.Length; g++)
            {
                byte[] glyph = VgaFont.Glyphs[Glyph(s[g])];
                for (int row = 0; row < 16; row++)
                {
                    for (int b = 0; b < 8; b++)
                    {
                        FillRect(left + g * 16 + b * 2, y + row, 2, 1, (glyph[row] & (0x80 >> b)) != 0 ? fg : bg);
                    }
                }
            }
        }

        // Like vgaTextTransparent(): only the set pixels, no clipping to the screen width.
        internal void TextTransparent(string s, int x, int y, Align align, int scale, ushort fg)
        {
            int cw = 8 * scale;
            int left = Left(x, s.Length * cw, align);
            for (int g = 0; g < s.Length; g++)
            {
                byte[] glyph = VgaFont.Glyphs[Glyph(s[g])];
                for (int row = 0; row < 16; row++)
                {
                    for (int b = 0; b < 8; b++)
                    {
                        if ((glyph[row] & (0x80 >> b)) != 0)
                        {
                            FillRect(left + g * cw + b * scale, y + row * scale, scale, scale, fg);
                        }
                    }
                }
            }
        }

        // Patterns of the VGA characters 0xB0 to 0xB2, like shadeSet() in view_common.cpp.
        private static bool ShadeSet(int level, int px, int py)
        {
            bool light = (py & 1) != 0 ? (px & 3) == 0 : (px & 3) == 2;
            switch (level)
            {
                case 1: return light;
                case 2: return ((px + py) & 1) == 0;
                case 3: return !light;
                default: return true;
            }
        }

        // Like fillShade(): level 0 background, 1 to 3 the shades, 4 full; cell is the dot size.
        internal void FillShade(int x, int y, int w, int h, int level, int cell, ushort fg, ushort bg)
        {
            if (w <= 0 || h <= 0)
            {
                return;
            }
            if (level >= 4 || level <= 0)
            {
                FillRect(x, y, w, h, level >= 4 ? fg : bg);
                return;
            }
            cell = Math.Max(1, cell);
            for (int row = 0; row < h; row++)
            {
                for (int i = 0; i < w; i++)
                {
                    FillRect(x + i, y + row, 1, 1, ShadeSet(level, i / cell, row / cell) ? fg : bg);
                }
            }
        }

        // Width like textWidth() of TFT_eSPI.
        internal static int TftWidth(string s, TftFont font, int size)
        {
            int w = 0;
            foreach (char c in s)
            {
                w += font.Width(c);
            }
            return w * size;
        }

        // Like drawString() of TFT_eSPI with a background colour: every character
        // cell is filled completely, size enlarges each pixel.
        internal void Tft(string s, int x, int y, TftFont font, int size, ushort fg, ushort bg, Datum datum)
        {
            int w = TftWidth(s, font, size);
            int h = font.Height * size;
            if (datum == Datum.TC || datum == Datum.MC)
            {
                x -= w / 2;
            }
            else if (datum == Datum.TR || datum == Datum.MR)
            {
                x -= w;
            }
            if (datum == Datum.ML || datum == Datum.MC || datum == Datum.MR)
            {
                y -= h / 2;
            }
            foreach (char c in s)
            {
                int cw = font.Width(c);
                for (int row = 0; row < font.Height; row++)
                {
                    for (int col = 0; col < cw; col++)
                    {
                        FillRect(x + col * size, y + row * size, size, size, font.Pixel(c, col, row) ? fg : bg);
                    }
                }
                x += cw * size;
            }
        }

        // Flip cards of the style Fallblatt, for the flip animation (FlipAnimation).
        internal readonly List<Rectangle> Cards = new List<Rectangle>();

        private static int Expand(ushort p)
        {
            int r = (p >> 11) & 0x1F, g = (p >> 5) & 0x3F, b = p & 0x1F;
            return unchecked((int)0xFF000000) | ((r * 255 / 31) << 16) | ((g * 255 / 63) << 8) | (b * 255 / 31);
        }

        // Fills an outline with anti aliasing: the area is copied out, drawn with
        // GDI+ and written back in RGB565.
        internal void FillPath(GraphicsPath path, ushort colour)
        {
            RectangleF bf = path.GetBounds();
            var area = Rectangle.FromLTRB((int)Math.Floor(bf.Left) - 1, (int)Math.Floor(bf.Top) - 1,
                                          (int)Math.Ceiling(bf.Right) + 1, (int)Math.Ceiling(bf.Bottom) + 1);
            area.Intersect(new Rectangle(clipX0, clipY0, clipX1 - clipX0, clipY1 - clipY0));
            if (area.Width <= 0 || area.Height <= 0)
            {
                return;
            }
            using (var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb))
            {
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, area.Width, area.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                var row = new int[area.Width];
                for (int y = 0; y < area.Height; y++)
                {
                    for (int x = 0; x < area.Width; x++)
                    {
                        row[x] = Expand(Pixels[(area.Y + y) * Size + area.X + x]);
                    }
                    Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, area.Width);
                }
                bmp.UnlockBits(data);
                using (Graphics g = Graphics.FromImage(bmp))
                using (var brush = new SolidBrush(Color.FromArgb(Expand(colour))))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.TranslateTransform(-area.X, -area.Y);
                    g.FillPath(brush, path);
                }
                data = bmp.LockBits(new Rectangle(0, 0, area.Width, area.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                for (int y = 0; y < area.Height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, area.Width);
                    for (int x = 0; x < area.Width; x++)
                    {
                        int v = row[x];
                        Pixels[(area.Y + y) * Size + area.X + x] = Rgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
                    }
                }
                bmp.UnlockBits(data);
            }
        }

        // Rectangle with rounded corners, anti aliased.
        internal void FillRoundRect(int x, int y, int w, int h, int radius, ushort colour)
        {
            using (var path = new GraphicsPath())
            {
                int d = radius * 2;
                path.AddArc(x, y, d, d, 180, 90);
                path.AddArc(x + w - d, y, d, d, 270, 90);
                path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
                path.AddArc(x, y + h - d, d, d, 90, 90);
                path.CloseFigure();
                FillPath(path, colour);
            }
        }

        // A round dot of the given diameter with its top left corner at x, y,
        // drawn pixel by pixel so it looks the same at every position.
        internal void Dot(int x, int y, int diameter, ushort colour)
        {
            if (diameter <= 2)
            {
                FillRect(x, y, diameter, diameter, colour);
                return;
            }
            double r = diameter / 2.0;
            for (int dy = 0; dy < diameter; dy++)
            {
                for (int dx = 0; dx < diameter; dx++)
                {
                    double cx = dx + 0.5 - r, cy = dy + 0.5 - r;
                    if (cx * cx + cy * cy <= r * r + 0.3)
                    {
                        FillRect(x + dx, y + dy, 1, 1, colour);
                    }
                }
            }
        }

        // VGA text with separate horizontal and vertical scale, like the double
        // height print of a receipt printer.
        internal void TextScaled(string s, int x, int y, Align align, int sx, int sy, ushort fg, ushort bg)
        {
            int left = Left(x, s.Length * 8 * sx, align);
            for (int g = 0; g < s.Length; g++)
            {
                byte[] glyph = VgaFont.Glyphs[Glyph(s[g])];
                for (int row = 0; row < 16; row++)
                {
                    for (int b = 0; b < 8; b++)
                    {
                        FillRect(left + (g * 8 + b) * sx, y + row * sy, sx, sy, (glyph[row] & (0x80 >> b)) != 0 ? fg : bg);
                    }
                }
            }
        }

        // Glyph bitmap of one character, for the LED matrix.
        internal static byte[] GlyphRows(char c)
        {
            return VgaFont.Glyphs[Glyph(c)];
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
