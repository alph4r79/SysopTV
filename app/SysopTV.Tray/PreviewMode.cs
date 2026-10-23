// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// SysopTV.exe --preview <folder>: writes every picture design with sample
// data as PNG, 240x240 and three times larger. No network.

using System;
using System.Drawing;
using System.Drawing.Imaging;
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

            foreach (DesignInfo d in ClaudeDesigns.All)
            {
                Write(ClaudeDesigns.Render(d.Id, UsageView.Build(data, false, null, true, now)), folder, "claude-" + d.Id);
            }
            Write(ClaudeDesigns.Render("dos", UsageView.Build(data, false, null, false, now)), folder, "claude-dos-uebrig");
            Write(ClaudeDesigns.Render("dos", UsageView.Build(SampleUsage(now, false), false, null, true, now)), folder, "claude-dos-nicht-aktiv");
            string stamp = UsageView.StampText(now.LocalDateTime.AddMinutes(-23));
            Write(ClaudeDesigns.Render("dos", UsageView.Build(data, false, stamp, true, now)), folder, "claude-dos-stand");
            Write(ClaudeDesigns.Render("dos", UsageView.Build(data, true, null, true, now)), folder, "claude-dos-anmeldung");

            DateTime local = DateTime.Now;
            foreach (DesignInfo d in ShopDesigns.All)
            {
                Write(ShopDesigns.Render(d.Id, Shop(ShopData.Sample(4), local)), folder, "shop-" + d.Id);
            }
            Write(ShopDesigns.Render("heute", Shop(ShopData.Sample(0), local)), folder, "shop-heute-leer");
            Write(ShopDesigns.Render("heute", Shop(ShopData.Sample(14), local)), folder, "shop-heute-voll");
            return 0;
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

        private static void Write(Canvas c, string folder, string name)
        {
            using (Bitmap small = c.ToBitmap(1))
            {
                small.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
            }
            using (Bitmap big = c.ToBitmap(3))
            {
                big.Save(Path.Combine(folder, name + "-3x.png"), ImageFormat.Png);
            }
        }
    }
}
