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
using System.Runtime.Serialization;

namespace SysopTV.Tray
{
    // The classes are stored in state.json, so the last valid numbers survive
    // a restart of the program. They hold no customer data.
    [DataContract]
    internal sealed class ShopPeriod
    {
        [DataMember(Name = "date", EmitDefaultValue = false)] internal DateTime Date;  // only for the days of the last 14 days
        [DataMember(Name = "orders")] internal int Orders;
        [DataMember(Name = "revenue")] internal decimal Revenue;
    }

    [DataContract]
    internal sealed class ShopOrder
    {
        [DataMember(Name = "number")] internal string Number;  // like "#1043"
        [DataMember(Name = "time")] internal DateTime Time;    // shop time
        [DataMember(Name = "amount")] internal decimal Amount;
        [DataMember(Name = "shipped")] internal bool Shipped;
    }

    // The same model for Shopify and WooCommerce, so every shop view works with both.
    [DataContract]
    internal sealed class ShopData
    {
        internal const int MaxTodayOrders = 20;

        [DataMember(Name = "today")] internal ShopPeriod Today = new ShopPeriod();
        [DataMember(Name = "yesterday")] internal ShopPeriod Yesterday = new ShopPeriod();
        [DataMember(Name = "last7Days")] internal ShopPeriod Week = new ShopPeriod();          // last 7 days including today
        [DataMember(Name = "week")] internal ShopPeriod CalendarWeek = new ShopPeriod();      // since Monday 0:00, shop time
        [DataMember(Name = "month")] internal ShopPeriod Month = new ShopPeriod();            // current month
        [DataMember(Name = "year")] internal ShopPeriod Year = new ShopPeriod();              // since 1 January
        [DataMember(Name = "yearNote", EmitDefaultValue = false)] internal string YearNote;   // why there is no year value, null if fine
        [DataMember(Name = "days")] internal List<ShopPeriod> Days = new List<ShopPeriod>();         // last 14 days, oldest first, today last
        [DataMember(Name = "todayOrders")] internal List<ShopOrder> TodayOrders = new List<ShopOrder>();  // newest first, at most 20
        [DataMember(Name = "unshipped")] internal int Unshipped;
        [DataMember(Name = "lastOrderTime")] internal DateTime? LastOrderTime;   // shop time
        [DataMember(Name = "lastOrderAmount")] internal decimal LastOrderAmount;
        [DataMember(Name = "shop")] internal string ShopName = "";              // name of the shop at the source
        [DataMember(Name = "currency")] internal string Currency = "";          // like EUR, only WooCommerce sends it
        [DataMember(Name = "shopNow", EmitDefaultValue = false)] internal DateTime ShopNow;               // shop time when the data was read
        [DataMember(Name = "fetchedAt", EmitDefaultValue = false)] internal DateTime FetchedAt;           // PC time when the data was read
        internal string Plugin = "";             // version of the WooCommerce plugin
        internal string Error;                   // null if fine
        internal string ErrorKind;               // ShopErrors: what kind of error, for the notice screen
        internal int RetryAfterSeconds;          // the source asked to wait, 0 if not

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Today = new ShopPeriod();
            Yesterday = new ShopPeriod();
            Week = new ShopPeriod();
            CalendarWeek = new ShopPeriod();
            Month = new ShopPeriod();
            Year = new ShopPeriod();
            Days = new List<ShopPeriod>();
            TodayOrders = new List<ShopOrder>();
            ShopName = "";
            Currency = "";
            Plugin = "";
        }

        // Revenue of the 7 days before the last 7 days, null without data.
        internal decimal? PreviousWeekRevenue
        {
            get { return Days.Count >= 14 ? Days.Take(7).Sum(d => d.Revenue) : (decimal?)null; }
        }

        internal static ShopData Sample(int todayOrders)
        {
            return Sample(todayOrders, DateTime.Now);
        }

        // Sample numbers around a fixed moment, for the pictures of the documentation.
        internal static ShopData Sample(int todayOrders, DateTime now)
        {
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
            d.CalendarWeek = new ShopPeriod { Orders = 23, Revenue = 1398.70m };
            d.Month = new ShopPeriod { Orders = 52, Revenue = 3022.15m };
            d.Year = new ShopPeriod { Orders = 612, Revenue = 36418.70m };

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

    // Day values of finished days of one year, for the Shopify year. Stored in
    // state.json, so only today and the last 30 days are read again each time.
    [DataContract]
    internal sealed class YearCache
    {
        [DataMember(Name = "shop")] internal string Shop = "";  // shop address, another shop starts again
        [DataMember(Name = "year")] internal int Year;
        [DataMember(Name = "fullDate", EmitDefaultValue = false)] internal DateTime FullDate;  // shop day of the last full reload
        [DataMember(Name = "days")] internal List<ShopPeriod> Days = new List<ShopPeriod>();

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Days = new List<ShopPeriod>();
            Shop = "";
        }
    }

    // Day, week and month in the time zone of the shop. The week starts on
    // Monday at 0:00, the month on the 1st at 0:00.
    internal static class ShopCalendar
    {
        internal static DateTime ShopNow(DateTime utcNow, int offsetMinutes)
        {
            return DateTime.SpecifyKind(utcNow, DateTimeKind.Unspecified).AddMinutes(offsetMinutes);
        }

        internal static DateTime WeekStart(DateTime shopTime)
        {
            DateTime day = shopTime.Date;
            int sinceMonday = ((int)day.DayOfWeek + 6) % 7;
            return day.AddDays(-sinceMonday);
        }

        internal static DateTime MonthStart(DateTime shopTime)
        {
            return new DateTime(shopTime.Year, shopTime.Month, 1);
        }
    }

    // Kinds of errors of a shop source, for the notice screen on the cube.
    internal static class ShopErrors
    {
        internal const string Access = "access";    // key, token or secret wrong, scope missing, app not installed
        internal const string TooMany = "toomany";  // the source asked to wait
        internal const string Offline = "offline";  // not reachable or no answer
        internal const string Other = "other";
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
