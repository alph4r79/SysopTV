// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Texts and values for the screen, ported from usage_data.cpp of the
// firmware: same rounding of reset times, same countdown, same week text.

using System;
using System.Globalization;

namespace SysopTV.Tray
{
    internal sealed class ViewColumn
    {
        internal bool Present;
        internal bool Warn;      // less than 20 % left, in both modes
        internal int Percent;    // the value to show
        internal int Segments;   // 0 to 10
        internal string Bottom;  // countdown or reset time
    }

    internal enum ViewScreen { Limits, Login, NoCarrier }

    internal sealed class ViewModel
    {
        internal ViewScreen Screen;
        internal string Clock;
        internal ViewColumn Session;
        internal ViewColumn Week;
        internal string LastData;
        internal bool ShowUsed;
        internal string Address = "";
    }

    internal static class UsageView
    {
        private const int WarnBelow = 20;
        private const int SegmentCount = 10;
        private static readonly string[] Days = { "So", "Mo", "Di", "Mi", "Do", "Fr", "Sa" };

        // Reset rounded up to the full minute, like usageResetRounded().
        internal static long ResetRounded(long resetsAt)
        {
            return (resetsAt + 59) / 60 * 60;
        }

        internal static string Countdown(long secondsLeft)
        {
            if (secondsLeft <= 0)
            {
                return "Reset faellig";
            }
            long minutes = (secondsLeft + 59) / 60;
            long hours = Math.Min(99, minutes / 60);
            return "noch " + hours + ":" + (minutes % 60).ToString("00");
        }

        internal static string WeekReset(DateTime nowLocal, DateTime resetLocal)
        {
            if (resetLocal <= nowLocal)
            {
                return "Reset faellig";
            }
            string clock = resetLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
            int days = (resetLocal.Date - nowLocal.Date).Days;
            if (days == 0)
            {
                return "heute " + clock;
            }
            if (days == 1)
            {
                return "morgen " + clock;
            }
            return Days[(int)resetLocal.DayOfWeek] + " " + clock;
        }

        internal static ViewColumn Column(UsageLimit limit, bool isSession, bool showUsed, DateTimeOffset now)
        {
            var c = new ViewColumn();
            if (limit == null)
            {
                c.Bottom = "keine Daten";
                return c;
            }
            c.Present = true;
            int remaining = 100 - limit.Used;
            c.Warn = remaining < WarnBelow;
            c.Percent = showUsed ? limit.Used : remaining;
            c.Segments = (c.Percent * SegmentCount + 99) / 100;
            if (!limit.ResetsAt.HasValue)
            {
                c.Bottom = isSession ? "noch -:--" : "--";
            }
            else
            {
                long reset = ResetRounded(limit.ResetsAt.Value);
                if (isSession)
                {
                    c.Bottom = Countdown(reset - now.ToUnixTimeSeconds());
                }
                else
                {
                    DateTime resetLocal = DateTimeOffset.FromUnixTimeSeconds(reset).ToLocalTime().DateTime;
                    c.Bottom = WeekReset(now.ToLocalTime().DateTime, resetLocal);
                }
            }
            return c;
        }

        internal static ViewModel Build(UsageData data, UsageData lastOk, DateTime? lastOkTime, bool showUsed, DateTimeOffset now)
        {
            var v = new ViewModel { ShowUsed = showUsed, Clock = now.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) };
            UsageData values = data != null && data.State == "ok" ? data : lastOk;
            v.Session = Column(values != null ? values.Session : null, true, showUsed, now);
            v.Week = Column(values != null ? values.Week : null, false, showUsed, now);
            if (data == null || data.State == "error")
            {
                v.Screen = ViewScreen.NoCarrier;
                v.LastData = lastOkTime.HasValue ? "letzte Daten " + lastOkTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "noch keine Daten";
            }
            else if (data.State == "login")
            {
                v.Screen = ViewScreen.Login;
            }
            else
            {
                v.Screen = ViewScreen.Limits;
            }
            return v;
        }
    }
}
