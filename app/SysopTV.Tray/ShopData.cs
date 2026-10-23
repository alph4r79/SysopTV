// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Shop numbers as the shop designs need them. On purpose without any customer
// data: no names, addresses or e-mail addresses, only order number, time,
// amount and whether the order is shipped.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SysopTV.Tray
{
    internal sealed class ShopPeriod
    {
        internal DateTime Date;  // only for the days of the last 14 days
        internal int Orders;
        internal decimal Revenue;
    }

    internal sealed class ShopOrder
    {
        internal string Number;  // like "#1043"
        internal DateTime Time;  // shop time
        internal decimal Amount;
        internal bool Shipped;
    }

    internal sealed class ShopData
    {
        internal const int MaxTodayOrders = 20;

        internal ShopPeriod Today = new ShopPeriod();
        internal ShopPeriod Yesterday = new ShopPeriod();
        internal ShopPeriod Week = new ShopPeriod();   // last 7 days including today
        internal ShopPeriod Month = new ShopPeriod();  // current month
        internal List<ShopPeriod> Days = new List<ShopPeriod>();       // last 14 days, oldest first, today last
        internal List<ShopOrder> TodayOrders = new List<ShopOrder>();  // newest first, at most 20
        internal int Unshipped;
        internal DateTime? LastOrderTime;              // shop time
        internal decimal LastOrderAmount;
        internal DateTime ShopNow;                     // shop time when the data was read
        internal DateTime FetchedAt;                   // PC time when the data was read
        internal string Error;                         // null if fine

        // Revenue of the 7 days before the last 7 days, null without data.
        internal decimal? PreviousWeekRevenue
        {
            get { return Days.Count >= 14 ? Days.Take(7).Sum(d => d.Revenue) : (decimal?)null; }
        }

        internal static ShopData Sample(int todayOrders)
        {
            DateTime now = DateTime.Now;
            DateTime today = now.Date;
            var d = new ShopData { FetchedAt = now.AddSeconds(-40), ShopNow = now, Unshipped = 3 };
            decimal[] revenue = { 250m, 230m, 215m, 260m, 240m, 245m, 235m, 211.40m, 318.90m, 165.00m, 401.30m, 182.50m, 412.80m, 184.50m };
            int[] orders = { 4, 4, 3, 5, 4, 4, 4, 4, 5, 3, 6, 2, 7, 4 };
            for (int i = 0; i < 14; i++)
            {
                d.Days.Add(new ShopPeriod { Date = today.AddDays(i - 13), Orders = orders[i], Revenue = revenue[i] });
            }
            d.Yesterday = new ShopPeriod { Orders = 7, Revenue = 412.80m };
            d.Week = new ShopPeriod { Orders = 31, Revenue = 1876.40m };
            d.Month = new ShopPeriod { Orders = 52, Revenue = 3022.15m };

            decimal[] amounts = { 49.90m, 34.90m, 59.80m, 39.90m, 24.95m, 89.00m, 19.90m, 44.50m, 64.90m, 29.90m, 54.00m, 39.00m, 74.90m, 22.50m };
            DateTime time = today.AddHours(Math.Max(1, Math.Min(now.Hour, 22))).AddMinutes(32);
            for (int i = 0; i < todayOrders; i++)
            {
                d.TodayOrders.Add(new ShopOrder
                {
                    Number = "#" + (1046 + todayOrders - 4 - i).ToString(CultureInfo.InvariantCulture),
                    Time = time,
                    Amount = amounts[i % amounts.Length],
                    Shipped = i >= 3
                });
                time = time.AddMinutes(-(37 + 11 * (i % 3)));
            }
            d.Today = new ShopPeriod { Date = today, Orders = todayOrders, Revenue = d.TodayOrders.Sum(o => o.Amount) };
            d.Days[13].Orders = d.Today.Orders;
            d.Days[13].Revenue = d.Today.Revenue;
            if (todayOrders > 0)
            {
                d.LastOrderTime = d.TodayOrders[0].Time;
                d.LastOrderAmount = d.TodayOrders[0].Amount;
            }
            else
            {
                d.LastOrderTime = today.AddDays(-1).AddHours(21).AddMinutes(14);
                d.LastOrderAmount = 39.90m;
            }
            return d;
        }
    }

    internal static class ShopFormat
    {
        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
        private static readonly string[] Days = { "So", "Mo", "Di", "Mi", "Do", "Fr", "Sa" };

        // 184,50 €, from 1000 without cents and with a dot: 3.022 €
        internal static string Money(decimal amount)
        {
            if (Math.Abs(amount) >= 1000)
            {
                return Whole(amount);
            }
            return amount.ToString("0.00", German) + " €";
        }

        // Without cents: 413 €, 1.876 €
        internal static string Whole(decimal amount)
        {
            return Math.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("#,##0", German) + " €";
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

        internal static string Orders(int n)
        {
            return n + (n == 1 ? " Bestellung" : " Bestellungen");
        }

        // "gestern 21:14", "Mo 20.10. 21:14"
        internal static string When(DateTime time, DateTime today)
        {
            string clock = time.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (time.Date == today)
            {
                return "heute " + clock;
            }
            if (time.Date == today.AddDays(-1))
            {
                return "gestern " + clock;
            }
            return Days[(int)time.DayOfWeek] + " " + time.ToString("dd.MM.", CultureInfo.InvariantCulture) + " " + clock;
        }
    }
}
