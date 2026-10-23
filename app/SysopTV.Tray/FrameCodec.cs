// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Picture format "SYF1", see docs/frame-format.md and frame.cpp of the
// firmware: 12 byte header, then RGB565 pixels as RLE packets.

using System;
using System.Collections.Generic;
using System.Drawing;

namespace SysopTV.Tray
{
    internal static class FrameCodec
    {
        internal static byte[] Encode(Canvas canvas, Rectangle r)
        {
            var o = new List<byte>(r.Width * r.Height / 4 + 64);
            o.AddRange(new[] { (byte)'S', (byte)'Y', (byte)'F', (byte)'1' });
            Put16(o, r.X);
            Put16(o, r.Y);
            Put16(o, r.Width);
            Put16(o, r.Height);

            int count = r.Width * r.Height;
            Func<int, ushort> px = i => canvas.Pixels[(r.Y + i / r.Width) * Canvas.Size + r.X + i % r.Width];
            int pos = 0;
            var literal = new List<ushort>(128);
            while (pos < count)
            {
                ushort c = px(pos);
                int run = 1;
                while (pos + run < count && run < 128 && px(pos + run) == c)
                {
                    run++;
                }
                // Runs of 2 and more pay off (3 bytes instead of 4 or more).
                if (run >= 2)
                {
                    Flush(o, literal);
                    o.Add((byte)(0x80 | (run - 1)));
                    Put16(o, c);
                    pos += run;
                }
                else
                {
                    literal.Add(c);
                    if (literal.Count == 128)
                    {
                        Flush(o, literal);
                    }
                    pos++;
                }
            }
            Flush(o, literal);
            return o.ToArray();
        }

        private static void Flush(List<byte> o, List<ushort> literal)
        {
            if (literal.Count == 0)
            {
                return;
            }
            o.Add((byte)(literal.Count - 1));
            foreach (ushort c in literal)
            {
                Put16(o, c);
            }
            literal.Clear();
        }

        private static void Put16(List<byte> o, int v)
        {
            o.Add((byte)(v & 0xFF));
            o.Add((byte)((v >> 8) & 0xFF));
        }

        // Decoder with the same checks as the firmware, for tests. Draws into target.
        internal static string Decode(byte[] data, Canvas target)
        {
            if (data.Length < 12 || data[0] != 'S' || data[1] != 'Y' || data[2] != 'F' || data[3] != '1')
            {
                return "kein SYF1";
            }
            int x = data[4] | data[5] << 8, y = data[6] | data[7] << 8;
            int w = data[8] | data[9] << 8, h = data[10] | data[11] << 8;
            if (w == 0 || h == 0 || x >= Canvas.Size || y >= Canvas.Size || w > Canvas.Size - x || h > Canvas.Size - y)
            {
                return "Ausschnitt ausserhalb";
            }
            int total = w * h, done = 0, p = 12;
            while (done < total)
            {
                if (p >= data.Length)
                {
                    return "zu wenige Pixel";
                }
                int c = data[p++];
                int n = (c & 0x7F) + 1;
                if (done + n > total)
                {
                    return "zu viele Pixel";
                }
                if ((c & 0x80) != 0)
                {
                    if (p + 2 > data.Length)
                    {
                        return "zu wenige Pixel";
                    }
                    ushort col = (ushort)(data[p] | data[p + 1] << 8);
                    p += 2;
                    for (int i = 0; i < n; i++, done++)
                    {
                        target.Pixels[(y + done / w) * Canvas.Size + x + done % w] = col;
                    }
                }
                else
                {
                    if (p + 2 * n > data.Length)
                    {
                        return "zu wenige Pixel";
                    }
                    for (int i = 0; i < n; i++, done++)
                    {
                        target.Pixels[(y + done / w) * Canvas.Size + x + done % w] = (ushort)(data[p] | data[p + 1] << 8);
                        p += 2;
                    }
                }
            }
            return p == data.Length ? null : "Daten nach dem letzten Pixel";
        }

        // Smallest rectangle around all pixels that differ, or Empty if none do.
        internal static Rectangle ChangedArea(Canvas before, Canvas after)
        {
            int minX = Canvas.Size, minY = Canvas.Size, maxX = -1, maxY = -1;
            for (int y = 0; y < Canvas.Size; y++)
            {
                for (int x = 0; x < Canvas.Size; x++)
                {
                    int i = y * Canvas.Size + x;
                    if (before.Pixels[i] != after.Pixels[i])
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            return maxX < 0 ? Rectangle.Empty : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
    }
}
