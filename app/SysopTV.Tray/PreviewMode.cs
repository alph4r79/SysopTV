// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// SysopTV.exe --preview <folder>: writes every picture design with sample
// data as PNG, 240x240 and three times larger, the notice screens shown
// without numbers, plus two overview sheets
// (uebersicht-claude.png, uebersicht-shop.png) with all pictures side by side.
//
// SysopTV.exe --docs <folder>: the pictures of the documentation without
// metadata in the files. <folder>\screens gets the screens with fixed sample
// data (Tuesday 10:42), each 480x480, every pixel doubled without smoothing.
// <folder>\app gets every page of the window with sample data, drawn without
// showing it. For that all files of the program go to a fresh temp folder,
// the real settings are never read or written. No network.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace SysopTV.Tray
{
    internal static class PreviewMode
    {
        internal static int Run(string folder)
        {
            Directory.CreateDirectory(folder);
            DateTimeOffset now = DateTimeOffset.Now;
            UsageData data = SampleUsage(now, true);
            var claude = new List<KeyValuePair<string, Canvas>>();
            var shop = new List<KeyValuePair<string, Canvas>>();

            foreach (DesignInfo d in ClaudeDesigns.All)
            {
                claude.Add(Write(ClaudeDesigns.Render(d.Id, UsageView.Build(data, false, null, true, now)), folder, "claude-" + d.Id));
            }
            UsageData warn = SampleUsage(now, true);
            warn.Session.Used = 88;
            warn.Week.Used = 91;
            foreach (string id in new[] { "led", "hell", "fallblatt", "gruen" })
            {
                // Blinking of 4-Ton-Grün shows its visible phase.
                ViewModel vw = UsageView.Build(warn, false, null, true, now);
                vw.BlinkOff = false;
                claude.Add(Write(ClaudeDesigns.Render(id, vw), folder, "claude-" + id + "-warnung"));
            }
            claude.Add(Write(ClaudeDesigns.Render("dos", UsageView.Build(data, false, null, false, now)), folder, "claude-dos-uebrig"));
            claude.Add(Write(ClaudeDesigns.Render("dos", UsageView.Build(SampleUsage(now, false), false, null, true, now)), folder, "claude-dos-nicht-aktiv"));
            string stamp = UsageView.StampText(now.LocalDateTime.AddMinutes(-23));
            claude.Add(Write(ClaudeDesigns.Render("dos", UsageView.Build(data, false, stamp, true, now)), folder, "claude-dos-stand"));
            claude.Add(Write(ClaudeDesigns.Render("dos", UsageView.Build(data, true, null, true, now)), folder, "claude-dos-anmeldung"));

            // Flip animation: session from 27 to 28 percent, the first two of three steps.
            Canvas before = ClaudeDesigns.Render("fallblatt", UsageView.Build(data, false, null, true, now));
            UsageData next = SampleUsage(now, true);
            next.Session.Used = 28;
            Canvas after = ClaudeDesigns.Render("fallblatt", UsageView.Build(next, false, null, true, now));
            List<Canvas> flip = FlipAnimation.Frames(before, after);
            claude.Add(Write(flip[0], folder, "claude-fallblatt-klappen-1"));
            claude.Add(Write(flip[1], folder, "claude-fallblatt-klappen-2"));
            claude.Add(Write(Notice.Render("dos", "CLAUDE", "Claude-Daten werden geladen", null, now.LocalDateTime), folder, "claude-dos-hinweis-laden"));
            claude.Add(Write(Notice.Render("bernstein", "CLAUDE", "Keine Daten von Anthropic", "nächster Versuch " + now.LocalDateTime.AddMinutes(4).ToString("HH:mm"), now.LocalDateTime), folder, "claude-bernstein-hinweis-fehler"));
            claude.Add(Write(Notice.Render("fallblatt", "CLAUDE", "Claude-Daten werden geladen", null, now.LocalDateTime), folder, "claude-fallblatt-hinweis-laden"));

            DateTime local = DateTime.Now;
            foreach (DesignInfo d in ShopDesigns.All)
            {
                shop.Add(Write(ShopDesigns.Render(d.Id, Shop(ShopData.Sample(4), local)), folder, "shop-" + d.Id));
            }
            shop.Add(Write(ShopDesigns.Render("heute", Shop(ShopData.Sample(0), local)), folder, "shop-heute-leer"));
            shop.Add(Write(ShopDesigns.Render("heute", Shop(ShopData.Sample(14), local)), folder, "shop-heute-voll"));
            string[][] variants =
            {
                new[] { "day", "both", "heute" },
                new[] { "week", "orders", "woche" },
                new[] { "month", "revenue", "monat" },
                new[] { "year", "both", "jahr" }
            };
            foreach (DesignInfo style in ShopBig.Styles)
            {
                if (ShopBig.ShowsAllPeriods(style.Id))
                {
                    // These show several periods at once: the sample and large numbers.
                    var all = new ShopBigView { Data = ShopData.Sample(4), ShopName = "Mein Retro-Shop", Now = local };
                    shop.Add(Write(ShopBig.Render(style.Id, all), folder, "shop-gross-" + style.Id));
                    var large = new ShopBigView { Data = LargeNumbers(local), ShopName = "Mein Retro-Shop", Now = local };
                    shop.Add(Write(ShopBig.Render(style.Id, large), folder, "shop-gross-" + style.Id + "-grosse-zahlen"));
                    continue;
                }
                foreach (string[] v in variants)
                {
                    var big = new ShopBigView
                    {
                        Data = ShopData.Sample(4),
                        ShopName = "Mein Retro-Shop",
                        Now = local,
                        Period = v[0],
                        Content = v[1]
                    };
                    shop.Add(Write(ShopBig.Render(style.Id, big), folder, "shop-gross-" + style.Id + "-" + v[2]));
                }
            }

            ShopData six = ShopData.Sample(4);
            six.Year = new ShopPeriod { Orders = 4321, Revenue = 123456.78m };
            foreach (string id in new[] { "led", "fallblatt" })
            {
                var big = new ShopBigView { Data = six, ShopName = "Mein Retro-Shop", Now = local, Period = "year", Content = "revenue" };
                shop.Add(Write(ShopBig.Render(id, big), folder, "shop-gross-" + id + "-jahr-sechsstellig"));
            }
            string retry = "nächster Versuch " + local.AddMinutes(5).ToString("HH:mm");
            string[][] notices =
            {
                new[] { "bon", "Shopdaten werden geladen", null },
                new[] { "dos", "Zu viele Anfragen", retry },
                new[] { "bernstein", "Shop nicht erreichbar", retry },
                new[] { "led", "Schlüssel ungültig", "im Programm prüfen" },
                new[] { "hell", "Zugang prüfen", "im Programm prüfen" },
                new[] { "fallblatt", "Shop nicht eingetragen", "im Programm einrichten" },
                new[] { "gruen", "Shop meldet einen Fehler", retry }
            };
            foreach (string[] n in notices)
            {
                shop.Add(Write(Notice.Render(n[0], "SHOP", n[1], n[2], local), folder, "shop-gross-" + n[0] + "-hinweis"));
            }
            ShopData noScope = ShopData.Sample(4);
            noScope.YearNote = ShopifyClient.MissingAllOrders;
            shop.Add(Write(ShopBig.Render("bon", new ShopBigView { Data = noScope, ShopName = "Mein Retro-Shop", Now = local, Period = "year", Content = "both" }), folder, "shop-gross-bon-jahr-ohne-zugriff"));
            ShopData oldPlugin = ShopData.Sample(4);
            oldPlugin.YearNote = WooClient.UpdatePlugin;
            shop.Add(Write(ShopBig.Render("dos", new ShopBigView { Data = oldPlugin, ShopName = "Mein Retro-Shop", Now = local, Period = "year", Content = "both", Source = ShopSources.WooCommerce }), folder, "shop-gross-dos-jahr-altes-plugin"));

            Sheet(claude, Path.Combine(folder, "uebersicht-claude.png"));
            Sheet(shop, Path.Combine(folder, "uebersicht-shop.png"));
            return 0;
        }

        // Fixed moment for the documentation: Tuesday 20.10.2026, 10:42.
        private static readonly DateTime DocsNow = new DateTime(2026, 10, 20, 10, 42, 0);

        internal static int Docs(string imagesFolder)
        {
            string folder = Path.Combine(imagesFolder, "screens");
            Directory.CreateDirectory(folder);
            var now = new DateTimeOffset(DocsNow);
            // Session 27 % with reset at 12:59 (in 2:17), week 14 % with reset Thursday 23:00.
            var data = new UsageData
            {
                State = "ok",
                Session = new UsageLimit { Used = 27, ResetsAt = now.AddHours(2).AddMinutes(17).ToUnixTimeSeconds() },
                Week = new UsageLimit { Used = 14, ResetsAt = new DateTimeOffset(DocsNow.Date.AddDays(2).AddHours(23)).ToUnixTimeSeconds() }
            };
            foreach (DesignInfo d in ClaudeDesigns.All)
            {
                DocsPng(ClaudeDesigns.Render(d.Id, UsageView.Build(data, false, null, true, now)), folder, "claude-" + d.Id);
            }
            DocsPng(ClaudeDesigns.Render("dos", UsageView.Build(data, false, null, false, now)), folder, "claude-dos-uebrig");
            var warn = new UsageData
            {
                State = "ok",
                Session = new UsageLimit { Used = 86, ResetsAt = data.Session.ResetsAt },
                Week = new UsageLimit { Used = 41, ResetsAt = data.Week.ResetsAt }
            };
            DocsPng(ClaudeDesigns.Render("fallblatt", UsageView.Build(warn, false, null, true, now)), folder, "claude-fallblatt-warnung");

            foreach (DesignInfo style in ShopBig.Styles)
            {
                var big = new ShopBigView { Data = ShopData.Sample(4, DocsNow), ShopName = "My Shop", Now = DocsNow, Period = "day", Content = "both" };
                DocsPng(ShopBig.Render(style.Id, big), folder, "shop-gross-" + style.Id + (ShopBig.ShowsAllPeriods(style.Id) ? "" : "-heute"));
            }
            var year = new ShopBigView { Data = ShopData.Sample(4, DocsNow), ShopName = "My Shop", Now = DocsNow, Period = "year", Content = "both" };
            DocsPng(ShopBig.Render("fallblatt", year), folder, "shop-gross-fallblatt-jahr");
            string[][] details = { new[] { "dos", "tabelle" }, new[] { "heute", "bestellliste" }, new[] { "bon", "kassenbon" }, new[] { "woche", "wochendiagramm" } };
            foreach (string[] d in details)
            {
                var v = new ShopView { Data = ShopData.Sample(4, DocsNow), ShopName = "My Shop", Now = DocsNow };
                DocsPng(ShopDesigns.Render(d[0], v), folder, "shop-" + d[1]);
            }
            return DocsApp(Path.Combine(imagesFolder, "app"));
        }

        // The pages of the window, file name and the cube chosen on "Würfel" (-1 for none).
        private static readonly string[][] AppPages =
        {
            new[] { "Übersicht", "-1", "app-overview" },
            new[] { "Würfel", "0", "app-cubes-claude" },
            new[] { "Würfel", "1", "app-cubes-shopify" },
            new[] { "Würfel", "2", "app-cubes-woocommerce" },
            new[] { "Claude", "-1", "app-claude" },
            new[] { "Shopify", "-1", "app-shopify" },
            new[] { "WooCommerce", "-1", "app-woocommerce" },
            new[] { "Programm", "-1", "app-program" }
        };

        private static int DocsApp(string folder)
        {
            Directory.CreateDirectory(folder);
            string data = Path.Combine(Path.GetTempPath(), "sysoptv-docs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(data);
            string previousDir = AppPaths.DataDirOption;
            bool previousQuiet = Log.Quiet;
            Func<string, TimeSpan, CubeInfo> readInfo = CubesPage.ReadInfo;
            Func<string, string, CubeSettings> readSettings = CubesPage.ReadSettings;
            Func<DateTime?> login = ClaudePage.LoginSource;
            AppPaths.DataDirOption = data;
            Log.Quiet = true;
            try
            {
                DateTime now = DateTime.Now;
                var cfg = new AppConfig();
                cfg.Cubes.Add(new CubeEntry { Address = "192.168.178.50", Function = CubeFunction.Claude, Design = "dos", AdminPassword = "example-password" });
                cfg.Cubes.Add(new CubeEntry { Address = "192.168.178.51", Function = CubeFunction.Shopify, ShopView = ShopBig.ViewBig, BigStyle = "bon", AdminPassword = "example-password" });
                cfg.Cubes.Add(new CubeEntry { Address = "192.168.178.52", Function = CubeFunction.WooCommerce, ShopView = ShopBig.ViewDetails, Design = "dos", AdminPassword = "example-password" });
                cfg.Shop.Domain = "myshop.myshopify.com";
                cfg.Shop.DisplayName = "My Shop";
                cfg.Shop.ClientId = "your-client-id";
                cfg.Shop.ClientSecret = "your-client-secret";
                cfg.Woo.Url = "https://shop.example.com";
                cfg.Woo.DisplayName = "My Shop";
                cfg.Woo.Key = "your-plugin-key";
                CubesPage.ReadInfo = (address, timeout) => SampleInfo(cfg, address);
                CubesPage.ReadSettings = (address, password) => SampleSettings(cfg, address);
                ClaudePage.LoginSource = () => now.AddHours(5).AddMinutes(12);

                var service = new SenderService(cfg, Path.Combine(data, "state.json"));
                ShopData shopify = ShopData.Sample(4, now);
                shopify.ShopName = "My Shop";
                ShopData woo = ShopData.Sample(6, now);
                woo.ShopName = "My Shop";
                woo.Currency = "EUR";
                woo.Plugin = "0.2.0";
                var docsNow = new DateTimeOffset(now);
                var usage = new UsageData
                {
                    State = "ok",
                    Session = new UsageLimit { Used = 27, ResetsAt = docsNow.AddHours(2).AddMinutes(17).ToUnixTimeSeconds() },
                    Week = new UsageLimit { Used = 14, ResetsAt = docsNow.AddDays(2).AddHours(12).ToUnixTimeSeconds() }
                };
                service.ShowSamples(usage, now.AddSeconds(-40), new Dictionary<string, ShopData>
                {
                    { ShopSources.Shopify, shopify },
                    { ShopSources.WooCommerce, woo }
                });

                // A real message loop like in the running program: a timer opens
                // one page after the other and takes the picture 3 seconds later,
                // when the page has loaded what it shows.
                using (var form = new MainForm(cfg, service, null))
                using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-4000, -4000);
                    form.ShowInTaskbar = false;
                    int index = -1;
                    bool capture = false;
                    DateTime next = DateTime.Now.AddSeconds(1);
                    timer.Tick += (sender, e) =>
                    {
                        if (DateTime.Now < next)
                        {
                            return;
                        }
                        if (capture)
                        {
                            Mask(form, data);
                            using (var bmp = new Bitmap(form.Width, form.Height))
                            {
                                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                                WritePng(bmp, Path.Combine(folder, AppPages[index][2] + ".png"));
                            }
                            capture = false;
                            next = DateTime.Now.AddMilliseconds(200);
                            return;
                        }
                        index++;
                        if (index >= AppPages.Length)
                        {
                            timer.Stop();
                            form.Close();
                            return;
                        }
                        form.ShowForDocs(AppPages[index][0], int.Parse(AppPages[index][1]));
                        capture = true;
                        next = DateTime.Now.AddSeconds(3);
                    };
                    timer.Start();
                    Application.Run(form);
                }
                service.Dispose();
                return 0;
            }
            finally
            {
                CubesPage.ReadInfo = readInfo;
                CubesPage.ReadSettings = readSettings;
                ClaudePage.LoginSource = login;
                AppPaths.DataDirOption = previousDir;
                Log.Quiet = previousQuiet;
                try
                {
                    Directory.Delete(data, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        // The temp folder must not appear in a picture: the normal folder instead.
        private static void Mask(Control c, string data)
        {
            if (c.Text.IndexOf(data, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                c.Text = c.Text.Replace(data, "%LOCALAPPDATA%\\SysopTV");
            }
            foreach (Control child in c.Controls)
            {
                Mask(child, data);
            }
        }

        // Device names as the firmware makes them, shown in the settings of the
        // cube. /api/info reports none, so the list keeps the short address.
        private static string SampleName(AppConfig cfg, string address)
        {
            string[] names = { "sysoptv-3fa2", "sysoptv-7c1e", "sysoptv-b804" };
            int i = cfg.Cubes.FindIndex(x => x.Address == address);
            return names[Math.Max(0, i) % names.Length];
        }

        private static CubeInfo SampleInfo(AppConfig cfg, string address)
        {
            var info = new CubeInfo
            {
                Address = address,
                Ip = address,
                Hostname = SampleName(cfg, address) + ".local",
                DeviceName = "",
                Version = "0.4.3",
                UptimeText = "2 Tage 3 h 41 min",
                FrameValid = true
            };
            info.Capabilities.AddRange(new[] { "usage", "settings", "frame" });
            return info;
        }

        private static CubeSettings SampleSettings(AppConfig cfg, string address)
        {
            var s = new CubeSettings
            {
                Design = 2,
                CycleSeconds = 0,
                ShowUsed = true,
                Brightness = 80,
                NightEnabled = true,
                NightFrom = "22:00",
                NightTo = "07:00",
                NightBrightness = 10,
                DeviceName = SampleName(cfg, address)
            };
            for (int i = 0; i < ClaudeDesigns.FirmwareCount; i++)
            {
                s.DesignNames.Add(ClaudeDesigns.All[i].Label);
            }
            return s;
        }

        // 480x480 PNG, nearest neighbour, only the chunks a PNG needs (no gamma,
        // no time, no text), so the file carries no metadata.
        private static void DocsPng(Canvas c, string folder, string name)
        {
            using (Bitmap big = c.ToBitmap(2))
            {
                WritePng(big, Path.Combine(folder, name + ".png"));
            }
        }

        private static void WritePng(Bitmap bmp, string file)
        {
            byte[] png;
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                png = ms.ToArray();
            }
            using (var outFile = File.Create(file))
            {
                outFile.Write(png, 0, 8);
                int p = 8;
                while (p + 8 <= png.Length)
                {
                    int length = (png[p] << 24) | (png[p + 1] << 16) | (png[p + 2] << 8) | png[p + 3];
                    string type = System.Text.Encoding.ASCII.GetString(png, p + 4, 4);
                    int size = 12 + length;
                    if (type == "IHDR" || type == "PLTE" || type == "IDAT" || type == "IEND")
                    {
                        outFile.Write(png, p, size);
                    }
                    p += size;
                }
            }
        }

        // Four digit counts and amounts above 10.000 euros, to check that nothing is cut.
        internal static ShopData LargeNumbers(DateTime now)
        {
            ShopData d = ShopData.Sample(4, now);
            d.Today = new ShopPeriod { Orders = 1234, Revenue = 12345.67m };
            d.CalendarWeek = new ShopPeriod { Orders = 4321, Revenue = 98765.43m };
            d.Month = new ShopPeriod { Orders = 9876, Revenue = 456789.01m };
            return d;
        }

        // Session 27 % with reset in 2:16, week 85 % with reset next Thursday 22:59:30.
        // Without session reset the session is "nicht aktiv".
        internal static UsageData SampleUsage(DateTimeOffset now, bool sessionActive)
        {
            DateTime thursday = now.Date.AddDays(((int)DayOfWeek.Thursday - (int)now.DayOfWeek + 7) % 7 + 7).AddHours(22).AddMinutes(59).AddSeconds(30);
            return new UsageData
            {
                State = "ok",
                Session = new UsageLimit { Used = sessionActive ? 27 : 0, ResetsAt = sessionActive ? now.AddHours(2).AddMinutes(15).AddSeconds(30).ToUnixTimeSeconds() : (long?)null },
                Week = new UsageLimit { Used = 85, ResetsAt = new DateTimeOffset(thursday).ToUnixTimeSeconds() }
            };
        }

        private static ShopView Shop(ShopData d, DateTime now)
        {
            return new ShopView { Data = d, ShopName = "Mein Retro-Shop", Now = now };
        }

        private static KeyValuePair<string, Canvas> Write(Canvas c, string folder, string name)
        {
            using (Bitmap small = c.ToBitmap(1))
            {
                small.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
            }
            using (Bitmap big = c.ToBitmap(3))
            {
                big.Save(Path.Combine(folder, name + "-3x.png"), ImageFormat.Png);
            }
            return new KeyValuePair<string, Canvas>(name, c);
        }

        // All pictures in a grid, 240 px each with the name below.
        private static void Sheet(List<KeyValuePair<string, Canvas>> items, string file)
        {
            const int cols = 6, pad = 12, label = 22, tile = Canvas.Size;
            int rows = (items.Count + cols - 1) / cols;
            int width = pad + cols * (tile + pad);
            int height = pad + rows * (tile + label + pad);
            using (var sheet = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            using (Graphics g = Graphics.FromImage(sheet))
            using (var font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point))
            using (var brush = new SolidBrush(Color.FromArgb(225, 225, 225)))
            {
                g.Clear(Color.FromArgb(40, 40, 40));
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                var center = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
                for (int i = 0; i < items.Count; i++)
                {
                    int x = pad + (i % cols) * (tile + pad);
                    int y = pad + (i / cols) * (tile + label + pad);
                    using (Bitmap b = items[i].Value.ToBitmap(1))
                    {
                        g.DrawImageUnscaled(b, x, y);
                    }
                    g.DrawString(items[i].Key, font, brush, new RectangleF(x, y + tile + 3, tile, label), center);
                }
                sheet.Save(file, ImageFormat.Png);
            }
        }
    }
}
