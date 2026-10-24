// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// "Benachrichtigen, wenn das Limit knapp wird": after every fresh query the
// numbers are compared with the thresholds. Each limit gets at most one
// Windows message per threshold and period. The session period ends with its
// reset, the week period with the week reset. What was shown is kept in
// state.json, so a restart does not repeat it. Works whether or not a cube
// shows Claude.

using System;
using System.Collections.Generic;

namespace SysopTV.Tray
{
    internal static class LimitNotifier
    {
        // Anthropic moves the reset time by a few seconds now and then. A reset
        // within this distance of the stored one is the same period.
        private static readonly long SamePeriodSeconds = (long)TimeSpan.FromMinutes(30).TotalSeconds;

        // The messages to show for these numbers. Updates the memory in state.
        internal static List<string> Check(UsageLimit session, UsageLimit week, NotifyConfig cfg, AppState state, DateTimeOffset now)
        {
            var messages = new List<string>();
            if (cfg == null || !cfg.Enabled)
            {
                return messages;
            }
            string text = CheckOne(session, true, cfg.Session, ref state.NotifiedSessionReset, ref state.NotifiedSessionLevel, now);
            if (text != null)
            {
                messages.Add(text);
            }
            text = CheckOne(week, false, cfg.Week, ref state.NotifiedWeekReset, ref state.NotifiedWeekLevel, now);
            if (text != null)
            {
                messages.Add(text);
            }
            return messages;
        }

        private static string CheckOne(UsageLimit l, bool isSession, int threshold, ref long periodReset, ref int level, DateTimeOffset now)
        {
            // Without a reset time there is no period: an inactive session warns never.
            if (threshold <= 0 || l == null || !l.ResetsAt.HasValue)
            {
                return null;
            }
            long reset = UsageView.ResetRounded(l.ResetsAt.Value);
            if (reset <= now.ToUnixTimeSeconds())
            {
                return null;
            }
            if (Math.Abs(reset - periodReset) > SamePeriodSeconds)
            {
                // A new period: nothing shown yet.
                periodReset = reset;
                level = 0;
            }
            if (l.Used < threshold || threshold <= level)
            {
                return null;
            }
            level = threshold;
            return Text(l, isSession, now);
        }

        // "Claude: Session bei 91 %. Reset in 1:12." or "Claude: Woche bei 90 %. Reset Do 23:00."
        internal static string Text(UsageLimit l, bool isSession, DateTimeOffset now)
        {
            long reset = UsageView.ResetRounded(l.ResetsAt.Value);
            string when;
            if (isSession)
            {
                long minutes = (reset - now.ToUnixTimeSeconds() + 59) / 60;
                when = "in " + Math.Min(99, minutes / 60) + ":" + (minutes % 60).ToString("00");
            }
            else
            {
                when = UsageView.WeekReset(now.LocalDateTime, DateTimeOffset.FromUnixTimeSeconds(reset).LocalDateTime);
            }
            return "Claude: " + (isSession ? "Session" : "Woche") + " bei " + l.Used + " %. Reset " + when + ".";
        }
    }
}
