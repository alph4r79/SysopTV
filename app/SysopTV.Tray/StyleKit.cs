// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Drawing helpers for the styles LED-Matrix, Fallblatt and the big VGA
// numbers, shared by the Claude styles and the shop big display.

using System;
using System.Drawing;

namespace SysopTV.Tray
{
    // LED matrix: only lit round dots on pure black, nothing for the dots that
    // are off (the panel would show them as olive). One grid for the whole
    // picture: values in the big grid with the VGA font, one font pixel per
    // dot, set proportionally; labels in a grid exactly half as big with the
    // 5x7 font of the GLCD set. All dots sit on multiples of the small pitch.
    internal static class Led
    {
        internal static readonly ushort On = Canvas.Rgb(255, 40, 0);
        internal static readonly ushort Dim = Canvas.Rgb(170, 30, 0);
        internal static readonly ushort WarnOn = Canvas.Rgb(255, 190, 0);
        internal static readonly ushort WarnDim = Canvas.Rgb(170, 125, 0);
        private const int SpaceDots = 3;

        // Dot size for a pitch: 6 px pitch gives 5 px dots, 4 gives 3, 3 gives 2.
        internal static int DotSize(int pitch)
        {
            return Math.Max(1, pitch - 1);
        }

        private static int Snap(int v, int pitch)
        {
            return (int)Math.Round(v / (double)pitch) * pitch;
        }

        // ---- values: VGA font, proportional ----

        private static bool Columns(char ch, out int first, out int last)
        {
            byte[] g = Canvas.GlyphRows(ch);
            first = 8;
            last = -1;
            for (int col = 0; col < 8; col++)
            {
                foreach (byte row in g)
                {
                    if ((row & (0x80 >> col)) != 0)
                    {
                        first = Math.Min(first, col);
                        last = Math.Max(last, col);
                    }
                }
            }
            return last >= 0;
        }

        private static void Rows(string s, out int top, out int bottom)
        {
            BigVga.Rows(s, out top, out bottom);
        }

        private static int BigDots(string s)
        {
            int n = 0;
            foreach (char ch in s)
            {
                int first, last;
                n += Columns(ch, out first, out last) ? last - first + 2 : SpaceDots;
            }
            return Math.Max(0, n - 1);
        }

        internal static int BigWidth(string s, int pitch)
        {
            int dots = BigDots(s);
            return dots == 0 ? 0 : (dots - 1) * pitch + DotSize(pitch);
        }

        internal static int BigHeight(string s, int pitch)
        {
            int top, bottom;
            Rows(s, out top, out bottom);
            return (bottom - top) * pitch + DotSize(pitch);
        }

        // Draws a value centred at cx, its top row at y (both snapped to the grid).
        internal static void Big(Canvas c, string s, int cx, int y, int pitch, ushort colour)
        {
            int top, bottom;
            Rows(s, out top, out bottom);
            int d = DotSize(pitch);
            int x0 = Snap(cx - BigWidth(s, pitch) / 2, pitch);
            y = Snap(y, pitch);
            int dot = 0;
            foreach (char ch in s)
            {
                int first, last;
                if (!Columns(ch, out first, out last))
                {
                    dot += SpaceDots;
                    continue;
                }
                byte[] g = Canvas.GlyphRows(ch);
                for (int col = first; col <= last; col++)
                {
                    for (int r = top; r <= bottom; r++)
                    {
                        if ((g[r] & (0x80 >> col)) != 0)
                        {
                            c.Dot(x0 + (dot + col - first) * pitch, y + (r - top) * pitch, d, colour);
                        }
                    }
                }
                dot += last - first + 2;
            }
        }

        // ---- labels: 5x7 font, 6 dots per character ----

        private static string Plain(string s)
        {
            return s.Replace("ß", "SS");
        }

        internal static int SmallWidth(string s, int pitch)
        {
            int n = Plain(s).Length * 6 - 1;
            return n <= 0 ? 0 : (n - 1) * pitch + DotSize(pitch);
        }

        internal static int SmallHeight(int pitch)
        {
            return 6 * pitch + DotSize(pitch);
        }

        // Draws a label centred at cx, top at y. Umlauts get their two dots
        // one row above the capital.
        internal static void Small(Canvas c, string s, int cx, int y, int pitch, ushort colour)
        {
            s = Plain(s);
            int d = DotSize(pitch);
            int x0 = Snap(cx - SmallWidth(s, pitch) / 2, pitch);
            y = Snap(y, pitch);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                bool umlaut = "ÄÖÜäöü".IndexOf(ch) >= 0;
                char baseChar = umlaut ? "AOUaou"["ÄÖÜäöü".IndexOf(ch)] : ch;
                int cellX = x0 + i * 6 * pitch;
                for (int row = 0; row < 7; row++)
                {
                    for (int col = 0; col < 5; col++)
                    {
                        if (baseChar >= 32 && baseChar < 128 && TftFonts.Glcd.Pixel(baseChar, col, row))
                        {
                            c.Dot(cellX + col * pitch, y + row * pitch, d, colour);
                        }
                    }
                }
                if (umlaut)
                {
                    int row = char.IsUpper(ch) ? -1 : 0;
                    c.Dot(cellX + 1 * pitch, y + row * pitch, d, colour);
                    c.Dot(cellX + 3 * pitch, y + row * pitch, d, colour);
                }
            }
        }
    }

    // Split flap display: one card per character, JetBrains Mono Bold on it.
    internal static class Flap
    {
        // Pure black around the cards, the panel turns dark grey into blue grey.
        internal static readonly ushort Back = 0;
        internal static readonly ushort Card = Canvas.Rgb(55, 55, 55);
        internal static readonly ushort Split = 0;
        internal static readonly ushort Main = Canvas.Rgb(240, 240, 230);
        internal static readonly ushort Second = Canvas.Rgb(240, 190, 60);
        internal static readonly ushort Label = Canvas.Rgb(200, 200, 200);
        internal static readonly ushort Warn = Canvas.Rgb(255, 90, 60);
        private const double Aspect = 0.64;   // card width to card height
        private const double Glyph = 0.62;    // digit height to card height

        internal static int Gap(int cardWidth)
        {
            return Math.Max(2, cardWidth / 10);
        }

        // Point, comma and space sit between the cards, a third of a card wide.
        private static bool Narrow(char ch)
        {
            return ch == '.' || ch == ',' || ch == ' ';
        }

        private static int TotalWidth(string s, int w)
        {
            int total = 0;
            foreach (char ch in s)
            {
                total += (Narrow(ch) ? w / 3 : w) + Gap(w);
            }
            return total - Gap(w);
        }

        // Largest card height for s in the box.
        internal static int FitHeight(string s, int maxWidth, int maxHeight)
        {
            int h = maxHeight;
            while (h > 12 && TotalWidth(s, (int)(h * Aspect)) > maxWidth)
            {
                h--;
            }
            return h;
        }

        // Cards for s, centred at cx, top at y. Every card is noted for the flip animation.
        internal static void Cards(Canvas c, string s, int cx, int y, int height, ushort fg)
        {
            int w = (int)(height * Aspect);
            int gap = Gap(w);
            int x = cx - TotalWidth(s, w) / 2;
            int radius = Math.Max(2, w / 8);
            float glyph = (float)(height * Glyph);
            foreach (char ch in s)
            {
                if (Narrow(ch))
                {
                    if (ch != ' ')
                    {
                        // The separator sits low between the cards, like on a station board.
                        float sh = glyph * 0.9f;
                        TtfText.Draw(c, ch.ToString(), TtfFace.MonoBold, sh, x + w / 6, y + (int)((height - glyph) / 2), Align.Center, fg);
                    }
                    x += w / 3 + gap;
                    continue;
                }
                c.FillRoundRect(x, y, w, height, radius, Card);
                string t = ch.ToString();
                float h = Math.Min(glyph, TtfText.Fit(t, TtfFace.MonoBold, glyph, w - 4));
                TtfText.Draw(c, t, TtfFace.MonoBold, h, x + w / 2, y + (int)((height - h) / 2), Align.Center, fg);
                c.FillRect(x, y + height / 2 - 1, w, Math.Max(1, height / 40 + 1), Split);
                c.Cards.Add(new Rectangle(x, y, w, height));
                x += w + gap;
            }
        }

        internal static void FitCards(Canvas c, string s, Rectangle box, ushort fg)
        {
            int h = FitHeight(s, box.Width, box.Height);
            Cards(c, s, box.X + box.Width / 2, box.Y + (box.Height - h) / 2, h, fg);
        }
    }

    // Big VGA text: only the rows the text uses (digits use rows 2 to 11),
    // scaled separately in width and height, at most twice as high as wide.
    internal static class BigVga
    {
        internal static void Rows(string s, out int top, out int bottom)
        {
            top = 15;
            bottom = 0;
            foreach (char ch in s)
            {
                byte[] g = Canvas.GlyphRows(ch);
                for (int r = 0; r < 16; r++)
                {
                    if (g[r] != 0)
                    {
                        top = Math.Min(top, r);
                        bottom = Math.Max(bottom, r);
                    }
                }
            }
            if (bottom < top)
            {
                top = 2;
                bottom = 11;
            }
        }

        internal static void Scale(string s, int maxWidth, int maxHeight, out int sx, out int sy)
        {
            int top, bottom;
            Rows(s, out top, out bottom);
            int n = Math.Max(1, s.Length);
            sy = Math.Max(1, maxHeight / (bottom - top + 1));
            sx = Math.Max(1, Math.Min(sy, maxWidth / (8 * n)));
            sy = Math.Min(sy, 2 * sx);
        }

        // Height of the digits when drawn into the box.
        internal static int DigitHeight(string s, int maxWidth, int maxHeight)
        {
            int sx, sy;
            Scale(s, maxWidth, maxHeight, out sx, out sy);
            return 10 * sy;
        }

        internal static void FitText(Canvas c, string s, Rectangle box, ushort fg)
        {
            int sx, sy;
            Scale(s, box.Width, box.Height, out sx, out sy);
            int top, bottom;
            Rows(s, out top, out bottom);
            int w = s.Length * 8 * sx;
            int x = box.X + (box.Width - w) / 2;
            int y = box.Y + (box.Height - (bottom - top + 1) * sy) / 2;
            for (int g = 0; g < s.Length; g++)
            {
                byte[] glyph = Canvas.GlyphRows(s[g]);
                for (int row = top; row <= bottom; row++)
                {
                    for (int b = 0; b < 8; b++)
                    {
                        if ((glyph[row] & (0x80 >> b)) != 0)
                        {
                            c.FillRect(x + (g * 8 + b) * sx, y + (row - top) * sy, sx, sy, fg);
                        }
                    }
                }
            }
        }
    }
}
