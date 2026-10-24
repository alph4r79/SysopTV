// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Shop big display: one period (today, this week since Monday, this month,
// this year)
// with one big number, in seven styles. The same layout for all styles:
// shop name small on top, the period as a heading, the main number as large
// as fits, below it the label. With "both" the count comes first, directly
// below it BESTELLUNGEN, then the revenue, so the amount is never read as a
// count. Everything keeps at least 8 px from the edge of the picture.

using System;
using System.Drawing;
using System.Globalization;

namespace SysopTV.Tray
{
    internal sealed class ShopBigView
    {
        internal ShopData Data;
        internal string ShopName;
        internal DateTime Now;
        internal string Stamp;    // "Stand HH:MM" if the numbers are not fresh, otherwise null
        internal string Period;   // day, week, month, year
        internal string Content;  // both, orders, revenue
        internal string Source = ShopSources.Shopify;
    }

    internal static class ShopBig
    {
        internal const string ViewBig = "big";
        internal const string ViewDetails = "details";
        internal static readonly DesignInfo[] Views =
        {
            new DesignInfo(ViewBig, "Großanzeige"),
            new DesignInfo(ViewDetails, "Details")
        };

        internal const string DefaultPeriod = "day";
        internal static readonly DesignInfo[] Periods =
        {
            new DesignInfo("day", "Heute"),
            new DesignInfo("week", "Diese Woche"),
            new DesignInfo("month", "Dieser Monat"),
            new DesignInfo("year", "Dieses Jahr")
        };

        internal const string DefaultContent = "both";
        internal static readonly DesignInfo[] Contents =
        {
            new DesignInfo("both", "Bestellungen und Umsatz"),
            new DesignInfo("orders", "nur Bestellungen"),
            new DesignInfo("revenue", "nur Umsatz")
        };

        internal const string DefaultStyle = "bon";
        internal static readonly DesignInfo[] Styles =
        {
            new DesignInfo("bon", "Kassenbon"),
            new DesignInfo("dos", "DOS"),
            new DesignInfo("bernstein", "Bernstein"),
            new DesignInfo("led", "LED-Matrix"),
            new DesignInfo("hell", "Hell"),
            new DesignInfo("fallblatt", "Fallblatt"),
            new DesignInfo("gruen", "4-Ton-Grün")
        };

        // Distance between content and the edge of the picture.
        internal const int Margin = 8;

        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

        internal static bool Known(DesignInfo[] list, string id)
        {
            return Array.Exists(list, d => d.Id == id);
        }

        internal static string Label(DesignInfo[] list, string id)
        {
            DesignInfo d = Array.Find(list, x => x.Id == id);
            return d != null ? d.Label : id;
        }

        internal static ShopPeriod PeriodData(ShopData d, string period)
        {
            switch (period)
            {
                case "week": return d.CalendarWeek;
                case "month": return d.Month;
                case "year": return d.Year;
                default: return d.Today;
            }
        }

        // HEUTE, DIESE WOCHE, the name of the month (OKTOBER) or the year (2026).
        internal static string Title(string period, DateTime shopNow)
        {
            switch (period)
            {
                case "week": return "DIESE WOCHE";
                case "month": return German.DateTimeFormat.GetMonthName(shopNow.Month).ToUpper(German);
                case "year": return shopNow.Year.ToString(CultureInfo.InvariantCulture);
                default: return "HEUTE";
            }
        }

        // The texts of the display, the same for every style.
        internal sealed class Texts
        {
            internal string Name;
            internal string Title;
            internal string Main;
            internal string Sub;     // revenue below the label, only with "both"
            internal string Label;
            internal string Extra;   // "52 Best.", only with "revenue"
        }

        internal static Texts Make(ShopBigView v)
        {
            ShopPeriod p = PeriodData(v.Data, v.Period);
            DateTime shopNow = v.Data.ShopNow == DateTime.MinValue ? v.Now : v.Data.ShopNow;
            var t = new Texts
            {
                Name = string.IsNullOrWhiteSpace(v.ShopName) ? (string.IsNullOrEmpty(v.Data.ShopName) ? "Shop" : v.Data.ShopName) : v.ShopName.Trim(),
                Title = Title(v.Period, shopNow)
            };
            string count = p.Orders.ToString(CultureInfo.InvariantCulture);
            if (v.Content == "revenue")
            {
                t.Main = ShopFormat.Money(p.Revenue);
                t.Label = "UMSATZ";
                t.Extra = count + " Best.";
            }
            else
            {
                t.Main = count;
                t.Label = p.Orders == 1 ? "BESTELLUNG" : "BESTELLUNGEN";
                if (v.Content != "orders")
                {
                    t.Sub = ShopFormat.Money(p.Revenue);
                }
            }
            return t;
        }

        private static string Short(string s, int max)
        {
            return s.Length > max ? s.Substring(0, max - 1) + "." : s;
        }

        // Boxes for main number, label, revenue and extra line between top and bottom.
        internal sealed class Boxes
        {
            internal Rectangle Main;
            internal Rectangle Sub;
            internal int LabelY;
            internal int ExtraY;
        }

        // only orders:   main, label
        // only revenue:  main, label, extra
        // both:          main, label, revenue (about half the height of the main number)
        internal static Boxes Layout(Texts t, int left, int width, int top, int bottom, int labelHeight)
        {
            var b = new Boxes();
            if (t.Extra != null)
            {
                b.ExtraY = bottom - labelHeight;
                b.LabelY = b.ExtraY - labelHeight - 4;
                b.Main = new Rectangle(left, top, width, b.LabelY - 6 - top);
            }
            else if (t.Sub != null)
            {
                int avail = bottom - top - labelHeight - 12;
                int mainH = avail * 64 / 100;
                b.Main = new Rectangle(left, top, width, mainH);
                b.LabelY = top + mainH + 4;
                int subTop = b.LabelY + labelHeight + 8;
                b.Sub = new Rectangle(left, subTop, width, bottom - subTop);
            }
            else
            {
                b.LabelY = bottom - labelHeight;
                b.Main = new Rectangle(left, top, width, b.LabelY - 6 - top);
            }
            return b;
        }

        internal static Canvas Render(string style, ShopBigView v)
        {
            Texts t = Make(v);
            if (v.Period == "year" && v.Data.YearNote != null)
            {
                // No year value: missing Shopify scope, old plugin or the first count still running.
                return Notice.Render(style, t.Title, v.Data.YearNote, YearDetail(v.Data.YearNote), v.Now);
            }
            switch (style)
            {
                case "dos": return Dos(v, t);
                case "bernstein": return Bernstein(v, t);
                case "led": return LedStyle(v, t);
                case "hell": return Hell(v, t);
                case "fallblatt": return Fallblatt(v, t);
                case "gruen": return Gruen(v, t);
                default: return Receipt(v, t);
            }
        }

        internal static string YearDetail(string note)
        {
            if (note == ShopifyClient.MissingAllOrders)
            {
                return "Heute, Woche und Monat gehen weiter";
            }
            if (note == WooClient.UpdatePlugin)
            {
                return "auf Version 0.2.0 oder neuer";
            }
            return "bitte etwas Geduld";
        }

        private static string Clock(ShopBigView v)
        {
            return v.Stamp ?? v.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        // ---- Kassenbon: paper, dashed lines, torn edge ----

        internal static readonly ushort Paper = Canvas.Rgb(235, 232, 222);
        internal static readonly ushort Ink = 0;
        internal static readonly ushort InkMuted = Canvas.Rgb(70, 70, 70);

        // The torn edge of the receipt, the teeth reach down to 6 px above the bottom.
        internal static void TornEdge(Canvas c)
        {
            for (int x = 0; x < 240; x++)
            {
                int edge = 234 - Math.Abs(x % 12 - 6);
                c.FillRect(x, edge, 1, 240 - edge, 0);
            }
        }

        private static Canvas Receipt(ShopBigView v, Texts t)
        {
            var c = new Canvas();
            c.Clear(Paper);
            c.Text(Short(t.Name, 28).ToUpper(German), 120, 8, Align.Center, 1, Ink, Paper);
            c.Text(v.Now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) + "  " + Clock(v), 120, 24, Align.Center, 1, InkMuted, Paper);
            c.Text(new string('-', 28), 120, 40, Align.Center, 1, InkMuted, Paper);
            c.TextScaled(t.Title, 120, 56, Align.Center, t.Title.Length > 13 ? 1 : 2, 2, Ink, Paper);
            Boxes b = Layout(t, Margin, 224, 94, 224, 16);
            BigVga.FitText(c, t.Main, b.Main, Ink);
            c.Text(t.Label, 120, b.LabelY, Align.Center, 1, Ink, Paper);
            if (t.Sub != null)
            {
                BigVga.FitText(c, t.Sub, b.Sub, Ink);
            }
            if (t.Extra != null)
            {
                c.Text(t.Extra, 120, b.ExtraY, Align.Center, 1, InkMuted, Paper);
            }
            TornEdge(c);
            return c;
        }

        // ---- DOS: blue, cyan bars on top and at the bottom ----

        internal static readonly ushort DosBack = Canvas.Rgb(0, 0, 170);
        internal static readonly ushort DosBar = Canvas.Rgb(0, 170, 170);
        private static readonly ushort DosGray = Canvas.Rgb(170, 170, 170);
        private static readonly ushort DosYellow = Canvas.Rgb(255, 255, 85);
        private static readonly ushort DosWhite = Canvas.Rgb(255, 255, 255);

        private static Canvas Dos(ShopBigView v, Texts t)
        {
            var c = new Canvas();
            c.Clear(DosBack);
            // The bars run to the edge, their text keeps the margin.
            c.FillRect(0, 0, 240, 26, DosBar);
            c.Text(Short(t.Name, 21), Margin, 8, Align.Left, 1, 0, DosBar);
            c.Text(v.Now.ToString("HH:mm", CultureInfo.InvariantCulture), 240 - Margin, 8, Align.Right, 1, 0, DosBar);
            c.TextScaled(t.Title, 120, 32, Align.Center, t.Title.Length > 13 ? 1 : 2, 2, DosWhite, DosBack);
            Boxes b = Layout(t, Margin, 224, 70, 208, 16);
            BigVga.FitText(c, t.Main, b.Main, DosYellow);
            c.Text(t.Label, 120, b.LabelY, Align.Center, 1, DosGray, DosBack);
            if (t.Sub != null)
            {
                BigVga.FitText(c, t.Sub, b.Sub, DosWhite);
            }
            if (t.Extra != null)
            {
                c.Text(t.Extra, 120, b.ExtraY, Align.Center, 1, DosGray, DosBack);
            }
            c.FillRect(0, 214, 240, 26, DosBar);
            c.Text(Label(ShopSources.All, v.Source), Margin, 216, Align.Left, 1, 0, DosBar);
            c.Text(v.Stamp ?? "Stand vor " + ShopFormat.Age(v.Now - v.Data.FetchedAt), 240 - Margin, 216, Align.Right, 1, 0, DosBar);
            return c;
        }

        // ---- Bernstein: amber on black, frame like the tanks, status bar ----

        private static Canvas Bernstein(ShopBigView v, Texts t)
        {
            var c = new Canvas();
            ushort main = ClaudeDesigns.AmberMain, dim = ClaudeDesigns.AmberDim;
            c.Text(Short(t.Name, 28), Margin, 8, Align.Left, 1, dim, 0);
            c.TextScaled(t.Title, 120, 26, Align.Center, t.Title.Length > 13 ? 1 : 2, 2, main, 0);
            c.DrawRect(Margin, 62, 224, 146, ClaudeDesigns.AmberFrame);
            c.DrawRect(Margin + 1, 63, 222, 144, ClaudeDesigns.AmberFrame);
            Boxes b = Layout(t, 16, 208, 70, 202, 16);
            BigVga.FitText(c, t.Main, b.Main, main);
            c.Text(t.Label, 120, b.LabelY, Align.Center, 1, dim, 0);
            if (t.Sub != null)
            {
                BigVga.FitText(c, t.Sub, b.Sub, main);
            }
            if (t.Extra != null)
            {
                c.Text(t.Extra, 120, b.ExtraY, Align.Center, 1, dim, 0);
            }
            c.FillRect(0, 214, 240, 26, ClaudeDesigns.AmberBar);
            c.Text("SHOP", Margin, 216, Align.Left, 1, 0, ClaudeDesigns.AmberBar);
            c.Text(Clock(v), 240 - Margin, 216, Align.Right, 1, 0, ClaudeDesigns.AmberBar);
            return c;
        }

        // ---- LED-Matrix: lit dots only, one grid ----

        // Small heading, the main number big, label and secondary value small,
        // all rows spread evenly. Values that do not fit the 6 px grid take
        // 4 px, six digit amounts 3 px, the labels then use the same grid.
        private static Canvas LedStyle(ShopBigView v, Texts t)
        {
            var c = new Canvas();
            int big = Led.BigWidth(t.Main, 6) <= 224 ? 6 : Led.BigWidth(t.Main, 4) <= 224 ? 4 : 3;
            int small = big == 4 ? 4 : 3;
            string top = v.Stamp ?? t.Title;
            var heights = new System.Collections.Generic.List<int> { Led.TinyHeight(small), Led.BigHeight(t.Main, big), Led.TinyHeight(small) };
            var pitches = new System.Collections.Generic.List<int> { small, big, small };
            if (t.Sub != null)
            {
                heights.Add(Led.BigHeight(t.Sub, small));
                pitches.Add(small);
            }
            if (t.Extra != null)
            {
                heights.Add(Led.TinyHeight(small));
                pitches.Add(small);
            }
            int[] tops = Led.Spread(heights.ToArray(), pitches.ToArray());
            Led.Tiny(c, top, 120, tops[0], small, v.Stamp != null ? Led.WarnDim : Led.Dim);
            Led.Big(c, t.Main, 120, tops[1], big, Led.On);
            Led.Tiny(c, t.Label, 120, tops[2], small, Led.Dim);
            if (t.Sub != null)
            {
                Led.Big(c, t.Sub, 120, tops[3], small, Led.Dim);
            }
            if (t.Extra != null)
            {
                Led.Tiny(c, t.Extra, 120, tops[3], small, Led.Dim);
            }
            return c;
        }

        // ---- Hell: light, Inter, black text ----

        internal static readonly ushort HellBack = Canvas.Rgb(245, 245, 242);
        internal static readonly ushort HellText = 0;
        internal static readonly ushort HellDim = Canvas.Rgb(70, 70, 70);
        internal static readonly ushort HellLine = Canvas.Rgb(150, 150, 145);
        internal static readonly ushort HellMoney = Canvas.Rgb(0, 110, 60);

        private static Canvas Hell(ShopBigView v, Texts t)
        {
            var c = new Canvas();
            c.Clear(HellBack);
            TtfText.Draw(c, Short(t.Name, 24), TtfFace.InterRegular, 10, 12, 12, Align.Left, HellText);
            TtfText.Draw(c, Clock(v), TtfFace.InterRegular, 10, 228, 12, Align.Right, HellText);
            float th = TtfText.Fit(t.Title, TtfFace.InterBold, 16, 216);
            TtfText.Draw(c, t.Title, TtfFace.InterBold, th, 120, 36, Align.Center, HellText);
            c.FillRect(12, 62, 216, 1, HellLine);
            Boxes b = Layout(t, 12, 216, 74, 222, 14);
            // Revenue is green, as the main number and below the count.
            DrawTtf(c, t.Main, TtfFace.InterBold, b.Main, t.Extra != null ? HellMoney : HellText);
            TtfText.Draw(c, t.Label, TtfFace.InterRegular, 11, 120, b.LabelY + 2, Align.Center, HellText);
            if (t.Sub != null)
            {
                DrawTtf(c, t.Sub, TtfFace.InterBold, b.Sub, HellMoney);
            }
            if (t.Extra != null)
            {
                TtfText.Draw(c, t.Extra, TtfFace.InterRegular, 11, 120, b.ExtraY + 2, Align.Center, HellText);
            }
            return c;
        }

        // Digits as high as the box allows, smaller if the width needs it.
        internal static void DrawTtf(Canvas c, string s, TtfFace face, Rectangle box, ushort fg)
        {
            // Commas and the euro sign reach below the digits; keep a little air above and below.
            float h = TtfText.Fit(s, face, box.Height * 0.84f, box.Width);
            TtfText.Draw(c, s, face, h, box.X + box.Width / 2, box.Y + (int)((box.Height - h) / 2), Align.Center, fg);
        }

        // ---- Fallblatt: cards on pure black, light grey labels ----

        private static Canvas Fallblatt(ShopBigView v, Texts t)
        {
            var c = new Canvas();
            TtfText.Draw(c, Short(t.Name, 24), TtfFace.MonoBold, 9, 10, 10, Align.Left, Flap.Label);
            TtfText.Draw(c, Clock(v), TtfFace.MonoBold, 9, 230, 10, Align.Right, Flap.Label);
            float th = TtfText.Fit(t.Title, TtfFace.MonoBold, 14, 220);
            TtfText.Draw(c, t.Title, TtfFace.MonoBold, th, 120, 30, Align.Center, Flap.Label);
            Boxes b = Layout(t, Margin, 224, 54, 230, 11);
            Flap.FitCards(c, t.Main, b.Main, Flap.Main);
            TtfText.Draw(c, t.Label, TtfFace.MonoBold, 10, 120, b.LabelY, Align.Center, Flap.Label);
            if (t.Sub != null)
            {
                Flap.FitCards(c, t.Sub, b.Sub, Flap.Second);
            }
            if (t.Extra != null)
            {
                TtfText.Draw(c, t.Extra, TtfFace.MonoBold, 10, 120, b.ExtraY, Align.Center, Flap.Second);
            }
            return c;
        }

        // ---- 4-Ton-Grün: four greens, frame 8 px inside the edge ----

        internal static readonly ushort G0 = Canvas.Rgb(15, 56, 15);
        internal static readonly ushort G1 = Canvas.Rgb(48, 98, 48);
        internal static readonly ushort G2 = Canvas.Rgb(139, 172, 15);
        internal static readonly ushort G3 = Canvas.Rgb(155, 188, 15);

        internal static void GreenFrame(Canvas c)
        {
            c.Clear(G3);
            c.DrawRect(Margin, Margin, 240 - 2 * Margin, 240 - 2 * Margin, G0);
            c.DrawRect(Margin + 1, Margin + 1, 238 - 2 * Margin, 238 - 2 * Margin, G0);
            c.DrawRect(Margin + 2, Margin + 2, 236 - 2 * Margin, 236 - 2 * Margin, G0);
        }

        private static Canvas Gruen(ShopBigView v, Texts t)
        {
            var c = new Canvas();
            GreenFrame(c);
            c.Text(Short(t.Name, 17), 16, 16, Align.Left, 1, G1, G3);
            c.Text(Clock(v), 224, 16, Align.Right, 1, G1, G3);
            c.TextScaled(t.Title, 120, 36, Align.Center, t.Title.Length > 12 ? 1 : 2, 2, G0, G3);
            Boxes b = Layout(t, 16, 208, 74, 222, 16);
            if (t.Extra != null)
            {
                // Only revenue: the amount sits in the field of the second lightest green.
                c.FillRect(16, b.Main.Y, 208, b.Main.Height, G2);
                BigVga.FitText(c, t.Main, new Rectangle(20, b.Main.Y + 2, 200, b.Main.Height - 4), G0);
            }
            else
            {
                BigVga.FitText(c, t.Main, b.Main, G0);
            }
            c.Text(t.Label, 120, b.LabelY, Align.Center, 1, G0, G3);
            if (t.Sub != null)
            {
                c.FillRect(16, b.Sub.Y, 208, b.Sub.Height, G2);
                BigVga.FitText(c, t.Sub, new Rectangle(20, b.Sub.Y + 2, 200, b.Sub.Height - 4), G0);
            }
            if (t.Extra != null)
            {
                c.Text(t.Extra, 120, b.ExtraY, Align.Center, 1, G1, G3);
            }
            return c;
        }
    }
}
