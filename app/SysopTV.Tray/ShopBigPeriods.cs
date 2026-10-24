// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Four styles of the shop big display in the look of the Claude designs
// Klassik, Säulen, Phosphor and ANSI, without bars. They always show several
// periods together (Klassik today, week and month, the others today and
// week), so "Zeitraum" and "Inhalt" do not count for them. Week means since
// Monday 0:00, month since the 1st, both in the time zone of the shop.
//
// Pixel text is the VGA font of the Claude designs. Its capitals are 10
// pixels high and it only scales in whole steps, so each text takes the
// scale closest to the drawing; the top edges of the capitals follow the
// drawing, except where the 8 pixels from the edge need a few pixels more.
// The smooth text of Klassik is Inter: the TFT fonts of the Claude design
// have no euro sign and only fixed heights. Like those fonts it is drawn
// without anti aliased edges, dark mixed pixels look olive on the panel. A
// number that does not fit its area gets the next smaller size, nothing is
// cut.

using System;
using System.Globalization;

namespace SysopTV.Tray
{
    internal static partial class ShopBig
    {
        private static readonly ushort AnsiClock = Canvas.Rgb(168, 168, 168);

        // The VGA capitals start in row 2 of the 16 rows of a glyph.
        private const int VgaCapTop = 2, VgaCap = 10;

        internal static bool ShowsAllPeriods(string style)
        {
            return style == "klassik" || style == "saeulen" || style == "phosphor" || style == "ansi";
        }

        // The hint on the page "Würfel" while such a style is chosen.
        internal static string AllPeriodsHint(string style)
        {
            return style == "klassik" ? "Dieser Stil zeigt Heute, Woche und Monat zusammen." : "Dieser Stil zeigt Heute und Woche zusammen.";
        }

        private static Canvas RenderAllPeriods(string style, ShopBigView v)
        {
            switch (style)
            {
                case "klassik": return KlassikBig(v);
                case "saeulen": return SaeulenBig(v);
                case "phosphor": return PhosphorBig(v);
                default: return AnsiBig(v);
            }
        }

        private static string ShopNameOf(ShopBigView v)
        {
            string name = string.IsNullOrWhiteSpace(v.ShopName) ? (string.IsNullOrEmpty(v.Data.ShopName) ? "Shop" : v.Data.ShopName) : v.ShopName.Trim();
            return name.ToUpper(German);
        }

        private static string Count(ShopPeriod p)
        {
            return p.Orders.ToString(CultureInfo.InvariantCulture);
        }

        private static string OrdersWord(ShopPeriod p)
        {
            return p.Orders == 1 ? "Bestellung" : "Bestellungen";
        }

        // Largest VGA scale up to max at which s fits into width.
        private static int VgaScale(string s, int width, int max, bool bold)
        {
            int scale = max;
            while (scale > 1 && s.Length * 8 * scale + (bold ? BoldShift(scale) : 0) > width)
            {
                scale--;
            }
            return scale;
        }

        private static int BoldShift(int scale)
        {
            return Math.Max(1, scale / 2);
        }

        // VGA text whose capitals start at capTop.
        private static void Vga(Canvas c, string s, int x, int capTop, Align align, int scale, ushort fg)
        {
            c.TextTransparent(s, x, capTop - VgaCapTop * scale, align, scale, fg);
        }

        // Bold VGA text: drawn twice, the second time shifted to the right.
        private static void VgaBold(Canvas c, string s, int x, int capTop, Align align, int scale, ushort fg)
        {
            int shift = BoldShift(scale);
            int left = align == Align.Center ? x - shift / 2 : align == Align.Right ? x - shift : x;
            Vga(c, s, left, capTop, align, scale, fg);
            Vga(c, s, left + shift, capTop, align, scale, fg);
        }

        // A number in the VGA font as large as fits, at most maxScale, centred
        // on the capitals of the largest size.
        private static void VgaNumber(Canvas c, string s, int x, int capTop, Align align, int maxScale, int width, bool bold, ushort fg)
        {
            int scale = VgaScale(s, width, maxScale, bold);
            int top = capTop + (maxScale - scale) * VgaCap / 2;
            if (bold)
            {
                VgaBold(c, s, x, top, align, scale, fg);
            }
            else
            {
                Vga(c, s, x, top, align, scale, fg);
            }
        }

        // Smooth text with the digits cap pixels high, smaller if it does not fit width.
        private static void Smooth(Canvas c, string s, int x, int capTop, float cap, int width, Align align, ushort fg)
        {
            float h = TtfText.Fit(s, TtfFace.InterRegular, cap, width);
            TtfText.Draw(c, s, TtfFace.InterRegular, h, x, (int)Math.Round(capTop + (cap - h) / 2), align, fg);
        }

        // ---- Klassik ----

        private static Canvas KlassikBig(ShopBigView v)
        {
            ShopData d = v.Data;
            var c = new Canvas();
            ushort main = ClaudeDesigns.AmberMain, label = ClaudeDesigns.AmberDim;
            Vga(c, "HEUTE", 15, 12, Align.Left, 1, label);
            Vga(c, Short(ShopNameOf(v), (225 - 15 - 40 - 16) / 8), 225, 12, Align.Right, 1, label);
            Smooth(c, Count(d.Today), 120, 30, 60, 210, Align.Center, main);
            Smooth(c, ShopFormat.Money(d.Today.Revenue), 120, 110, 19, 210, Align.Center, main);
            c.FillRect(15, 149, 211, 2, ClaudeDesigns.AmberFrame);
            KlassikRows(c, d.CalendarWeek, d.Month, main, label);
            string stand = d.FetchedAt == DateTime.MinValue ? "--:--" : d.FetchedAt.ToString("HH:mm", CultureInfo.InvariantCulture);
            Vga(c, "Stand " + stand, 15, 210, Align.Left, 1, label);
            Crisp(c, main, label, ClaudeDesigns.AmberFrame);
            return c;
        }

        // Pixels between the orange and black become one of the two.
        private static void Crisp(Canvas c, ushort main, ushort label, ushort line)
        {
            for (int i = 0; i < c.Pixels.Length; i++)
            {
                ushort p = c.Pixels[i];
                if (p != 0 && p != main && p != label && p != line)
                {
                    c.Pixels[i] = ((p >> 11) & 0x1F) >= 16 ? main : (ushort)0;
                }
            }
        }

        // The rows of week and month with the same size for both amounts. If an
        // amount would get smaller than 11 pixels, the labels get shorter first.
        private static void KlassikRows(Canvas c, ShopPeriod week, ShopPeriod month, ushort main, ushort label)
        {
            string weekMoney = ShopFormat.Money(week.Revenue), monthMoney = ShopFormat.Money(month.Revenue);
            string[] formats = { "{0}  {1} BEST.", "{0} {1} BEST.", "{0} {1}" };
            string weekText = null, monthText = null;
            float cap = 0;
            foreach (string f in formats)
            {
                weekText = string.Format(CultureInfo.InvariantCulture, f, "WOCHE", Count(week));
                monthText = string.Format(CultureInfo.InvariantCulture, f, "MONAT", Count(month));
                cap = Math.Min(TtfText.Fit(weekMoney, TtfFace.InterRegular, 15, Free(weekText)),
                               TtfText.Fit(monthMoney, TtfFace.InterRegular, 15, Free(monthText)));
                if (cap >= 11)
                {
                    break;
                }
            }
            Vga(c, weekText, 15, 161, Align.Left, 1, label);
            Vga(c, monthText, 15, 186, Align.Left, 1, label);
            int shift = (int)Math.Round((15 - cap) / 2);
            TtfText.Draw(c, weekMoney, TtfFace.InterRegular, cap, 225, 157 + shift, Align.Right, main);
            TtfText.Draw(c, monthMoney, TtfFace.InterRegular, cap, 225, 182 + shift, Align.Right, main);
        }

        // Room for an amount right of a label that starts at x 15, up to x 225.
        private static int Free(string text)
        {
            return 225 - (15 + text.Length * 8 + 10);
        }

        // ---- Säulen and Phosphor ----

        // Shop name, "BESTELLT" 14 pixels behind it, the clock on the right.
        private static void GreenHeader(Canvas c, ShopBigView v, int capTop, ushort main, ushort dim)
        {
            string clock = Clock(v);
            int room = 232 - clock.Length * 8 - 8 - 14 - 64 - 8;
            string name = Short(ShopNameOf(v), Math.Max(3, room / 8));
            Vga(c, name, 8, capTop, Align.Left, 1, main);
            Vga(c, "BESTELLT", 8 + name.Length * 8 + 14, capTop, Align.Left, 1, dim);
            Vga(c, clock, 232, capTop, Align.Right, 1, dim);
        }

        private static Canvas SaeulenBig(ShopBigView v)
        {
            var c = new Canvas();
            GreenHeader(c, v, 8, ClaudeDesigns.SaeulenMain, ClaudeDesigns.SaeulenDim);
            c.FillRect(8, 25, 224, 1, ClaudeDesigns.SaeulenLine);
            SaeulenColumn(c, 60, v.Data.Today, "HEUTE", "seit 0:00");
            SaeulenColumn(c, 179, v.Data.CalendarWeek, "WOCHE", "seit Mo");
            return c;
        }

        private static void SaeulenColumn(Canvas c, int cx, ShopPeriod p, string label, string since)
        {
            ushort main = ClaudeDesigns.SaeulenMain, dim = ClaudeDesigns.SaeulenDim, frame = ClaudeDesigns.SaeulenOutline;
            c.DrawRect(cx - 49, 36, 99, 146, frame);
            c.DrawRect(cx - 46, 39, 93, 140, frame);
            VgaNumber(c, Count(p), cx, 58, Align.Center, 4, 85, true, main);
            c.Tft(OrdersWord(p), cx, 113, TftFonts.Glcd, 1, dim, 0, Datum.TC);
            c.FillRect(cx - 36, 134, 72, 1, frame);
            Vga(c, ShopFormat.Money(p.Revenue), cx, 147, Align.Center, 1, main);
            c.Tft(label, cx, 191, TftFonts.Glcd, 2, main, 0, Datum.TC);
            // Font 2 has the top of its capitals in row 3.
            c.Tft(since, cx, 216 - 3, TftFonts.Font2, 1, dim, 0, Datum.TC);
        }

        private static Canvas PhosphorBig(ShopBigView v)
        {
            var c = new Canvas();
            GreenHeader(c, v, 9, ClaudeDesigns.PhosphorMain, ClaudeDesigns.PhosphorDim);
            PhosphorColumn(c, 60, v.Data.Today, "HEUTE", "seit 0:00");
            PhosphorColumn(c, 180, v.Data.CalendarWeek, "WOCHE", "seit Mo");
            return c;
        }

        private static void PhosphorColumn(Canvas c, int cx, ShopPeriod p, string label, string since)
        {
            ushort main = ClaudeDesigns.PhosphorMain, dim = ClaudeDesigns.PhosphorDim;
            c.FillShade(cx - 44, 34, 88, 160, 1, 2, ClaudeDesigns.PhosphorShade, 0);
            c.FillRect(cx - 36, 62, 72, 52, 0);
            VgaNumber(c, Count(p), cx, 68, Align.Center, 4, 68, false, main);
            c.FillRect(cx - 40, 140, 80, 24, 0);
            Vga(c, ShopFormat.Money(p.Revenue), cx, 146, Align.Center, 1, main);
            // One pixel higher than drawn, so the lowest line keeps 8 pixels from the edge.
            Vga(c, label, cx, 202, Align.Center, 1, main);
            Vga(c, since, cx, 222, Align.Center, 1, dim);
        }

        // ---- ANSI ----

        private static Canvas AnsiBig(ShopBigView v)
        {
            var c = new Canvas();
            ushort magenta = ClaudeDesigns.AnsiMagenta;
            for (int i = 0; i < 3; i++)
            {
                c.FillShade(8 + i * 7, 8, 7, 14, 3 - i, 1, magenta, 0);
                c.FillShade(211 + i * 7, 8, 7, 14, i + 1, 1, magenta, 0);
            }
            string name = Short(ShopNameOf(v), 10);
            Vga(c, string.Join(" ", name.ToCharArray()), 120, 10, Align.Center, 1, magenta);
            c.FillRect(8, 25, 224, 1, magenta);
            AnsiSection(c, 35, v.Data.Today, "HEUTE", ClaudeDesigns.AnsiCyan, ClaudeDesigns.AnsiCyanDim);
            AnsiSection(c, 131, v.Data.CalendarWeek, "WOCHE", ClaudeDesigns.AnsiYellow, ClaudeDesigns.AnsiYellowDim);
            Vga(c, Clock(v), 230, 222, Align.Right, 1, AnsiClock);
            return c;
        }

        private static void AnsiSection(Canvas c, int y, ShopPeriod p, string name, ushort main, ushort dim)
        {
            Vga(c, name, 8, y, Align.Left, 1, main);
            string word = OrdersWord(p);
            Vga(c, word, 8, y + 18, Align.Left, 1, ClaudeDesigns.AnsiWhite);
            int free = 228 - (8 + word.Length * 8 + 10);
            VgaNumber(c, Count(p), 228, y + 1, Align.Right, 3, free, true, main);
            for (int x = 8; x <= 230; x += 6)
            {
                c.FillRect(x, y + 56, 2, 2, ClaudeDesigns.AnsiGray);
            }
            Vga(c, "Umsatz " + ShopFormat.Money(p.Revenue), 8, y + 72, Align.Left, 1, dim);
        }
    }
}
