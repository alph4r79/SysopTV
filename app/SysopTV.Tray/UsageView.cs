// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Texts and values for the screen, ported from usage_data.cpp of firmware
// 0.4.1: same rounding of reset times, same countdown, same week text, same
// "nicht aktiv" for a session without reset time.

using System;
using System.Globalization;

namespace SysopTV.Tray
{
    internal sealed class ViewColumn
    {
        internal bool Present;
        internal bool Warn;          // less than 20 % left, in both modes
        internal int Percent;        // the value to show
        internal int Remaining;      // percent left
        internal int Segments;       // 0 to 10
        internal string Bottom;      // countdown, reset time or a plain text
        internal bool BottomPlain;   // Bottom is no time, so no "Reset" in front
        internal string ResetClock = "";  // local time of the reset "HH:MM", empty if unknown

        // Like usageResetLine() of the firmware.
        internal string ResetLine
        {
            get { return BottomPlain ? Bottom : "Reset " + Bottom; }
        }
    }

    internal enum ViewScreen { Limits, Login }

    internal sealed class ViewModel
    {
        internal ViewScreen Screen;
        internal string Clock;
        internal ViewColumn Session;
        internal ViewColumn Week;
        internal bool ShowUsed;
        internal string Stamp;  // "Stand HH:MM" if the values are not fresh, otherwise null
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
            var c = new ViewColumn { BottomPlain = true };
            if (limit == null)
            {
                c.Bottom = "keine Daten";
                return c;
            }
            c.Present = true;
            c.Remaining = 100 - limit.Used;
            c.Warn = c.Remaining < WarnBelow;
            c.Percent = showUsed ? limit.Used : c.Remaining;
            c.Segments = (c.Percent * SegmentCount + 99) / 100;
            if (!limit.ResetsAt.HasValue)
            {
                // Anthropic sends no reset time while no session is running.
                c.Bottom = isSession ? "nicht aktiv" : "--";
                return c;
            }
            long reset = ResetRounded(limit.ResetsAt.Value);
            DateTime resetLocal = DateTimeOffset.FromUnixTimeSeconds(reset).ToLocalTime().DateTime;
            c.ResetClock = resetLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (isSession)
            {
                c.Bottom = Countdown(reset - now.ToUnixTimeSeconds());
            }
            else
            {
                c.Bottom = WeekReset(now.ToLocalTime().DateTime, resetLocal);
            }
            c.BottomPlain = c.Bottom.StartsWith("Reset", StringComparison.Ordinal);
            return c;
        }

        // values: the last valid numbers (may be null), login: show "Anmeldung abgelaufen",
        // stamp: "Stand HH:MM" for values that are not fresh, or null.
        internal static ViewModel Build(UsageData values, bool login, string stamp, bool showUsed, DateTimeOffset now)
        {
            var v = new ViewModel
            {
                ShowUsed = showUsed,
                Clock = now.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture),
                Screen = login ? ViewScreen.Login : ViewScreen.Limits,
                Stamp = stamp
            };
            v.Session = Column(values != null ? values.Session : null, true, showUsed, now);
            v.Week = Column(values != null ? values.Week : null, false, showUsed, now);
            return v;
        }

        internal static string StampText(DateTime when)
        {
            return "Stand " + when.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
    }
}
