// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// "Shop als Bild": four designs, 240x240 with the 8x16 VGA font (30
// characters per line). DOS (table), Shop Heute (today's orders), Kassenbon
// (receipt) and Shop Woche (bars of the last 7 days).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SysopTV.Tray
{
    internal sealed class ShopView
    {
        internal ShopData Data;
        internal string ShopName;
        internal DateTime Now;
        internal string Stamp;  // "Stand HH:MM" if the numbers are not fresh, otherwise null
    }

    internal static class ShopDesigns
    {
        internal static readonly DesignInfo[] All =
        {
            new DesignInfo("dos", "DOS"),
            new DesignInfo("heute", "Shop Heute"),
            new DesignInfo("bon", "Kassenbon"),
            new DesignInfo("woche", "Shop Woche")
        };

        internal const string Default = "dos";

        private static readonly Dictionary<string, Func<ShopView, Canvas>> Renderers = new Dictionary<string, Func<ShopView, Canvas>>
        {
            { "dos", Dos },
            { "heute", Today },
            { "bon", Receipt },
            { "woche", Week }
        };

        internal static bool Known(string id)
        {
            return id != null && Renderers.ContainsKey(id);
        }

        internal static Canvas Render(string design, ShopView v)
        {
            Func<ShopView, Canvas> r;
            if (!Renderers.TryGetValue(design ?? "", out r))
            {
                r = Dos;
            }
            return r(v);
        }

        private static readonly ushort Black = 0;

        private static string Clock(DateTime t)
        {
            return t.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        private static string Name(ShopView v, int max)
        {
            string name = string.IsNullOrWhiteSpace(v.ShopName) ? "Shop" : v.ShopName.Trim();
            return name.Length > max ? name.Substring(0, max - 1) + "." : name;
        }

        // ---- DOS ----

        private static readonly ushort DosBack = Canvas.Rgb(0, 0, 170);
        private static readonly ushort DosBar = Canvas.Rgb(0, 170, 170);
        private static readonly ushort DosFrame = Canvas.Rgb(85, 255, 255);
        private static readonly ushort DosGray = Canvas.Rgb(170, 170, 170);
        private static readonly ushort DosYellow = Canvas.Rgb(255, 255, 85);
        private static readonly ushort DosWhite = Canvas.Rgb(255, 255, 255);

        // Columns: label from x 12, orders right aligned at 136, revenue at 228.
        private const int LabelX = 12, OrdersRight = 136, RevenueRight = 228;

        private static Canvas Dos(ShopView v)
        {
            ShopData d = v.Data;
            var c = new Canvas();
            c.Clear(DosBack);

            c.FillRect(0, 0, 240, 18, DosBar);
            c.Text(Name(v, 23), 4, 1, Align.Left, 1, Black, DosBar);
            c.Text(Clock(v.Now), 236, 1, Align.Right, 1, Black, DosBar);

            c.DoubleFrame(2, 22, 236, 128, DosFrame);
            c.Text("Best.", OrdersRight, 28, Align.Right, 1, DosGray, DosBack);
            c.Text("Umsatz", RevenueRight, 28, Align.Right, 1, DosGray, DosBack);
            DosRow(c, 46, "Heute", d.Today, DosYellow);
            DosRow(c, 64, "Gestern", d.Yesterday, DosWhite);
            DosRow(c, 82, "7 Tage", d.Week, DosWhite);
            DosRow(c, 100, "Monat", d.Month, DosWhite);
            c.FillRect(8, 121, 224, 1, DosFrame);
            c.Text("Durchschnitt", LabelX, 127, Align.Left, 1, DosGray, DosBack);
            string avg = d.Month.Orders > 0 ? ShopFormat.Money(d.Month.Revenue / d.Month.Orders) : "--";
            c.Text(avg, RevenueRight, 127, Align.Right, 1, DosWhite, DosBack);

            c.Text("Offen:", 6, 160, Align.Left, 1, DosGray, DosBack);
            if (d.Unshipped > 0)
            {
                c.Text(d.Unshipped + " zu versenden", 70, 160, Align.Left, 1, DosYellow, DosBack);
            }
            else
            {
                c.Text("nichts offen", 70, 160, Align.Left, 1, DosWhite, DosBack);
            }
            c.Text("Letzte:", 6, 186, Align.Left, 1, DosGray, DosBack);
            string last = d.LastOrderTime.HasValue
                ? Clock(d.LastOrderTime.Value) + "  " + ShopFormat.Money(d.LastOrderAmount)
                : "keine";
            c.Text(last, 70, 186, Align.Left, 1, DosWhite, DosBack);

            c.FillRect(0, 222, 240, 18, DosBar);
            c.Text("Shopify", 4, 223, Align.Left, 1, Black, DosBar);
            c.Text(v.Stamp ?? "Stand vor " + ShopFormat.Age(v.Now - d.FetchedAt), 236, 223, Align.Right, 1, Black, DosBar);
            return c;
        }

        private static void DosRow(Canvas c, int y, string label, ShopPeriod p, ushort colour)
        {
            c.Text(label, LabelX, y, Align.Left, 1, colour, DosBack);
            c.Text(p.Orders.ToString(CultureInfo.InvariantCulture), OrdersRight, y, Align.Right, 1, colour, DosBack);
            c.Text(ShopFormat.Money(p.Revenue), RevenueRight, y, Align.Right, 1, colour, DosBack);
        }

        // ---- Shop Heute ----

        private static readonly ushort AnsiMagenta = Canvas.Rgb(220, 40, 220);
        private static readonly ushort AnsiCyan = Canvas.Rgb(40, 230, 240);
        private static readonly ushort AnsiYellow = Canvas.Rgb(255, 225, 30);
        private static readonly ushort AnsiGreen = Canvas.Rgb(70, 230, 0);
        private static readonly ushort AnsiWhite = Canvas.Rgb(210, 210, 210);
        private static readonly ushort AnsiGray = Canvas.Rgb(100, 100, 100);

        private const int RowTop = 32, RowPitch = 18, MaxRows = 10;

        private static Canvas Today(ShopView v)
        {
            ShopData d = v.Data;
            var c = new Canvas();
            c.Text("HEUTE", 8, 4, Align.Left, 1, AnsiCyan, Black);
            c.Text(Clock(v.Now), 232, 4, Align.Right, 1, AnsiGray, Black);
            if (v.Stamp != null)
            {
                c.Text(v.Stamp, 120, 4, Align.Center, 1, AnsiGray, Black);
            }
            // Block line: solid, then the shades 0xB2, 0xB1, 0xB0 to the right.
            for (int i = 0; i < 28; i++)
            {
                int level = i < 22 ? 4 : i < 24 ? 3 : i < 26 ? 2 : 1;
                c.FillShade(8 + i * 8, 23, 8, 4, level, 1, AnsiMagenta, Black);
            }

            int total = d.Today.Orders;
            if (total == 0)
            {
                c.Text("Noch keine Bestellung heute", 120, 96, Align.Center, 1, AnsiWhite, Black);
                string last = d.LastOrderTime.HasValue
                    ? "letzte " + ShopFormat.When(d.LastOrderTime.Value, d.ShopNow.Date)
                    : "noch keine Bestellung";
                c.Text(last, 120, 120, Align.Center, 1, AnsiGray, Black);
            }
            else
            {
                List<ShopOrder> orders = d.TodayOrders;
                bool more = total > MaxRows;
                int rows = Math.Min(orders.Count, more ? MaxRows - 1 : MaxRows);
                for (int i = 0; i < rows; i++)
                {
                    ShopOrder o = orders[i];
                    int y = RowTop + i * RowPitch;
                    c.Text(Clock(o.Time), 8, y, Align.Left, 1, AnsiGray, Black);
                    c.Text(o.Number, 56, y, Align.Left, 1, AnsiCyan, Black);
                    c.Text(ShopFormat.Money(o.Amount), 212, y, Align.Right, 1, AnsiWhite, Black);
                    if (o.Shipped)
                    {
                        Tick(c, 220, y + 4, AnsiGreen);
                    }
                    else
                    {
                        Dot(c, 226, y + 8, AnsiYellow);
                    }
                }
                if (more)
                {
                    c.Text("+" + (total - rows) + " weitere", 56, RowTop + rows * RowPitch, Align.Left, 1, AnsiGray, Black);
                }
            }

            c.FillRect(8, 214, 224, 1, AnsiMagenta);
            c.Text(ShopFormat.Orders(total), 8, 221, Align.Left, 1, AnsiWhite, Black);
            string sum = ShopFormat.Money(d.Today.Revenue);
            int w = Canvas.TextWidth(sum, 1);
            c.FillRect(232 - w - 4, 219, w + 8, 20, AnsiYellow);
            c.Text(sum, 232, 221, Align.Right, 1, Black, AnsiYellow);
            return c;
        }

        // Check mark, 10 x 8 pixels, two pixels thick.
        private static void Tick(Canvas c, int x, int y, ushort colour)
        {
            for (int i = 0; i <= 3; i++)
            {
                c.FillRect(x + i, y + 3 + i, 2, 2, colour);
            }
            for (int i = 0; i <= 6; i++)
            {
                c.FillRect(x + 3 + i, y + 6 - i, 2, 2, colour);
            }
        }

        private static void Dot(Canvas c, int cx, int cy, ushort colour)
        {
            for (int dy = -3; dy <= 3; dy++)
            {
                for (int dx = -3; dx <= 3; dx++)
                {
                    if (dx * dx + dy * dy <= 10)
                    {
                        c.FillRect(cx + dx, cy + dy, 1, 1, colour);
                    }
                }
            }
        }

        // ---- Kassenbon ----

        private static readonly ushort Paper = Canvas.Rgb(235, 232, 222);
        private static readonly ushort Ink = Canvas.Rgb(30, 30, 30);
        private static readonly ushort InkMuted = Canvas.Rgb(120, 117, 108);
        private static readonly string Dashes = new string('-', 28);

        private static Canvas Receipt(ShopView v)
        {
            ShopData d = v.Data;
            var c = new Canvas();
            c.Clear(Paper);
            c.Text(Name(v, 28).ToUpperInvariant(), 120, 6, Align.Center, 1, Ink, Paper);
            string when = v.Now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) + "  " + (v.Stamp ?? Clock(v.Now));
            c.Text(when, 120, 24, Align.Center, 1, InkMuted, Paper);
            c.Text(Dashes, 120, 42, Align.Center, 1, InkMuted, Paper);
            ReceiptRow(c, 58, "HEUTE", d.Today);
            ReceiptRow(c, 76, "GESTERN", d.Yesterday);
            ReceiptRow(c, 94, "7 TAGE", d.Week);
            ReceiptRow(c, 112, "MONAT", d.Month);
            c.Text(Dashes, 120, 130, Align.Center, 1, InkMuted, Paper);
            c.Text("SUMME HEUTE", 8, 150, Align.Left, 1, Ink, Paper);
            c.TextWide(ShopFormat.Money(d.Today.Revenue), 232, 150, Align.Right, Ink, Paper);
            c.Text("OFFEN: " + d.Unshipped, 8, 174, Align.Left, 1, Ink, Paper);
            c.Text("DANKE FÜR IHREN EINKAUF", 120, 198, Align.Center, 1, InkMuted, Paper);
            // Torn edge: teeth of 12 pixels, below them the black of the display.
            for (int x = 0; x < 240; x++)
            {
                int edge = 234 - Math.Abs(x % 12 - 6);
                c.FillRect(x, edge, 1, 240 - edge, Black);
            }
            return c;
        }

        private static void ReceiptRow(Canvas c, int y, string label, ShopPeriod p)
        {
            c.Text(label, 8, y, Align.Left, 1, Ink, Paper);
            c.Text(p.Orders + " BEST.", 136, y, Align.Right, 1, InkMuted, Paper);
            c.Text(ShopFormat.Money(p.Revenue), 232, y, Align.Right, 1, Ink, Paper);
        }

        // ---- Shop Woche ----

        private static readonly string[] Initials = { "S", "M", "D", "M", "D", "F", "S" };
        private const int BarsBottom = 200, BarsHeight = 84, BarW = 24, BarGap = 8, BarsLeft = 12;

        private static Canvas Week(ShopView v)
        {
            ShopData d = v.Data;
            var c = new Canvas();
            ushort main = ClaudeDesigns.AmberMain, dim = ClaudeDesigns.AmberDim;
            c.Text("WOCHE", 8, 4, Align.Left, 1, dim, Black);
            c.Text(Clock(v.Now), 232, 4, Align.Right, 1, dim, Black);
            if (v.Stamp != null)
            {
                c.Text(v.Stamp, 120, 4, Align.Center, 1, dim, Black);
            }

            List<ShopPeriod> days = d.Days.Count >= 7 ? d.Days.Skip(d.Days.Count - 7).ToList() : new List<ShopPeriod>();
            c.Text(ShopFormat.Money(d.Week.Revenue), 120, 28, Align.Center, 3, main, Black);
            decimal? before = d.PreviousWeekRevenue;
            if (before.HasValue && before.Value > 0)
            {
                int change = (int)Math.Round((d.Week.Revenue - before.Value) * 100 / before.Value, MidpointRounding.AwayFromZero);
                string text = (change > 0 ? "+" : "") + change + " % zur Vorwoche";
                c.Text(text, 120, 80, Align.Center, 1, change < 0 ? ClaudeDesigns.AmberWarn : main, Black);
            }

            decimal max = days.Count > 0 ? days.Max(p => p.Revenue) : 0;
            int top = BarsBottom - BarsHeight;
            for (int i = 0; i < days.Count; i++)
            {
                int x = BarsLeft + i * (BarW + BarGap);
                c.DrawRect(x, top, BarW, BarsHeight, dim);
                int h = max > 0 ? (int)Math.Round(days[i].Revenue * BarsHeight / max, MidpointRounding.AwayFromZero) : 0;
                if (days[i].Revenue > 0 && h < 2)
                {
                    h = 2;
                }
                c.FillRect(x, BarsBottom - h, BarW, h, main);
                bool today = i == days.Count - 1;
                string initial = Initials[(int)days[i].Date.DayOfWeek];
                if (today)
                {
                    c.FillRect(x, BarsBottom + 4, BarW, 16, main);
                    c.Text(initial, x + BarW / 2, BarsBottom + 4, Align.Center, 1, Black, main);
                }
                else
                {
                    c.Text(initial, x + BarW / 2, BarsBottom + 4, Align.Center, 1, dim, Black);
                }
            }
            if (max > 0)
            {
                int best = days.FindIndex(p => p.Revenue == max);
                c.Text(ShopFormat.Whole(max), BarsLeft + best * (BarW + BarGap) + BarW / 2, top - 18, Align.Center, 1, main, Black);
            }

            string avg = d.Week.Orders > 0 ? ShopFormat.Money(d.Week.Revenue / d.Week.Orders) : "--";
            c.Text(d.Week.Orders + " Best., Schnitt " + avg, 120, 224, Align.Center, 1, dim, Black);
            return c;
        }
    }
}
