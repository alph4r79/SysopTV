// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Tray icon drawn at runtime: a small pixel monitor whose screen shows the
// state colour.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SysopTV.Tray
{
    internal static class TrayIcons
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        internal static Color ColorFor(StatusKind kind)
        {
            switch (kind)
            {
                case StatusKind.Ok: return Color.FromArgb(70, 200, 60);
                case StatusKind.Warn: return Color.FromArgb(255, 150, 0);
                case StatusKind.Error: return Color.FromArgb(230, 50, 40);
                default: return Color.FromArgb(140, 140, 140);
            }
        }

        // Draws on a 16x16 pixel grid and scales it without smoothing.
        internal static Icon Create(StatusKind kind)
        {
            Size size = SystemInformation.SmallIconSize;
            int scale = Math.Max(1, size.Width / 16);
            using (var bmp = new Bitmap(16 * scale, 16 * scale))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    Color casing = Color.FromArgb(200, 200, 192);
                    Color edge = Color.FromArgb(70, 70, 70);
                    Fill(g, scale, 1, 1, 14, 11, edge);
                    Fill(g, scale, 2, 2, 12, 9, casing);
                    Fill(g, scale, 3, 3, 10, 7, Color.FromArgb(20, 20, 20));
                    Fill(g, scale, 4, 4, 8, 5, ColorFor(kind));
                    Fill(g, scale, 6, 12, 4, 1, edge);
                    Fill(g, scale, 4, 13, 8, 2, edge);
                    Fill(g, scale, 5, 13, 6, 1, casing);
                }
                IntPtr handle = bmp.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        private static void Fill(Graphics g, int scale, int x, int y, int w, int h, Color c)
        {
            using (var b = new SolidBrush(c))
            {
                g.FillRectangle(b, x * scale, y * scale, w * scale, h * scale);
            }
        }

        // Program icon from the embedded .ico, for windows.
        internal static Icon AppIcon()
        {
            using (var s = typeof(TrayIcons).Assembly.GetManifestResourceStream("SysopTV.Tray.sysoptv.ico"))
            {
                return s != null ? new Icon(s) : SystemIcons.Application;
            }
        }
    }
}
