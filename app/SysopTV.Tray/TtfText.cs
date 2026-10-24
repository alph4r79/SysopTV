// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// TrueType text for the modern styles. The fonts are embedded resources
// (app/assets/fonts, SIL Open Font License 1.1) and loaded into a private
// font collection, nothing is installed in Windows. Text is drawn as an
// outline with anti aliasing into the RGB565 canvas, so sizes are exact:
// the height given is the height of the digits.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SysopTV.Tray
{
    internal enum TtfFace { InterRegular, InterBold, MonoBold }

    internal static class TtfText
    {
        private static readonly object Gate = new object();
        private static PrivateFontCollection fonts;
        private static readonly List<IntPtr> memory = new List<IntPtr>();
        private static readonly Dictionary<TtfFace, float> digitRatio = new Dictionary<TtfFace, float>();

        private static void Load()
        {
            if (fonts != null)
            {
                return;
            }
            var pfc = new PrivateFontCollection();
            foreach (string name in new[] { "Inter-Regular.ttf", "Inter-Bold.ttf", "JetBrainsMono-Bold.ttf" })
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SysopTV.Tray.fonts." + name))
                {
                    var data = new byte[s.Length];
                    s.Read(data, 0, data.Length);
                    // GDI+ keeps using the memory, so it stays allocated for the whole run.
                    IntPtr p = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, p, data.Length);
                    pfc.AddMemoryFont(p, data.Length);
                    memory.Add(p);
                }
            }
            fonts = pfc;
        }

        private static FontFamily Family(TtfFace face, out FontStyle style)
        {
            Load();
            style = face == TtfFace.InterRegular ? FontStyle.Regular : FontStyle.Bold;
            string want = face == TtfFace.MonoBold ? "JetBrains Mono" : "Inter";
            foreach (FontFamily f in fonts.Families)
            {
                if (f.Name == want)
                {
                    return f;
                }
            }
            return fonts.Families[0];
        }

        // Outline of s at em size 100, without moving it.
        private static GraphicsPath Path(string s, TtfFace face)
        {
            FontStyle style;
            FontFamily fam = Family(face, out style);
            var path = new GraphicsPath();
            path.AddString(s, fam, (int)style, 100f, new PointF(0, 0), StringFormat.GenericTypographic);
            return path;
        }

        // Height of the digit 0 at em size 100.
        private static float DigitHeight(TtfFace face)
        {
            float r;
            if (!digitRatio.TryGetValue(face, out r))
            {
                using (GraphicsPath p = Path("0", face))
                {
                    r = p.GetBounds().Height;
                }
                digitRatio[face] = r;
            }
            return r;
        }

        // Width of s when its digits are height pixels high.
        internal static int Width(string s, TtfFace face, float height)
        {
            lock (Gate)
            {
                using (GraphicsPath p = Path(s, face))
                {
                    RectangleF b = p.GetBounds();
                    return (int)Math.Ceiling(b.Width * height / DigitHeight(face));
                }
            }
        }

        // Largest digit height up to maxHeight at which s fits into maxWidth.
        internal static float Fit(string s, TtfFace face, float maxHeight, int maxWidth)
        {
            float h = maxHeight;
            while (h > 6 && Width(s, face, h) > maxWidth)
            {
                h -= 1;
            }
            return h;
        }

        // Draws s so that the top of its digits is at y and they are height
        // pixels high. x is the left, centre or right edge of the ink.
        internal static int Draw(Canvas c, string s, TtfFace face, float height, int x, int y, Align align, ushort fg)
        {
            if (string.IsNullOrEmpty(s))
            {
                return 0;
            }
            lock (Gate)
            {
                using (GraphicsPath p = Path(s, face))
                using (GraphicsPath digit = Path("0", face))
                {
                    RectangleF b = p.GetBounds();
                    RectangleF d = digit.GetBounds();
                    float scale = height / d.Height;
                    float w = b.Width * scale;
                    float left = align == Align.Center ? x - w / 2 : align == Align.Right ? x - w : x;
                    using (var m = new Matrix())
                    {
                        m.Translate(left - b.X * scale, y - d.Y * scale);
                        m.Scale(scale, scale);
                        p.Transform(m);
                    }
                    c.FillPath(p, fg);
                    return (int)Math.Ceiling(w);
                }
            }
        }
    }
}
