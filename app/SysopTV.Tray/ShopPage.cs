// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Common layout of the pages "Shopify" and "WooCommerce": a short guide with
// a link, the fields of the source, the name for the display, "Shop testen",
// the last query and today's numbers. Fields are saved when they are left.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SysopTV.Tray
{
    internal abstract class ShopPage : Page
    {
        private readonly Button test = new Button { Text = "Shop testen", AutoSize = true };
        private readonly Label testStatus = new Label { AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(6, 7, 0, 0) };
        private readonly Label lastQuery = Value();
        private readonly Label today = Value();
        protected readonly TextBox DisplayName = new TextBox { Width = 260 };

        protected ShopPage(PageContext ctx) : base(ctx)
        {
        }

        // Id of the source in ShopSources.
        protected abstract string Source { get; }

        // Saves the fields into Ctx.Config, returns true if something changed.
        protected abstract bool CommitFields();

        // Text when the fields are not complete, null if they are.
        protected abstract string MissingText { get; }

        // Asks the source once with the current settings.
        protected abstract ShopData FetchNow();

        // The extra part of a successful test, like the API or plugin version.
        protected abstract string VersionText(ShopData d);

        // Builds the page: guide, link, the rows of the source, then the common rows.
        protected void Build(string heading, string guide, string linkText, string linkUrl, Action<TableLayoutPanel> sourceRows, string note)
        {
            var guideLabel = new Label { Text = guide, AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 2) };
            var link = new LinkLabel { Text = linkText, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
            link.LinkClicked += (sender, e) =>
            {
                try
                {
                    Process.Start(linkUrl);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(FindForm(), "Der Browser ließ sich nicht öffnen: " + ex.Message, "SysopTV");
                }
            };
            TableLayoutPanel g = Grid();
            sourceRows(g);
            AddRow(g, "Name für die Anzeige", DisplayName);
            AddRow(g, null, Row(test, testStatus));
            AddRow(g, "Letzte Abfrage", lastQuery);
            AddRow(g, "Heute", today);
            Controls.Add(Stack(Heading(heading), guideLabel, link, g, Note(note)));
            foreach (Control c in g.Controls)
            {
                WatchLeave(c);
            }
            test.Click += async (sender, e) => await Test();
        }

        private void WatchLeave(Control c)
        {
            if (c is TextBox)
            {
                c.Leave += (sender, e) => Commit();
            }
            foreach (Control child in c.Controls)
            {
                WatchLeave(child);
            }
        }

        private void Commit()
        {
            if (CommitFields())
            {
                Ctx.Save();
            }
        }

        private async Task Test()
        {
            Commit();
            if (MissingText != null)
            {
                testStatus.Text = MissingText;
                return;
            }
            test.Enabled = false;
            testStatus.Text = "Frage den Shop ab ...";
            ShopData d = await Task.Run(() => FetchNow());
            if (IsDisposed)
            {
                return;
            }
            test.Enabled = true;
            testStatus.Text = TestText(d, VersionText(d));
        }

        // Result of "Shop testen" in one line. Public for the offline tests.
        internal static string TestText(ShopData d, string version)
        {
            if (d.Error != null)
            {
                return "Fehler: " + d.Error;
            }
            string name = string.IsNullOrEmpty(d.ShopName) ? "dem Shop" : d.ShopName;
            return "Verbunden mit " + name + ": heute " + ShopFormat.Orders(d.Today.Orders) + ", " + version + ".";
        }

        private static string Today(ShopData d)
        {
            return ShopFormat.Orders(d.Today.Orders) + ", " + ShopFormat.Money(d.Today.Revenue) + ", offen " + d.Unshipped;
        }

        internal override string[] Summary
        {
            get
            {
                if (!Ctx.Config.SourceConfigured(Source))
                {
                    return null;
                }
                ShopData d = Ctx.Service.ShopOf(Source);
                if (d == null)
                {
                    return new[] { "Noch keine Werte." };
                }
                return new List<string>
                {
                    "Heute: " + Today(d),
                    "Gestern: " + ShopFormat.Orders(d.Yesterday.Orders) + ", " + ShopFormat.Money(d.Yesterday.Revenue),
                    "Stand " + Time(d.FetchedAt) + (d.Error != null ? ", letzte Abfrage fehlgeschlagen: " + d.Error : "")
                }.ToArray();
            }
        }

        internal override void UpdateView()
        {
            ShopData d = Ctx.Service.ShopOf(Source);
            lastQuery.Text = Time(Ctx.Service.LastShopQueryOf(Source)) + (d != null && d.Error != null ? ", Fehler: " + d.Error : "");
            today.Text = d != null ? Today(d) : "keine Daten";
        }
    }
}
