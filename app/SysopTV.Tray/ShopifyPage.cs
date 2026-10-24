// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Page "Shopify": shop address and the access of an app from the Shopify
// Dev Dashboard (client ID and secret) or an older Admin API token.

using System.Windows.Forms;

namespace SysopTV.Tray
{
    internal sealed class ShopifyPage : ShopPage
    {
        private const string GuideUrl = "https://shopify.dev/docs/apps/build/dev-dashboard/create-apps-using-dev-dashboard";

        private readonly TextBox domain = new TextBox { Width = 260 };
        private readonly TextBox token = new TextBox { Width = 260, UseSystemPasswordChar = true };
        private readonly TextBox clientId = new TextBox { Width = 260 };
        private readonly TextBox secret = new TextBox { Width = 260, UseSystemPasswordChar = true };

        internal ShopifyPage(PageContext ctx) : base(ctx)
        {
            ShopConfig s = ctx.Config.Shop;
            domain.Text = s.Domain;
            DisplayName.Text = s.DisplayName;
            token.Text = s.AccessToken;
            clientId.Text = s.ClientId;
            secret.Text = s.ClientSecret;
            Build("Shopify",
                "Das Programm braucht eine eigene App im Shopify Dev Dashboard mit den Scopes read_orders "
                + "und read_all_orders (für das Jahr), installiert in deinem Shop. Aus den App-Einstellungen (Credentials) kommen Client-ID und Client-Secret "
                + "hier hinein, das Programm holt sich damit selbst einen Zugang. App und Shop müssen zur selben "
                + "Organisation gehören. Ein Admin-API-Token einer älteren Custom App geht auch.",
                "Anleitung bei Shopify: App im Dev Dashboard anlegen", GuideUrl,
                g =>
                {
                    AddRow(g, "Shop-Adresse", Row(domain, Note("z. B. meinshop.myshopify.com")));
                    AddRow(g, "Admin-API-Zugriffstoken", token);
                    AddRow(g, "oder Client-ID", clientId);
                    AddRow(g, "und Client-Secret", secret);
                },
                "Abgefragt werden alle 5 Minuten Bestellnummer, Uhrzeit, Betrag und Versandstatus der Bestellungen, keine Namen, Adressen oder E-Mail-Adressen. Stornierte und Testbestellungen zählen nicht.");
        }

        internal override string Title
        {
            get { return "Shopify"; }
        }

        protected override string Source
        {
            get { return ShopSources.Shopify; }
        }

        protected override bool CommitFields()
        {
            ShopConfig old = Ctx.Config.Shop;
            ShopConfig s = old.Copy();
            s.Domain = ShopifyClient.NormalizeDomain(domain.Text);
            s.DisplayName = DisplayName.Text.Trim();
            if (token.Text.Trim() != s.AccessToken)
            {
                s.AccessToken = token.Text.Trim();
            }
            s.ClientId = clientId.Text.Trim();
            if (secret.Text.Trim() != s.ClientSecret)
            {
                s.ClientSecret = secret.Text.Trim();
            }
            if (s.Domain == old.Domain && s.DisplayName == old.DisplayName && s.AccessTokenProtected == old.AccessTokenProtected &&
                s.ClientId == old.ClientId && s.ClientSecretProtected == old.ClientSecretProtected)
            {
                return false;
            }
            Ctx.Config.Shop = s;
            return true;
        }

        protected override string MissingText
        {
            get { return Ctx.Config.Shop.Configured ? null : "Bitte Shop-Adresse und Token oder Client-ID mit Secret eintragen."; }
        }

        protected override ShopData FetchNow()
        {
            return new ShopifyClient().Fetch(Ctx.Config.Shop.Copy());
        }

        protected override string VersionText(ShopData d)
        {
            return "API-Version " + ShopifyClient.ApiVersion;
        }
    }
}
