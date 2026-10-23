// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// "Shop als Bild": orders and revenue in the DOS style, 240x240 with the
// 8x16 VGA font (30 characters per line).

using System;
using System.Globalization;

namespace SysopTV.Tray
{
    internal sealed class ShopPeriod
    {
        internal int Orders;
        internal decimal Revenue;
    }

    internal sealed class ShopData
    {
        internal ShopPeriod Today = new ShopPeriod();
        internal ShopPeriod Yesterday = new ShopPeriod();
        internal ShopPeriod Week = new ShopPeriod();   // last 7 days including today
        internal ShopPeriod Month = new ShopPeriod();  // current month
        internal int Unshipped;
        internal DateTime? LastOrderTime;              // shop time
        internal decimal LastOrderAmount;
        internal DateTime FetchedAt;                   // when the data was read
        internal string Error;                         // null if fine

        internal static ShopData Sample()
        {
            DateTime now = DateTime.Now;
            return new ShopData
            {
                Today = new ShopPeriod { Orders = 4, Revenue = 184.50m },
                Yesterday = new ShopPeriod { Orders = 7, Revenue = 412.80m },
                Week = new ShopPeriod { Orders = 31, Revenue = 1876.40m },
                Month = new ShopPeriod { Orders = 52, Revenue = 3022.15m },
                Unshipped = 3,
                LastOrderTime = now.AddMinutes(-23),
                LastOrderAmount = 49.90m,
                FetchedAt = now.AddSeconds(-40)
            };
        }
    }

    internal static class ShopRenderer
    {
        internal static readonly ushort Back = Canvas.Rgb(0, 0, 170);
        internal static readonly ushort Bar = Canvas.Rgb(0, 170, 170);
        internal static readonly ushort Frame = Canvas.Rgb(85, 255, 255);
        internal static readonly ushort Gray = Canvas.Rgb(170, 170, 170);
        internal static readonly ushort Yellow = Canvas.Rgb(255, 255, 85);
        internal static readonly ushort White = Canvas.Rgb(255, 255, 255);
        private static readonly ushort Black = 0;
        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

        // Columns: label from x 12, orders right aligned at 136, revenue at 228.
        private const int LabelX = 12;
        private const int OrdersRight = 136;
        private const int RevenueRight = 228;

        // 184,50 €, from 1000 without cents and with a dot: 3.022 €
        internal static string Money(decimal amount)
        {
            if (Math.Abs(amount) >= 1000)
            {
                return Math.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("#,##0", German) + " €";
            }
            return amount.ToString("0.00", German) + " €";
        }

        internal static string Age(TimeSpan age)
        {
            int s = Math.Max(0, (int)age.TotalSeconds);
            if (s < 90)
            {
                return s + " s";
            }
            if (s < 2 * 3600)
            {
                return s / 60 + " min";
            }
            return s / 3600 + " h";
        }

        internal static Canvas Render(ShopData d, string shopName, DateTime now)
        {
            var c = new Canvas();
            c.Clear(Back);

            // Header bar: shop name left (shortened), clock right.
            c.FillRect(0, 0, 240, 18, Bar);
            string name = string.IsNullOrWhiteSpace(shopName) ? "Shop" : shopName.Trim();
            if (name.Length > 23)
            {
                name = name.Substring(0, 22) + ".";
            }
            c.Text(name, 4, 1, Align.Left, 1, Black, Bar);
            c.Text(now.ToString("HH:mm", CultureInfo.InvariantCulture), 236, 1, Align.Right, 1, Black, Bar);

            // Double frame with the table.
            c.DoubleFrame(2, 22, 236, 128, Frame);
            c.Text("Best.", OrdersRight, 28, Align.Right, 1, Gray, Back);
            c.Text("Umsatz", RevenueRight, 28, Align.Right, 1, Gray, Back);
            Row(c, 46, "Heute", d.Today, Yellow);
            Row(c, 64, "Gestern", d.Yesterday, White);
            Row(c, 82, "7 Tage", d.Week, White);
            Row(c, 100, "Monat", d.Month, White);
            c.FillRect(8, 121, 224, 1, Frame);
            c.Text("Ø Bestellung", LabelX, 127, Align.Left, 1, Gray, Back);
            string avg = d.Month.Orders > 0 ? Money(d.Month.Revenue / d.Month.Orders) : "--";
            c.Text(avg, RevenueRight, 127, Align.Right, 1, White, Back);

            // Below the frame: open orders and the last order.
            c.Text("Offen:", 6, 160, Align.Left, 1, Gray, Back);
            if (d.Unshipped > 0)
            {
                c.Text(d.Unshipped + " zu versenden", 70, 160, Align.Left, 1, Yellow, Back);
            }
            else
            {
                c.Text("nichts offen", 70, 160, Align.Left, 1, White, Back);
            }
            c.Text("Letzte:", 6, 186, Align.Left, 1, Gray, Back);
            string last = d.LastOrderTime.HasValue
                ? d.LastOrderTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture) + "  " + Money(d.LastOrderAmount)
                : "keine";
            c.Text(last, 70, 186, Align.Left, 1, White, Back);

            // Footer bar.
            c.FillRect(0, 222, 240, 18, Bar);
            c.Text("Shopify", 4, 223, Align.Left, 1, Black, Bar);
            string state = d.Error != null ? "Fehler" : "Stand vor " + Age(now - d.FetchedAt);
            c.Text(state, 236, 223, Align.Right, 1, Black, Bar);
            return c;
        }

        private static void Row(Canvas c, int y, string label, ShopPeriod p, ushort colour)
        {
            c.Text(label, LabelX, y, Align.Left, 1, colour, Back);
            c.Text(p.Orders.ToString(CultureInfo.InvariantCulture), OrdersRight, y, Align.Right, 1, colour, Back);
            c.Text(Money(p.Revenue), RevenueRight, y, Align.Right, 1, colour, Back);
        }
    }
}
