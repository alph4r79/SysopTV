// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// "Claude als Bild": the design DOS of the firmware (view_dos.cpp), drawn on
// the PC with the same colours, texts and positions, including the special
// screens of view_common.cpp.

namespace SysopTV.Tray
{
    internal static class DosRenderer
    {
        // Colours from config.h of the firmware.
        internal static readonly ushort Blue = Canvas.Rgb(0, 0, 168);
        internal static readonly ushort Cyan = Canvas.Rgb(40, 220, 230);
        internal static readonly ushort White = Canvas.Rgb(230, 230, 230);
        internal static readonly ushort Gray = Canvas.Rgb(160, 160, 190);
        internal static readonly ushort Yellow = Canvas.Rgb(255, 225, 30);
        internal static readonly ushort Empty = Canvas.Rgb(0, 0, 80);
        internal static readonly ushort Warn = Canvas.Rgb(255, 70, 60);
        private static readonly ushort Black = 0;

        private static readonly int[] WinX = { 4, 123 };
        private const int WinY = 28;
        private const int WinW = 113;
        private const int WinH = 208;
        private const int NumberY = 40;
        private const int ModeY = 90;
        private const int BarsTop = 114;
        private const int BarW = 64;
        private const int BarH = 6;
        private const int BarPitch = 9;
        private const int BottomY = 210;

        internal static Canvas Render(ViewModel v)
        {
            var c = new Canvas();
            if (v.Screen != ViewScreen.Limits)
            {
                Special(c, v);
                return c;
            }
            c.Clear(Blue);
            c.FillRect(0, 0, 240, 18, Cyan);
            c.Text("CLAUDE", 8, 1, Align.Left, 1, Black, Cyan);
            c.Text(v.Clock, 232, 1, Align.Right, 1, Black, Cyan);
            Window(c, 0, "SESSION");
            Window(c, 1, "WOCHE");
            for (int i = 0; i < 2; i++)
            {
                c.Text(v.ShowUsed ? "Verbraucht" : "Übrig", Centre(i), ModeY, Align.Center, 1, Gray, Blue);
            }
            Column(c, 0, v.Session);
            Column(c, 1, v.Week);
            return c;
        }

        private static int Centre(int index)
        {
            return WinX[index] + WinW / 2;
        }

        private static void Window(Canvas c, int index, string title)
        {
            c.DoubleFrame(WinX[index], WinY, WinW, WinH, White);
            c.Text(" " + title + " ", Centre(index), WinY - 8, Align.Center, 1, White, Blue);
        }

        private static void Column(Canvas c, int index, ViewColumn col)
        {
            int cx = Centre(index);
            ushort colour = col.Present ? (col.Warn ? Warn : Yellow) : Gray;
            c.Text(col.Present ? col.Percent + "%" : "--", cx, NumberY, Align.Center, 3, colour, Blue, 100);
            int x = cx - BarW / 2;
            for (int i = 0; i < 10; i++)
            {
                int y = BarsTop + (9 - i) * BarPitch;
                c.FillRect(x, y, BarW, BarH, i < col.Segments ? colour : Empty);
            }
            c.Text(col.Bottom, cx, BottomY, Align.Center, 1, Cyan, Blue, 104);
        }

        // Like drawSpecial() with the DOS style: main yellow, dim cyan, warning red.
        private static void Special(Canvas c, ViewModel v)
        {
            c.Clear(Blue);
            if (v.Screen == ViewScreen.Login)
            {
                c.Text("ANMELDUNG", 120, 60, Align.Center, 2, Warn, Blue);
                c.Text("ABGELAUFEN", 120, 100, Align.Center, 2, Warn, Blue);
                c.Text("Claude Code starten", 120, 150, Align.Center, 1, Yellow, Blue);
                c.Text(v.Clock, 120, 200, Align.Center, 1, Cyan, Blue, 80);
                return;
            }
            c.Text(v.Clock, 120, 40, Align.Center, 5, Yellow, Blue, 220);
            c.Text("NO CARRIER", 120, 140, Align.Center, 2, Yellow, Blue);
            c.Text(v.LastData, 120, 180, Align.Center, 1, Cyan, Blue, 220);
            if (!string.IsNullOrEmpty(v.Address))
            {
                c.Text(v.Address, 120, 220, Align.Center, 1, Cyan, Blue, 160);
            }
        }
    }
}
