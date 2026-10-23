// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Page "Claude": interval, login, last and next query, an active 429 block
// and "Jetzt abfragen".

using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SysopTV.Tray
{
    internal sealed class ClaudePage : Page
    {
        private readonly NumericUpDown interval = new NumericUpDown { Minimum = AppConfig.MinInterval, Maximum = 3600, Width = 80 };
        private readonly Label login = Value();
        private readonly Label lastQuery = Value();
        private readonly Label nextQuery = Value();
        private readonly Label block = Value();
        private readonly Label session = Value();
        private readonly Label week = Value();
        private readonly Label stand = Value();
        private readonly Button queryNow = new Button { Text = "Jetzt abfragen", AutoSize = true };
        private readonly Label queryNote = Note("");
        private DateTime? loginExpires;
        private DateTime loginRead = DateTime.MinValue;

        internal ClaudePage(PageContext ctx) : base(ctx)
        {
            interval.Value = Math.Min(interval.Maximum, ctx.Config.Interval);
            TableLayoutPanel g = Grid();
            AddRow(g, "Abfragen alle", Row(interval, Note("Sekunden, mindestens 120, sonst sperrt der Server (Fehler 429)")));
            AddRow(g, "Anmeldung", login);
            AddRow(g, "Letzte Abfrage", lastQuery);
            AddRow(g, "Nächste Abfrage", nextQuery);
            AddRow(g, "Sperre", block);
            AddRow(g, "Session", session);
            AddRow(g, "Woche", week);
            AddRow(g, "Stand der Werte", stand);
            AddRow(g, null, Row(queryNow, queryNote));
            Controls.Add(Stack(Heading("Claude"), g,
                Note("Das Programm liest die Limits mit der Anmeldung von Claude Code und erneuert sie nie selbst. Läuft sie ab, reicht es, Claude Code kurz zu benutzen.")));

            interval.Validated += (s, e) => SaveInterval();
            interval.ValueChanged += (s, e) =>
            {
                if (!interval.Focused)
                {
                    SaveInterval();
                }
            };
            queryNow.Click += (s, e) => QueryNow();
        }

        internal override string Title
        {
            get { return "Claude"; }
        }

        private void SaveInterval()
        {
            int v = (int)interval.Value;
            if (v != Ctx.Config.IntervalSeconds)
            {
                Ctx.Config.IntervalSeconds = v;
                Ctx.Save();
            }
        }

        private void QueryNow()
        {
            DateTime? last = Ctx.Service.LastQuery;
            if (Ctx.Service.BlockedUntil.HasValue)
            {
                queryNote.Text = "Anthropic bremst noch, gesendet werden die letzten Werte.";
            }
            else if (last.HasValue && DateTime.Now - last.Value < TimeSpan.FromSeconds(AppConfig.MinInterval))
            {
                queryNote.Text = "Neue Abfrage frühestens " + last.Value.AddSeconds(AppConfig.MinInterval).ToString("HH:mm:ss") + ", bis dahin gehen die letzten Werte raus.";
            }
            else
            {
                queryNote.Text = "Frage ab ...";
            }
            Ctx.Service.SendNow();
        }

        private static string Limit(UsageLimit l, bool isSession)
        {
            if (l == null)
            {
                return "keine Daten";
            }
            ViewColumn c = UsageView.Column(l, isSession, true, DateTimeOffset.Now);
            return l.Used + " % verbraucht, " + (100 - l.Used) + " % übrig, " + c.ResetLine;
        }

        internal override string[] Summary
        {
            get
            {
                UsageData data = Ctx.Service.LastData;
                UsageData ok = Ctx.Service.LastOk;
                var lines = new List<string>();
                if (data != null && data.State == "login")
                {
                    lines.Add("Anmeldung abgelaufen, Claude Code kurz benutzen.");
                }
                if (ok == null)
                {
                    lines.Add("Noch keine Werte.");
                    return lines.ToArray();
                }
                lines.Add("Session: " + Limit(ok.Session, true));
                lines.Add("Woche: " + Limit(ok.Week, false));
                lines.Add("Stand " + Time(Ctx.Service.LastOkTime));
                return lines.ToArray();
            }
        }

        internal override void UpdateView()
        {
            if (DateTime.Now - loginRead > TimeSpan.FromSeconds(30))
            {
                loginExpires = ClaudeClient.LoginExpires();
                loginRead = DateTime.Now;
            }
            UsageData data = Ctx.Service.LastData;
            if (data != null && data.State == "login" || loginExpires.HasValue && loginExpires.Value <= DateTime.Now)
            {
                login.Text = "abgelaufen, Claude Code kurz benutzen";
                login.ForeColor = MainForm.Colour(StatusKind.Warn);
            }
            else
            {
                login.Text = loginExpires.HasValue ? "gültig bis " + loginExpires.Value.ToString("dd.MM. HH:mm") : "unbekannt";
                login.ForeColor = ForeColor;
            }
            lastQuery.Text = Time(Ctx.Service.LastQuery);
            DateTime next = Ctx.Service.NextQuery;
            nextQuery.Text = next <= DateTime.Now ? "jetzt" : next.ToString("HH:mm:ss");
            DateTime? until = Ctx.Service.BlockedUntil;
            if (until.HasValue)
            {
                int minutes = (int)Math.Ceiling((until.Value - DateTime.Now).TotalMinutes);
                block.Text = "429-Pause bis " + until.Value.ToString("HH:mm") + ", noch " + minutes + " min";
                block.ForeColor = MainForm.Colour(StatusKind.Warn);
            }
            else
            {
                block.Text = "keine";
                block.ForeColor = ForeColor;
            }
            UsageData ok = Ctx.Service.LastOk;
            session.Text = Limit(ok != null ? ok.Session : null, true);
            week.Text = Limit(ok != null ? ok.Week : null, false);
            stand.Text = Time(Ctx.Service.LastOkTime);
            if (queryNote.Text == "Frage ab ..." && Ctx.Service.LastQuery.HasValue && DateTime.Now - Ctx.Service.LastQuery.Value < TimeSpan.FromSeconds(10))
            {
                queryNote.Text = "Abgefragt.";
            }
        }
    }
}
