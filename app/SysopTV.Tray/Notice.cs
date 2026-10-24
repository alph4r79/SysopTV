// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Notice screen: a cube in picture mode always gets a picture. Without any
// valid numbers the program draws a short message in the colours and font of
// the chosen style ("Shopdaten werden geladen", "Zu viele Anfragen, nächster
// Versuch 09:18", ...). Panel rules: dark backgrounds pure black, text on
// light backgrounds black, at least 8 px from the edge.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace SysopTV.Tray
{
    internal static class Notice
    {
        private enum Face { Vga, Inter, Mono, Led }

        private sealed class Look
        {
            internal ushort Back, Text, Dim;
            internal Face Face;
            internal Action<Canvas> Frame;
        }

        private static Look LookOf(string style)
        {
            switch (style)
            {
                case "dos":
                    return new Look { Back = Canvas.Rgb(0, 0, 168), Text = Canvas.Rgb(255, 225, 30), Dim = Canvas.Rgb(40, 220, 230), Face = Face.Vga };
                case "saeulen":
                case "phosphor":
                    return new Look { Back = 0, Text = Canvas.Rgb(70, 240, 0), Dim = Canvas.Rgb(30, 120, 0), Face = Face.Vga };
                case "ansi":
                case "heute":
                    return new Look { Back = 0, Text = Canvas.Rgb(40, 230, 240), Dim = Canvas.Rgb(140, 140, 140), Face = Face.Vga };
                case "led":
                    return new Look { Back = 0, Text = Led.On, Dim = Led.Dim, Face = Face.Led };
                case "hell":
                    return new Look { Back = ShopBig.HellBack, Text = ShopBig.HellText, Dim = ShopBig.HellDim, Face = Face.Inter };
                case "fallblatt":
                    return new Look { Back = 0, Text = Flap.Main, Dim = Flap.Label, Face = Face.Mono };
                case "gruen":
                    return new Look { Back = ShopBig.G3, Text = ShopBig.G0, Dim = ShopBig.G1, Face = Face.Vga, Frame = ShopBig.GreenFrame };
                case "bon":
                    return new Look { Back = ShopBig.Paper, Text = ShopBig.Ink, Dim = ShopBig.InkMuted, Face = Face.Vga, Frame = ShopBig.TornEdge };
                default:
                    // bernstein, klassik, woche
                    return new Look { Back = 0, Text = ClaudeDesigns.AmberMain, Dim = ClaudeDesigns.AmberDim, Face = Face.Vga };
            }
        }

        // heading small on top, the message in the middle (wrapped), detail
        // below it (for example "nächster Versuch 09:18"), the clock at the bottom.
        internal static Canvas Render(string style, string heading, string message, string detail, DateTime now)
        {
            Look look = LookOf(style);
            var c = new Canvas();
            c.Clear(look.Back);
            if (look.Frame != null)
            {
                look.Frame(c);
            }
            string clock = now.ToString("HH:mm", CultureInfo.InvariantCulture);
            switch (look.Face)
            {
                case Face.Led:
                    LedLayout(c, look, heading, message, detail, clock);
                    break;
                case Face.Inter:
                case Face.Mono:
                    TtfLayout(c, look, look.Face == Face.Inter ? TtfFace.InterBold : TtfFace.MonoBold,
                              look.Face == Face.Inter ? TtfFace.InterRegular : TtfFace.MonoBold, heading, message, detail, clock);
                    break;
                default:
                    VgaLayout(c, look, heading, message, detail, clock);
                    break;
            }
            return c;
        }

        // Splits a text into lines of at most max characters at spaces.
        internal static List<string> Wrap(string text, int max)
        {
            var lines = new List<string>();
            string line = "";
            foreach (string word in (text ?? "").Split(' '))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > max)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = line.Length == 0 ? word : line + " " + word;
                }
            }
            if (line.Length > 0)
            {
                lines.Add(line);
            }
            return lines;
        }

        private static void VgaLayout(Canvas c, Look look, string heading, string message, string detail, string clock)
        {
            int top = look.Frame == ShopBig.GreenFrame ? 18 : 12;
            c.Text(heading, 120, top, Align.Center, 1, look.Dim, look.Back);
            List<string> lines = Wrap(message, 13);
            bool big = lines.Count <= 3;
            if (!big)
            {
                lines = Wrap(message, 25);
            }
            int lineH = big ? 36 : 20;
            int y = 120 - (lines.Count * lineH + (detail != null ? 28 : 0)) / 2;
            foreach (string l in lines)
            {
                c.Text(l, 120, y, Align.Center, big ? 2 : 1, look.Text, look.Back);
                y += lineH;
            }
            if (detail != null)
            {
                foreach (string l in Wrap(detail, 25))
                {
                    c.Text(l, 120, y + 10, Align.Center, 1, look.Dim, look.Back);
                    y += 18;
                }
            }
            int bottom = look.Frame == ShopBig.TornEdge ? 202 : look.Frame == ShopBig.GreenFrame ? 206 : 212;
            c.Text(clock, 120, bottom, Align.Center, 1, look.Dim, look.Back);
        }

        private static void TtfLayout(Canvas c, Look look, TtfFace bold, TtfFace regular, string heading, string message, string detail, string clock)
        {
            TtfText.Draw(c, heading, regular, 10, 120, 14, Align.Center, look.Dim);
            List<string> lines = Wrap(message, 16);
            float h = 15;
            foreach (string l in lines)
            {
                h = Math.Min(h, TtfText.Fit(l, bold, 15, 216));
            }
            int lineH = (int)(h * 1.9f);
            List<string> details = detail != null ? Wrap(detail, 26) : new List<string>();
            int y = 120 - (lines.Count * lineH + details.Count * 20) / 2;
            foreach (string l in lines)
            {
                TtfText.Draw(c, l, bold, h, 120, y, Align.Center, look.Text);
                y += lineH;
            }
            foreach (string l in details)
            {
                TtfText.Draw(c, l, regular, 10, 120, y + 6, Align.Center, look.Dim);
                y += 20;
            }
            TtfText.Draw(c, clock, regular, 10, 120, 214, Align.Center, look.Dim);
        }

        private static void LedLayout(Canvas c, Look look, string heading, string message, string detail, string clock)
        {
            const int pitch = 3;
            var rows = new List<string> { heading };
            rows.AddRange(Wrap(message, 16));
            if (detail != null)
            {
                rows.AddRange(Wrap(detail, 16));
            }
            rows.Add(clock);
            var heights = new int[rows.Count];
            var pitches = new int[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                heights[i] = Led.TinyHeight(pitch);
                pitches[i] = pitch;
            }
            int[] tops = Led.Spread(heights, pitches);
            int messageEnd = 1 + Wrap(message, 16).Count;
            for (int i = 0; i < rows.Count; i++)
            {
                bool main = i >= 1 && i < messageEnd;
                Led.Tiny(c, rows[i], 120, tops[i], pitch, main ? look.Text : look.Dim);
            }
        }
    }
}
