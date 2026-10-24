// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Page "Shopify": shop address, name for the display, the access, "Shop
// testen" and the last query. Saved as soon as a field is left.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SysopTV.Tray
{
    internal sealed class ShopifyPage : Page
    {
        private const string GuideUrl = "https://shopify.dev/docs/apps/build/dev-dashboard/create-apps-using-dev-dashboard";

        private readonly TextBox domain = new TextBox { Width = 260 };
        private readonly TextBox displayName = new TextBox { Width = 260 };
        private readonly TextBox token = new TextBox { Width = 260, UseSystemPasswordChar = true };
        private readonly TextBox clientId = new TextBox { Width = 260 };
        private readonly TextBox secret = new TextBox { Width = 260, UseSystemPasswordChar = true };
        private readonly Button test = new Button { Text = "Shop testen", AutoSize = true };
        private readonly Label testStatus = Note("");
        private readonly Label lastQuery = Value();
        private readonly Label today = Value();

        internal ShopifyPage(PageContext ctx) : base(ctx)
        {
            ShopConfig s = ctx.Config.Shop;
            domain.Text = s.Domain;
            displayName.Text = s.DisplayName;
            token.Text = s.AccessToken;
            clientId.Text = s.ClientId;
            secret.Text = s.ClientSecret;

            TableLayoutPanel g = Grid();
            AddRow(g, "Shop-Adresse", Row(domain, Note("z. B. meinshop.myshopify.com")));
            AddRow(g, "Name für die Anzeige", displayName);
            AddRow(g, "Admin-API-Zugriffstoken", token);
            AddRow(g, "oder Client-ID", clientId);
            AddRow(g, "und Client-Secret", secret);
            AddRow(g, null, Row(test, testStatus));
            AddRow(g, "Letzte Abfrage", lastQuery);
            AddRow(g, "Heute", today);
            var guide = new Label
            {
                Text = "Das Programm braucht eine eigene App im Shopify Dev Dashboard mit dem Scope read_orders, "
                     + "installiert in deinem Shop. Aus den App-Einstellungen (Credentials) kommen Client-ID und Client-Secret "
                     + "hier hinein, das Programm holt sich damit selbst einen Zugang. App und Shop müssen zur selben "
                     + "Organisation gehören. Ein Admin-API-Token einer älteren Custom App geht auch.",
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                Margin = new Padding(0, 0, 0, 2)
            };
            var link = new LinkLabel { Text = "Anleitung bei Shopify: App im Dev Dashboard anlegen", AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
            link.LinkClicked += (sender, e) =>
            {
                try
                {
                    Process.Start(GuideUrl);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(FindForm(), "Der Browser ließ sich nicht öffnen: " + ex.Message, "SysopTV");
                }
            };
            Controls.Add(Stack(Heading("Shopify"), guide, link, g,
                Note("Abgefragt werden alle 5 Minuten Bestellnummer, Uhrzeit, Betrag und Versandstatus der Bestellungen, keine Namen, Adressen oder E-Mail-Adressen. Stornierte und Testbestellungen zählen nicht.")));

            foreach (TextBox t in new[] { domain, displayName, token, clientId, secret })
            {
                t.Leave += (sender, e) => Commit();
            }
            test.Click += async (sender, e) => await Test();
        }

        internal override string Title
        {
            get { return "Shopify"; }
        }

        private ShopConfig FromFields()
        {
            ShopConfig s = Ctx.Config.Shop.Copy();
            s.Domain = ShopifyClient.NormalizeDomain(domain.Text);
            s.DisplayName = displayName.Text.Trim();
            if (token.Text.Trim() != s.AccessToken)
            {
                s.AccessToken = token.Text.Trim();
            }
            s.ClientId = clientId.Text.Trim();
            if (secret.Text.Trim() != s.ClientSecret)
            {
                s.ClientSecret = secret.Text.Trim();
            }
            return s;
        }

        private void Commit()
        {
            ShopConfig old = Ctx.Config.Shop;
            ShopConfig s = FromFields();
            if (s.Domain != old.Domain || s.DisplayName != old.DisplayName || s.AccessTokenProtected != old.AccessTokenProtected ||
                s.ClientId != old.ClientId || s.ClientSecretProtected != old.ClientSecretProtected)
            {
                Ctx.Config.Shop = s;
                Ctx.Save();
            }
        }

        private async Task Test()
        {
            Commit();
            ShopConfig cfg = Ctx.Config.Shop.Copy();
            if (!cfg.Configured)
            {
                testStatus.Text = "Bitte Shop-Adresse und Token oder Client-ID mit Secret eintragen.";
                return;
            }
            test.Enabled = false;
            testStatus.Text = "Frage Shopify ab ...";
            ShopData d = await Task.Run(() => new ShopifyClient().Fetch(cfg));
            if (IsDisposed)
            {
                return;
            }
            test.Enabled = true;
            testStatus.Text = TestText(d);
        }

        // Result of "Shop testen" in one line. Public for the offline tests.
        internal static string TestText(ShopData d)
        {
            if (d.Error != null)
            {
                return "Fehler: " + d.Error;
            }
            string name = string.IsNullOrEmpty(d.ShopName) ? "dem Shop" : d.ShopName;
            return "Verbunden mit " + name + ": heute " + ShopFormat.Orders(d.Today.Orders) + ", API-Version " + ShopifyClient.ApiVersion + ".";
        }

        private static string Today(ShopData d)
        {
            return ShopFormat.Orders(d.Today.Orders) + ", " + ShopFormat.Money(d.Today.Revenue) + ", offen " + d.Unshipped;
        }

        internal override string[] Summary
        {
            get
            {
                ShopData d = Ctx.Service.Shop;
                if (!Ctx.Config.Shop.Configured)
                {
                    return new[] { "Kein Shop eingetragen." };
                }
                if (d == null)
                {
                    return new[] { "Noch keine Werte." };
                }
                var lines = new List<string>
                {
                    "Heute: " + Today(d),
                    "Gestern: " + ShopFormat.Orders(d.Yesterday.Orders) + ", " + ShopFormat.Money(d.Yesterday.Revenue),
                    "Stand " + Time(d.FetchedAt) + (d.Error != null ? ", letzte Abfrage fehlgeschlagen: " + d.Error : "")
                };
                return lines.ToArray();
            }
        }

        internal override void UpdateView()
        {
            ShopData d = Ctx.Service.Shop;
            DateTime? last = Ctx.Service.LastShopQuery;
            lastQuery.Text = Time(last) + (d != null && d.Error != null ? ", Fehler: " + d.Error : "");
            today.Text = d != null ? Today(d) : "keine Daten";
        }
    }
}
