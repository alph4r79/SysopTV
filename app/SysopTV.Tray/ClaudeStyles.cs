// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// The four styles that only exist as pictures: LED-Matrix, Hell, Fallblatt
// and 4-Ton-Grün. Like the firmware designs they show "nicht aktiv", the
// stamp for old values, the login screen and used or remaining.
//
// Colours follow the rule for the panel (see CLAUDE.md): dark backgrounds
// pure black, text on light backgrounds black or at most (70,70,70), and at
// least 8 px between content and the edge of the picture.

using System;

namespace SysopTV.Tray
{
    internal static partial class ClaudeDesigns
    {
        // Umlauts where the font has them: "Reset in 2:17", "Reset fällig".
        private static string NiceReset(ViewColumn c)
        {
            if (c.Bottom.StartsWith("noch ", StringComparison.Ordinal))
            {
                return "Reset in " + c.Bottom.Substring(5);
            }
            return c.ResetLine.Replace("faellig", "fällig");
        }

        private static string PctSpaced(ViewColumn c)
        {
            return c.Present ? c.Percent + " %" : "--";
        }

        // ---- LED-Matrix: big grid 6 px, small grid 3 px ----

        private const int LedBig = 6;
        private const int LedSmall = 3;

        // "RESET 3:55" or a short text for the small grid.
        private static string LedReset(ViewColumn s)
        {
            if (s.Bottom.StartsWith("noch ", StringComparison.Ordinal))
            {
                return "RESET " + s.Bottom.Substring(5);
            }
            if (s.Bottom.StartsWith("Reset", StringComparison.Ordinal))
            {
                return "RESET FÄLLIG";
            }
            return s.Bottom.ToUpperInvariant();
        }

        // Heading and reset in the small 3x5 font, the week in the 5x7 font, the
        // session big; all rows spread evenly from top to bottom.
        private static Canvas LedMatrix(ViewModel v)
        {
            var c = new Canvas();
            int tiny = Led.TinyHeight(LedSmall), small = Led.SmallHeight(LedSmall);
            if (v.Screen == ViewScreen.Login)
            {
                int[] t = Led.Spread(new[] { small, small, tiny }, new[] { LedSmall, LedSmall, LedSmall });
                Led.Small(c, "ANMELDUNG", 120, t[0], LedSmall, Led.WarnOn);
                Led.Small(c, "ABGELAUFEN", 120, t[1], LedSmall, Led.WarnOn);
                Led.Tiny(c, v.Clock, 120, t[2], LedSmall, Led.Dim);
                return c;
            }
            ViewColumn s = v.Session, w = v.Week;
            // Old values: the stamp takes the place of the heading.
            string top = v.Stamp != null ? v.Stamp : (v.ShowUsed ? "SESSION" : "ÜBRIG");
            int[] tops = Led.Spread(new[] { tiny, Led.BigHeight(Pct(s), LedBig), small, tiny }, new[] { LedSmall, LedBig, LedSmall, LedSmall });
            Led.Tiny(c, top, 120, tops[0], LedSmall, v.Stamp != null ? Led.WarnDim : Led.Dim);
            Led.Big(c, Pct(s), 120, tops[1], LedBig, s.Warn ? Led.WarnOn : Led.On);
            Led.Small(c, "WOCHE " + Pct(w), 120, tops[2], LedSmall, w.Warn ? Led.WarnDim : Led.Dim);
            Led.Tiny(c, LedReset(s), 120, tops[3], LedSmall, Led.Dim);
            return c;
        }

        // ---- Hell ----

        internal static readonly ushort HellBarBack = Canvas.Rgb(190, 190, 185);
        internal static readonly ushort HellBar = Canvas.Rgb(217, 119, 87);
        internal static readonly ushort HellWarn = Canvas.Rgb(200, 40, 20);
        private const int HellBarHeight = 8;

        private static Canvas Hell(ViewModel v)
        {
            var c = new Canvas();
            c.Clear(ShopBig.HellBack);
            ushort text = ShopBig.HellText;
            TtfText.Draw(c, "Claude", TtfFace.InterRegular, 10, 14, 12, Align.Left, text);
            TtfText.Draw(c, v.Stamp ?? v.Clock, TtfFace.InterRegular, 10, 226, 12, Align.Right, text);
            if (v.Screen == ViewScreen.Login)
            {
                TtfText.Draw(c, "Anmeldung abgelaufen", TtfFace.InterBold, 14, 120, 90, Align.Center, HellWarn);
                TtfText.Draw(c, "Claude Code kurz benutzen", TtfFace.InterRegular, 11, 120, 120, Align.Center, text);
                return c;
            }
            ViewColumn s = v.Session, w = v.Week;
            string mode = v.ShowUsed ? "verbraucht" : "übrig";
            TtfText.Draw(c, "Session", TtfFace.InterRegular, 10, 14, 36, Align.Left, text);
            TtfText.Draw(c, mode, TtfFace.InterRegular, 10, 226, 36, Align.Right, ShopBig.HellDim);
            TtfText.Draw(c, PctSpaced(s), TtfFace.InterBold, 40, 14, 54, Align.Left, s.Warn ? HellWarn : text);
            HellBarAt(c, 104, s);
            TtfText.Draw(c, NiceReset(s), TtfFace.InterRegular, 10, 14, 122, Align.Left, text);
            c.FillRect(14, 144, 212, 1, ShopBig.HellLine);
            TtfText.Draw(c, "Woche", TtfFace.InterRegular, 13, 14, 158, Align.Left, text);
            TtfText.Draw(c, PctSpaced(w), TtfFace.InterBold, 13, 226, 158, Align.Right, w.Warn ? HellWarn : text);
            HellBarAt(c, 180, w);
            TtfText.Draw(c, NiceReset(w), TtfFace.InterRegular, 10, 14, 198, Align.Left, text);
            return c;
        }

        private static void HellBarAt(Canvas c, int y, ViewColumn col)
        {
            const int h = HellBarHeight;
            c.FillRoundRect(14, y, 212, h, h / 2, HellBarBack);
            int fill = col.Present ? 212 * col.Percent / 100 : 0;
            if (fill >= h)
            {
                c.FillRoundRect(14, y, fill, h, h / 2, col.Warn ? HellWarn : HellBar);
            }
            else if (fill > 0)
            {
                c.FillRect(14, y, fill, h, col.Warn ? HellWarn : HellBar);
            }
        }

        // ---- Fallblatt ----

        private static Canvas Fallblatt(ViewModel v)
        {
            var c = new Canvas();
            TtfText.Draw(c, "SESSION", TtfFace.MonoBold, 9, 12, 10, Align.Left, Flap.Label);
            TtfText.Draw(c, v.Stamp ?? v.Clock, TtfFace.MonoBold, 9, 228, 10, Align.Right, Flap.Label);
            if (v.Screen == ViewScreen.Login)
            {
                TtfText.Draw(c, "ANMELDUNG", TtfFace.MonoBold, 16, 120, 80, Align.Center, Flap.Warn);
                TtfText.Draw(c, "ABGELAUFEN", TtfFace.MonoBold, 16, 120, 108, Align.Center, Flap.Warn);
                TtfText.Draw(c, "Claude Code starten", TtfFace.MonoBold, 10, 120, 150, Align.Center, Flap.Label);
                return c;
            }
            ViewColumn s = v.Session, w = v.Week;
            int h = Flap.FitHeight(Pct(s), 220, 84);
            Flap.Cards(c, Pct(s), 120, 28, h, s.Warn ? Flap.Warn : Flap.Main);
            TtfText.Draw(c, ModeUpper(v.ShowUsed), TtfFace.MonoBold, 9, 120, 122, Align.Center, Flap.Label);
            TtfText.Draw(c, "WOCHE", TtfFace.MonoBold, 9, 12, 146, Align.Left, Flap.Label);
            Flap.Cards(c, Pct(w), 120, 162, 44, w.Warn ? Flap.Warn : Flap.Second);
            TtfText.Draw(c, s.ResetLine, TtfFace.MonoBold, 10, 120, 218, Align.Center, Flap.Second);
            return c;
        }

        // ---- 4-Ton-Grün ----

        private static Canvas Green(ViewModel v)
        {
            var c = new Canvas();
            ShopBig.GreenFrame(c);
            ushort g0 = ShopBig.G0, g1 = ShopBig.G1, g2 = ShopBig.G2, g3 = ShopBig.G3;
            c.Text("CLAUDE", 16, 16, Align.Left, 1, g0, g3);
            c.Text(v.Stamp ?? v.Clock, 224, 16, Align.Right, 1, g1, g3);
            if (v.Screen == ViewScreen.Login)
            {
                c.Text("ANMELDUNG", 120, 70, Align.Center, 2, g0, g3);
                c.Text("ABGELAUFEN", 120, 108, Align.Center, 2, g0, g3);
                c.Text("Claude Code starten", 120, 160, Align.Center, 1, g1, g3);
                return c;
            }
            ViewColumn s = v.Session, w = v.Week;
            c.Text(ModeUpper(v.ShowUsed), 120, 34, Align.Center, 1, g1, g3);
            // Warning: the value blinks, one second on, one second off.
            if (!(s.Warn && v.BlinkOff))
            {
                c.Text(Pct(s), 120, 52, Align.Center, 4, g0, g3);
            }
            GreenBlocks(c, 120, 14, s, g0, g2);
            c.Text("WOCHE " + Pct(w), 120, 144, Align.Center, 1, (w.Warn && v.BlinkOff) ? g3 : g0, g3);
            GreenBlocks(c, 164, 8, w, g0, g2);
            c.Text(s.ResetLine, 120, 180, Align.Center, 1, g0, g3);
            c.Text(w.ResetLine, 120, 200, Align.Center, 1, g1, g3);
            return c;
        }

        private static void GreenBlocks(Canvas c, int y, int h, ViewColumn col, ushort on, ushort off)
        {
            const int n = 10, bw = 18, gap = 3;
            int x = 120 - (n * bw + (n - 1) * gap) / 2;
            for (int i = 0; i < n; i++)
            {
                c.FillRect(x + i * (bw + gap), y, bw, h, i < col.Segments ? on : off);
            }
        }
    }
}
