// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// SysopTV.exe --preview <folder>: writes every picture design with sample
// data as PNG, 240x240 and three times larger, plus two overview sheets
// (uebersicht-claude.png, uebersicht-shop.png) with all pictures side by side.
//
// SysopTV.exe --docs <folder>: the screens for README and GitHub with fixed
// sample data (Tuesday 10:42), each 480x480, every pixel doubled without
// smoothing, no frame, no text in the picture and no metadata in the file.
// No network.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

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
                new[] { "month", "revenue", "monat" }
            };
            foreach (DesignInfo style in ShopBig.Styles)
            {
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

            Sheet(claude, Path.Combine(folder, "uebersicht-claude.png"));
            Sheet(shop, Path.Combine(folder, "uebersicht-shop.png"));
            return 0;
        }

        // Fixed moment for the documentation: Tuesday 20.10.2026, 10:42.
        private static readonly DateTime DocsNow = new DateTime(2026, 10, 20, 10, 42, 0);

        internal static int Docs(string folder)
        {
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
                var big = new ShopBigView { Data = ShopData.Sample(4, DocsNow), ShopName = "Mein Shop", Now = DocsNow, Period = "day", Content = "both" };
                DocsPng(ShopBig.Render(style.Id, big), folder, "shop-gross-" + style.Id + "-heute");
            }
            string[][] details = { new[] { "dos", "tabelle" }, new[] { "heute", "bestellliste" }, new[] { "bon", "kassenbon" }, new[] { "woche", "wochendiagramm" } };
            foreach (string[] d in details)
            {
                var v = new ShopView { Data = ShopData.Sample(4, DocsNow), ShopName = "Mein Shop", Now = DocsNow };
                DocsPng(ShopDesigns.Render(d[0], v), folder, "shop-" + d[1]);
            }
            return 0;
        }

        // 480x480 PNG, nearest neighbour, only the chunks a PNG needs (no gamma,
        // no time, no text), so the file carries no metadata.
        private static void DocsPng(Canvas c, string folder, string name)
        {
            byte[] png;
            using (Bitmap big = c.ToBitmap(2))
            using (var ms = new MemoryStream())
            {
                big.Save(ms, ImageFormat.Png);
                png = ms.ToArray();
            }
            using (var outFile = File.Create(Path.Combine(folder, name + ".png")))
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
