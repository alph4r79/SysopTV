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

        // Bytes one more request costs besides the picture data: HTTP header and
        // multipart frame, measured roughly with the requests of the program.
        internal const int RequestOverhead = 300;
        internal const int MaxAreas = 4;
        private const int Tile = 8;

        // Bytes for sending these parts, one request per part.
        internal static int Cost(Canvas canvas, Rectangle r)
        {
            return Encode(canvas, r).Length + RequestOverhead;
        }

        // Up to four separate parts that together cover every changed pixel.
        // Parts are joined when one request for both is smaller than two.
        // Empty list if nothing changed.
        internal static List<Rectangle> ChangedAreas(Canvas before, Canvas after)
        {
            var result = new List<Rectangle>();
            Rectangle all = ChangedArea(before, after);
            if (all.IsEmpty)
            {
                return result;
            }

            // Changed tiles of 8 x 8 pixels, grouped into touching groups.
            const int n = Canvas.Size / Tile;
            var dirty = new bool[n, n];
            for (int y = all.Top; y < all.Bottom; y++)
            {
                for (int x = all.Left; x < all.Right; x++)
                {
                    int i = y * Canvas.Size + x;
                    if (before.Pixels[i] != after.Pixels[i])
                    {
                        dirty[y / Tile, x / Tile] = true;
                    }
                }
            }
            var seen = new bool[n, n];
            var stack = new Stack<Point>();
            for (int ty = 0; ty < n; ty++)
            {
                for (int tx = 0; tx < n; tx++)
                {
                    if (!dirty[ty, tx] || seen[ty, tx])
                    {
                        continue;
                    }
                    Rectangle box = Rectangle.Empty;
                    stack.Push(new Point(tx, ty));
                    seen[ty, tx] = true;
                    while (stack.Count > 0)
                    {
                        Point t = stack.Pop();
                        Rectangle exact = Exact(before, after, new Rectangle(t.X * Tile, t.Y * Tile, Tile, Tile));
                        box = box.IsEmpty ? exact : Rectangle.Union(box, exact);
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = t.X + dx, ny = t.Y + dy;
                                if (nx >= 0 && ny >= 0 && nx < n && ny < n && dirty[ny, nx] && !seen[ny, nx])
                                {
                                    seen[ny, nx] = true;
                                    stack.Push(new Point(nx, ny));
                                }
                            }
                        }
                    }
                    result.Add(box);
                }
            }

            // Many groups: first join by the smallest growth in area, that is cheap to compute.
            while (result.Count > 12)
            {
                JoinBest(result, (a, b) => Area(Rectangle.Union(a, b)) - Area(a) - Area(b));
            }
            // Then by bytes, down to four parts.
            var costs = new Dictionary<Rectangle, int>();
            Func<Rectangle, int> cost = r =>
            {
                int c;
                if (!costs.TryGetValue(r, out c))
                {
                    c = Cost(after, r);
                    costs[r] = c;
                }
                return c;
            };
            while (result.Count > MaxAreas)
            {
                JoinBest(result, (a, b) => cost(Rectangle.Union(a, b)) - cost(a) - cost(b));
            }
            // And as long as joining saves bytes.
            while (result.Count > 1)
            {
                int bi, bj;
                int gain = Best(result, (a, b) => cost(Rectangle.Union(a, b)) - cost(a) - cost(b), out bi, out bj);
                if (gain >= 0)
                {
                    break;
                }
                Join(result, bi, bj);
            }
            return result;
        }

        private static int Area(Rectangle r)
        {
            return r.Width * r.Height;
        }

        private static int Best(List<Rectangle> list, Func<Rectangle, Rectangle, int> price, out int bi, out int bj)
        {
            int best = int.MaxValue;
            bi = 0;
            bj = 1;
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    int p = price(list[i], list[j]);
                    if (p < best)
                    {
                        best = p;
                        bi = i;
                        bj = j;
                    }
                }
            }
            return best;
        }

        private static void JoinBest(List<Rectangle> list, Func<Rectangle, Rectangle, int> price)
        {
            int bi, bj;
            Best(list, price, out bi, out bj);
            Join(list, bi, bj);
        }

        private static void Join(List<Rectangle> list, int i, int j)
        {
            list[i] = Rectangle.Union(list[i], list[j]);
            list.RemoveAt(j);
        }

        // Smallest rectangle around the changed pixels inside one tile.
        private static Rectangle Exact(Canvas before, Canvas after, Rectangle tile)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = tile.Top; y < tile.Bottom; y++)
            {
                for (int x = tile.Left; x < tile.Right; x++)
                {
                    int i = y * Canvas.Size + x;
                    if (before.Pixels[i] != after.Pixels[i])
                    {
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }
            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
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
