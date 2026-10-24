// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// "Claude Limits": the six designs of firmware 0.4.2 drawn on the PC with
// the same colours, fonts, texts and positions (view_saeulen.cpp,
// view_phosphor.cpp, view_dos.cpp, view_ansi.cpp, view_amber.cpp,
// view_klassik.cpp and the login screen of view_common.cpp).
//
// Only for pictures: values that are not fresh get a small "Stand HH:MM",
// which the firmware does not have. Without any values no picture is drawn,
// so there is no NO CARRIER screen here.

using System;
using System.Collections.Generic;

namespace SysopTV.Tray
{
    internal sealed class DesignInfo
    {
        internal readonly string Id;     // stored in config.json
        internal readonly string Label;  // shown in the window

        internal DesignInfo(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public override string ToString()
        {
            return Label;
        }
    }

    internal static partial class ClaudeDesigns
    {
        // Same order and names as the designs of the firmware.
        internal static readonly DesignInfo[] All =
        {
            new DesignInfo("saeulen", "Säulen"),
            new DesignInfo("phosphor", "Phosphor"),
            new DesignInfo("dos", "DOS"),
            new DesignInfo("ansi", "ANSI"),
            new DesignInfo("bernstein", "Bernstein"),
            new DesignInfo("klassik", "Klassik"),
            new DesignInfo("led", "LED-Matrix"),
            new DesignInfo("hell", "Hell"),
            new DesignInfo("fallblatt", "Fallblatt"),
            new DesignInfo("gruen", "4-Ton-Grün")
        };

        internal const string Default = "dos";

        // The first six are the designs of the firmware, in its order.
        internal const int FirmwareCount = 6;

        private static readonly Dictionary<string, Func<ViewModel, Canvas>> Renderers = new Dictionary<string, Func<ViewModel, Canvas>>
        {
            { "saeulen", Saeulen },
            { "phosphor", Phosphor },
            { "dos", Dos },
            { "ansi", Ansi },
            { "bernstein", Bernstein },
            { "klassik", Klassik },
            { "led", LedMatrix },
            { "hell", Hell },
            { "fallblatt", Fallblatt },
            { "gruen", Green }
        };

        internal static bool Known(string id)
        {
            return id != null && Renderers.ContainsKey(id);
        }

        internal static Canvas Render(string design, ViewModel v)
        {
            Func<ViewModel, Canvas> r;
            if (!Renderers.TryGetValue(design ?? "", out r))
            {
                r = Dos;
            }
            return r(v);
        }

        private static readonly ushort Black = 0;

        // Colours from config.h of the firmware.
        private static readonly ushort SaeulenMain = Canvas.Rgb(70, 240, 0);
        private static readonly ushort SaeulenDim = Canvas.Rgb(30, 120, 0);
        private static readonly ushort SaeulenOutline = Canvas.Rgb(24, 96, 0);
        private static readonly ushort SaeulenLine = Canvas.Rgb(0, 40, 0);
        private static readonly ushort SaeulenWarn = Canvas.Rgb(255, 40, 16);

        private static readonly ushort PhosphorMain = Canvas.Rgb(70, 240, 0);
        private static readonly ushort PhosphorDim = Canvas.Rgb(30, 110, 0);
        private static readonly ushort PhosphorShade = Canvas.Rgb(20, 80, 0);
        private static readonly ushort PhosphorScan = Canvas.Rgb(30, 130, 0);
        private static readonly ushort PhosphorWarn = Canvas.Rgb(255, 110, 0);
        private static readonly ushort PhosphorWarnScan = Canvas.Rgb(140, 50, 0);

        private static readonly ushort DosBlue = Canvas.Rgb(0, 0, 168);
        private static readonly ushort DosCyan = Canvas.Rgb(40, 220, 230);
        private static readonly ushort DosWhite = Canvas.Rgb(230, 230, 230);
        private static readonly ushort DosGray = Canvas.Rgb(160, 160, 190);
        private static readonly ushort DosYellow = Canvas.Rgb(255, 225, 30);
        private static readonly ushort DosEmpty = Canvas.Rgb(0, 0, 80);
        private static readonly ushort DosWarn = Canvas.Rgb(255, 70, 60);

        private static readonly ushort AnsiMagenta = Canvas.Rgb(220, 40, 220);
        private static readonly ushort AnsiCyan = Canvas.Rgb(40, 230, 240);
        private static readonly ushort AnsiCyanDim = Canvas.Rgb(20, 110, 120);
        private static readonly ushort AnsiYellow = Canvas.Rgb(255, 225, 30);
        private static readonly ushort AnsiYellowDim = Canvas.Rgb(120, 100, 0);
        private static readonly ushort AnsiRed = Canvas.Rgb(255, 60, 50);
        private static readonly ushort AnsiRedDim = Canvas.Rgb(120, 20, 10);
        private static readonly ushort AnsiWhite = Canvas.Rgb(210, 210, 210);
        private static readonly ushort AnsiGray = Canvas.Rgb(100, 100, 100);
        private static readonly ushort AnsiDot = Canvas.Rgb(50, 50, 50);

        internal static readonly ushort AmberMain = Canvas.Rgb(255, 110, 0);
        internal static readonly ushort AmberDim = Canvas.Rgb(140, 55, 0);
        internal static readonly ushort AmberFrame = Canvas.Rgb(110, 42, 0);
        internal static readonly ushort AmberBar = Canvas.Rgb(120, 46, 0);
        internal static readonly ushort AmberWarn = Canvas.Rgb(255, 40, 16);

        private static string Pct(ViewColumn c)
        {
            return c.Present ? c.Percent + "%" : "--";
        }

        private static string ModeUpper(bool used)
        {
            return used ? "VERBRAUCHT" : "ÜBRIG";
        }

        private static string ModeTitle(bool used)
        {
            return used ? "Verbraucht" : "Übrig";
        }

        // Login screen like drawSpecial(), vga false uses the GLCD font and Font 2.
        private static Canvas Login(ViewModel v, ushort back, ushort main, ushort dim, ushort warn, bool vga)
        {
            var c = new Canvas();
            c.Clear(back);
            SpecialText(c, vga, back, "ANMELDUNG", 60, vga ? 2 : 3, warn);
            SpecialText(c, vga, back, "ABGELAUFEN", 100, vga ? 2 : 3, warn);
            SpecialText(c, vga, back, "Claude Code starten", 150, 1, main);
            SpecialText(c, vga, back, v.Clock, 200, 1, dim);
            return c;
        }

        private static void SpecialText(Canvas c, bool vga, ushort back, string s, int y, int scale, ushort colour)
        {
            if (vga)
            {
                c.Text(s, 120, y, Align.Center, scale, colour, back);
            }
            else if (scale == 1)
            {
                c.Tft(s, 120, y + 8, TftFonts.Font2, 1, colour, back, Datum.MC);
            }
            else
            {
                c.Tft(s, 120, y + scale * 4, TftFonts.Glcd, scale, colour, back, Datum.MC);
            }
        }

        // Small "Stand HH:MM" in the 6x8 font.
        private static void SmallStamp(Canvas c, ViewModel v, int x, int y, ushort fg, ushort bg, Datum datum)
        {
            if (v.Stamp != null)
            {
                c.Tft(v.Stamp, x, y, TftFonts.Glcd, 1, fg, bg, datum);
            }
        }

        // ---- 1 Säulen ----

        private static Canvas Saeulen(ViewModel v)
        {
            if (v.Screen == ViewScreen.Login)
            {
                return Login(v, Black, SaeulenMain, SaeulenDim, SaeulenWarn, false);
            }
            var c = new Canvas();
            c.Text("CLAUDE", 8, 4, Align.Left, 1, SaeulenMain, Black);
            c.Text(ModeUpper(v.ShowUsed), 68, 4, Align.Left, 1, SaeulenDim, Black);
            c.FillRect(8, 24, 224, 1, SaeulenLine);
            c.Text(v.Clock, 232, 4, Align.Right, 1, SaeulenDim, Black, 40);
            SaeulenColumn(c, 60, v.Session, "SESSION");
            SaeulenColumn(c, 180, v.Week, "WOCHE");
            SmallStamp(c, v, 120, 26, SaeulenDim, Black, Datum.TC);
            return c;
        }

        private static void SaeulenColumn(Canvas c, int cx, ViewColumn col, string label)
        {
            ushort colour = col.Present ? (col.Warn ? SaeulenWarn : SaeulenMain) : SaeulenDim;
            c.Tft(Pct(col), cx, 50, TftFonts.Glcd, 4, colour, Black, Datum.MC);
            int x = cx - 30;
            for (int i = 0; i < 10; i++)
            {
                int y = 76 + (9 - i) * 11;
                if (i < col.Segments)
                {
                    c.FillRect(x, y, 60, 9, colour);
                }
                else
                {
                    c.DrawRect(x, y, 60, 9, SaeulenOutline);
                }
            }
            c.Tft(label, cx, 198, TftFonts.Glcd, 2, colour, Black, Datum.MC);
            c.Tft(col.Bottom, cx, 222, TftFonts.Font2, 1, SaeulenDim, Black, Datum.MC);
        }

        // ---- 2 Phosphor ----

        private static Canvas Phosphor(ViewModel v)
        {
            if (v.Screen == ViewScreen.Login)
            {
                return Login(v, Black, PhosphorMain, PhosphorDim, PhosphorWarn, true);
            }
            var c = new Canvas();
            c.Text("CLAUDE", 8, 6, Align.Left, 1, PhosphorMain, Black);
            c.Text(ModeUpper(v.ShowUsed), 68, 6, Align.Left, 1, PhosphorDim, Black);
            c.Text(v.Clock, 232, 6, Align.Right, 1, PhosphorDim, Black, 40);
            PhosphorColumn(c, 60, v.Session, "SESSION");
            PhosphorColumn(c, 180, v.Week, "WOCHE");
            SmallStamp(c, v, 120, 25, PhosphorDim, Black, Datum.TC);
            return c;
        }

        private static void PhosphorColumn(Canvas c, int cx, ViewColumn col, string label)
        {
            ushort colour = col.Present ? (col.Warn ? PhosphorWarn : PhosphorMain) : PhosphorDim;
            c.Text(Pct(col), cx, 34, Align.Center, 2, colour, Black, 112);
            int percent = col.Present ? col.Percent : 0;
            ushort main = col.Warn ? PhosphorWarn : PhosphorMain;
            ushort scan = col.Warn ? PhosphorWarnScan : PhosphorScan;
            const int top = 74, height = 120, width = 40;
            int x = cx - width / 2;
            int filled = (percent * height + 50) / 100;
            if (percent > 0 && filled < 4)
            {
                filled = 4;
            }
            int empty = height - filled;
            c.FillShade(x, top, width, empty, 1, 2, PhosphorShade, Black);
            if (filled > 0)
            {
                int fillTop = top + empty;
                int edge = Math.Min(4, filled);
                c.FillShade(x, fillTop, width, edge, 2, 2, main, Black);
                c.FillRect(x, fillTop + edge, width, filled - edge, main);
                for (int y = fillTop + edge + 2; y < top + height; y += 3)
                {
                    c.FillRect(x, y, width, 1, scan);
                }
            }
            c.Text(label, cx, 200, Align.Center, 1, colour, Black, 112);
            c.Text(col.Bottom, cx, 220, Align.Center, 1, PhosphorDim, Black, 116);
        }

        // ---- 3 DOS ----

        private static readonly int[] DosWinX = { 4, 123 };
        private const int DosWinY = 28;
        private const int DosWinW = 113;
        private const int DosWinH = 208;

        private static Canvas Dos(ViewModel v)
        {
            if (v.Screen == ViewScreen.Login)
            {
                return Login(v, DosBlue, DosYellow, DosCyan, DosWarn, true);
            }
            var c = new Canvas();
            c.Clear(DosBlue);
            c.FillRect(0, 0, 240, 18, DosCyan);
            c.Text("CLAUDE", 8, 1, Align.Left, 1, Black, DosCyan);
            c.Text(v.Clock, 232, 1, Align.Right, 1, Black, DosCyan);
            if (v.Stamp != null)
            {
                c.Text(v.Stamp, 124, 1, Align.Center, 1, Black, DosCyan);
            }
            for (int i = 0; i < 2; i++)
            {
                int cx = DosWinX[i] + DosWinW / 2;
                c.DoubleFrame(DosWinX[i], DosWinY, DosWinW, DosWinH, DosWhite);
                c.Text(" " + (i == 0 ? "SESSION" : "WOCHE") + " ", cx, DosWinY - 8, Align.Center, 1, DosWhite, DosBlue);
                c.Text(ModeTitle(v.ShowUsed), cx, 90, Align.Center, 1, DosGray, DosBlue);
                DosColumn(c, cx, i == 0 ? v.Session : v.Week);
            }
            return c;
        }

        private static void DosColumn(Canvas c, int cx, ViewColumn col)
        {
            ushort colour = col.Present ? (col.Warn ? DosWarn : DosYellow) : DosGray;
            c.Text(Pct(col), cx, 40, Align.Center, 3, colour, DosBlue, 100);
            int x = cx - 32;
            for (int i = 0; i < 10; i++)
            {
                c.FillRect(x, 114 + (9 - i) * 9, 64, 6, i < col.Segments ? colour : DosEmpty);
            }
            c.Text(col.Bottom, cx, 210, Align.Center, 1, DosCyan, DosBlue, 104);
        }

        // ---- 4 ANSI ----

        private static Canvas Ansi(ViewModel v)
        {
            if (v.Screen == ViewScreen.Login)
            {
                return Login(v, Black, AnsiCyan, AnsiGray, AnsiRed, true);
            }
            var c = new Canvas();
            for (int i = 0; i < 3; i++)
            {
                c.FillShade(28 + i * 8, 4, 8, 16, 3 - i, 1, AnsiMagenta, Black);
                c.FillShade(188 + i * 8, 4, 8, 16, i + 1, 1, AnsiMagenta, Black);
            }
            c.Text("C L A U D E", 120, 4, Align.Center, 1, AnsiMagenta, Black);
            c.FillRect(4, 24, 232, 1, AnsiMagenta);
            AnsiBlock(c, 32, v.Session, "SESSION", AnsiCyan, AnsiCyanDim, v.ShowUsed);
            AnsiBlock(c, 128, v.Week, "WOCHE", AnsiYellow, AnsiYellowDim, v.ShowUsed);
            c.Text(v.Clock, 232, 222, Align.Right, 1, AnsiGray, Black);
            if (v.Stamp != null)
            {
                c.Text(v.Stamp, 8, 222, Align.Left, 1, AnsiGray, Black);
            }
            return c;
        }

        private static void AnsiBlock(Canvas c, int y, ViewColumn col, string name, ushort main, ushort dim, bool used)
        {
            c.Text(name, 8, y, Align.Left, 1, main, Black);
            c.Text(ModeTitle(used), 8, y + 18, Align.Left, 1, AnsiWhite, Black);
            ushort colour = col.Present ? (col.Warn ? AnsiRed : main) : AnsiGray;
            ushort trail = col.Warn ? AnsiRed : main;
            c.Text(Pct(col), 232, y - 2, Align.Right, 3, colour, Black, 100);

            const int barX = 8, barW = 224, barH = 14, cell = 8;
            int barY = y + 52;
            int percent = col.Present ? col.Percent : 0;
            int solid = (percent * barW + 50) / 100;
            int x = barX;
            if (solid > 0)
            {
                c.FillRect(x, barY, solid, barH, colour);
                x += solid;
            }
            for (int level = 3; level >= 1 && x < barX + barW && percent > 0; level--)
            {
                int w = Math.Min(cell, barX + barW - x);
                c.FillShade(x, barY, w, barH, level, 1, trail, Black);
                x += w;
            }
            if (x < barX + barW)
            {
                int first = ((x - barX + cell - 1) / cell) * cell + barX;
                for (int dx = first; dx + 2 <= barX + barW; dx += cell)
                {
                    c.FillRect(dx + 3, barY + barH / 2 - 1, 2, 2, AnsiDot);
                }
            }
            c.Text(col.ResetLine, 8, y + 72, Align.Left, 1, dim, Black, 224);
        }

        // ---- 5 Bernstein ----

        private static readonly int[] TankX = { 16, 128 };
        private const int TankY = 8, TankW = 96, TankH = 168, TankInner = 3;
        private const int TankNumberY = TankY + 60;

        private static Canvas Bernstein(ViewModel v)
        {
            if (v.Screen == ViewScreen.Login)
            {
                return Login(v, Black, AmberMain, AmberDim, AmberWarn, true);
            }
            var c = new Canvas();
            for (int i = 0; i < 2; i++)
            {
                c.DrawRect(TankX[i], TankY, TankW, TankH, AmberFrame);
                c.DrawRect(TankX[i] + 1, TankY + 1, TankW - 2, TankH - 2, AmberFrame);
            }
            c.Text("SESSION", TankX[0] + TankW / 2, 182, Align.Center, 1, AmberMain, Black);
            c.Text("WOCHE", TankX[1] + TankW / 2, 182, Align.Center, 1, AmberMain, Black);
            c.FillRect(0, 222, 240, 18, AmberBar);
            c.Text(v.ShowUsed ? "VERBRAUCHT IN %" : "ÜBRIG IN %", 6, 223, Align.Left, 1, Black, AmberBar);
            // The stamp takes the place of the clock, the bar has no room for both.
            c.Text(v.Stamp ?? v.Clock, 234, 223, Align.Right, 1, Black, AmberBar);
            BernsteinTank(c, 0, v.Session);
            BernsteinTank(c, 1, v.Week);
            return c;
        }

        private static void BernsteinTank(Canvas c, int index, ViewColumn col)
        {
            int x = TankX[index] + TankInner;
            int y = TankY + TankInner;
            int w = TankW - 2 * TankInner;
            int h = TankH - 2 * TankInner;
            int level = col.Present ? col.Percent : 0;
            ushort fill = col.Warn ? AmberWarn : AmberMain;
            int filled = (level * h + 50) / 100;
            int fillTop = y + h - filled;
            if (filled < h)
            {
                c.DrawRect(x, y, w, h - filled, col.Warn ? AnsiRedDim : AmberDim);
            }
            if (filled > 0)
            {
                c.FillRect(x, fillTop, w, filled, fill);
            }
            for (int q = 1; q <= 3; q++)
            {
                int my = y + h - (h * q) / 4;
                c.FillRect(x, my, 10, 1, my >= fillTop ? Black : AmberFrame);
            }
            string number = col.Present ? level.ToString() : "--";
            int cx = x + w / 2;
            ushort upper = col.Present ? fill : AmberDim;
            if (fillTop > TankNumberY)
            {
                c.SetClip(x, y, w, fillTop - y);
                c.TextTransparent(number, cx, TankNumberY, Align.Center, 3, upper);
                c.ResetClip();
            }
            if (filled > 0 && fillTop < TankNumberY + 48)
            {
                c.SetClip(x, fillTop, w, y + h - fillTop);
                c.TextTransparent(number, cx, TankNumberY, Align.Center, 3, Black);
                c.ResetClip();
            }
            c.Text(col.Bottom, TankX[index] + TankW / 2, 200, Align.Center, 1, AmberDim, Black, 110);
        }

        // ---- 6 Klassik ----

        private const int KlLeft = 16, KlRight = 224, KlBigY = 26, KlBigH = 75;
        private const int KlSegY = 184, KlSegH = 16, KlSegCount = 16, KlSegGap = 2;

        private static bool Critical(ViewColumn c)
        {
            return c.Present && c.Remaining <= 5;
        }

        private static Canvas Klassik(ViewModel v)
        {
            if (v.Screen == ViewScreen.Login)
            {
                return Login(v, Black, AmberMain, AmberDim, AmberWarn, true);
            }
            var c = new Canvas();
            if (Critical(v.Session) || Critical(v.Week))
            {
                c.FillRect(0, 0, 240, 6, AmberWarn);
                c.FillRect(0, 234, 240, 6, AmberWarn);
                c.FillRect(0, 0, 6, 240, AmberWarn);
                c.FillRect(234, 0, 6, 240, AmberWarn);
            }
            c.Text("SESSION", KlLeft, 10, Align.Left, 1, AmberDim, Black);
            c.Text(ModeUpper(v.ShowUsed), KlRight, 10, Align.Right, 1, AmberDim, Black);
            c.FillRect(KlLeft, 148, KlRight - KlLeft, 2, AmberFrame);
            c.Text("WOCHE", KlLeft, 158, Align.Left, 1, AmberDim, Black);

            ViewColumn s = v.Session, w = v.Week;
            ushort big = s.Present ? (s.Warn ? AmberWarn : AmberMain) : AmberDim;
            if (!s.Present)
            {
                c.Text("--", 120, KlBigY + 5, Align.Center, 4, big, Black);
            }
            else
            {
                string num = s.Percent.ToString();
                int wn = Canvas.TftWidth(num, TftFonts.Font8, 1);
                int wp = Canvas.TftWidth("%", TftFonts.Font4, 1);
                const int gap = 4;
                int left = 120 - (wn + gap + wp) / 2;
                c.Tft(num, left, KlBigY, TftFonts.Font8, 1, big, Black, Datum.TL);
                c.Tft("%", left + wn + gap, KlBigY + KlBigH - 30, TftFonts.Font4, 1, big, Black, Datum.TL);
            }
            string line = s.Present && s.ResetClock.Length > 0 ? "Reset " + s.ResetClock : "nicht aktiv";
            c.Tft(line, 120, 109, TftFonts.Font4, 1, AmberMain, Black, Datum.TC);
            SmallStamp(c, v, 120, 139, AmberDim, Black, Datum.TC);

            c.Tft(Pct(w), KlRight, 154, TftFonts.Font4, 1, w.Present ? (w.Warn ? AmberWarn : AmberMain) : AmberDim, Black, Datum.TR);
            int lit = 0;
            if (w.Present)
            {
                lit = (w.Percent * KlSegCount + 50) / 100;
                if (w.Percent > 0 && lit == 0)
                {
                    lit = 1;
                }
            }
            int span = KlRight - KlLeft + KlSegGap;
            for (int i = 0; i < KlSegCount; i++)
            {
                int x0 = KlLeft + i * span / KlSegCount;
                int x1 = KlLeft + (i + 1) * span / KlSegCount - KlSegGap;
                if (i < lit)
                {
                    c.FillRect(x0, KlSegY, x1 - x0, KlSegH, w.Warn ? AmberWarn : AmberMain);
                }
                else
                {
                    c.DrawRect(x0, KlSegY, x1 - x0, KlSegH, w.Warn ? AnsiRedDim : AmberDim);
                }
            }
            c.Text(w.ResetLine, KlLeft, 206, Align.Left, 1, AmberDim, Black, KlRight - KlLeft);
            return c;
        }
    }
}
