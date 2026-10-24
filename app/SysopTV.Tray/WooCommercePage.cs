// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Page "WooCommerce": address of the website and the key of the WordPress
// plugin "SysopTV for WooCommerce". The key is stored only encrypted (DPAPI).

using System.Windows.Forms;

namespace SysopTV.Tray
{
    internal sealed class WooCommercePage : ShopPage
    {
        private const string GuideUrl = "https://github.com/alph4r79/SysopTV/blob/main/docs/woocommerce-setup.md";

        private readonly TextBox url = new TextBox { Width = 260 };
        private readonly TextBox key = new TextBox { Width = 260, UseSystemPasswordChar = true };

        internal WooCommercePage(PageContext ctx) : base(ctx)
        {
            WooConfig w = ctx.Config.Woo;
            url.Text = w.Url;
            key.Text = w.Key;
            DisplayName.Text = w.DisplayName;
            Build("WooCommerce",
                "Im Shop muss das WordPress-Plugin „SysopTV for WooCommerce“ installiert und aktiv sein. "
                + "Unter WooCommerce, SysopTV erzeugt es einen Schlüssel, der hier hinein kommt. Das Plugin gibt nur Summen "
                + "heraus, keine Kundendaten, und antwortet nur über HTTPS.",
                "Anleitung: Plugin installieren und Schlüssel erzeugen", GuideUrl,
                g =>
                {
                    AddRow(g, "Adresse der Website", Row(url, Note("z. B. https://meinshop.de")));
                    AddRow(g, "Schlüssel", key);
                },
                "Abgefragt werden alle 5 Minuten Summen und für heute Bestellnummer, Uhrzeit, Betrag und Versandstatus, keine Namen, Adressen oder E-Mail-Adressen. Es zählen Bestellungen in Bearbeitung, fertiggestellt und in Wartestellung.");
        }

        internal override string Title
        {
            get { return "WooCommerce"; }
        }

        protected override string Source
        {
            get { return ShopSources.WooCommerce; }
        }

        protected override bool CommitFields()
        {
            WooConfig old = Ctx.Config.Woo;
            WooConfig w = old.Copy();
            w.Url = WooClient.NormalizeUrl(url.Text);
            w.DisplayName = DisplayName.Text.Trim();
            if (key.Text.Trim() != w.Key)
            {
                w.Key = key.Text.Trim();
            }
            if (w.Url == old.Url && w.DisplayName == old.DisplayName && w.KeyProtected == old.KeyProtected)
            {
                return false;
            }
            Ctx.Config.Woo = w;
            if (url.Text != w.Url)
            {
                url.Text = w.Url;
            }
            return true;
        }

        protected override string MissingText
        {
            get { return Ctx.Config.Woo.Configured ? null : "Bitte Adresse der Website und Schlüssel eintragen."; }
        }

        protected override ShopData FetchNow()
        {
            return new WooClient().Fetch(Ctx.Config.Woo.Copy());
        }

        protected override string VersionText(ShopData d)
        {
            return "Plugin " + (string.IsNullOrEmpty(d.Plugin) ? "?" : d.Plugin);
        }
    }
}
