// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// SysopTV.exe --preview <folder>: writes the pictures "Claude als Bild" and
// "Shop als Bild" with sample data as PNG, 240x240 and three times larger.
// No network.

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
            long sessionReset = now.AddHours(2).AddMinutes(15).AddSeconds(30).ToUnixTimeSeconds();
            DateTime thursday = now.Date.AddDays(((int)DayOfWeek.Thursday - (int)now.DayOfWeek + 7) % 7 + 7).AddHours(22).AddMinutes(59).AddSeconds(30);
            var data = new UsageData
            {
                State = "ok",
                Session = new UsageLimit { Used = 27, ResetsAt = sessionReset },
                Week = new UsageLimit { Used = 85, ResetsAt = new DateTimeOffset(thursday).ToUnixTimeSeconds() }
            };

            ViewModel used = UsageView.Build(data, data, DateTime.Now, true, now);
            Write(DosRenderer.Render(used), folder, "claude-als-bild");
            ViewModel left = UsageView.Build(data, data, DateTime.Now, false, now);
            Write(DosRenderer.Render(left), folder, "claude-als-bild-uebrig");
            ViewModel noCarrier = UsageView.Build(new UsageData { State = "error", Reason = "Beispiel" }, data, DateTime.Now.AddMinutes(-12), true, now);
            noCarrier.Address = "192.168.178.50";
            Write(DosRenderer.Render(noCarrier), folder, "claude-als-bild-no-carrier");
            Write(ShopRenderer.Render(ShopData.Sample(), "Mein Retro-Shop", DateTime.Now), folder, "shop-als-bild");
            return 0;
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
