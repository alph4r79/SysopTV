// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Flip animation of the style Fallblatt: between the old and the new picture
// three frames, only for the cards whose character changed. Like a real split
// flap the upper half of the old card falls down over the new one:
//   1. new upper half, old flap two thirds high above the split
//   2. old flap one third high
//   3. new lower half falling, one third high below the split
// Everything outside the changing cards is the new picture already.

using System;
using System.Collections.Generic;
using System.Drawing;

namespace SysopTV.Tray
{
    internal static class FlipAnimation
    {
        internal const int StepMilliseconds = 100;

        // Cards that exist in both pictures at the same place and show something else.
        internal static List<Rectangle> ChangedCards(Canvas before, Canvas after)
        {
            var list = new List<Rectangle>();
            if (before == null)
            {
                return list;
            }
            foreach (Rectangle r in after.Cards)
            {
                if (!before.Cards.Contains(r))
                {
                    continue;
                }
                bool differs = false;
                for (int y = r.Top; y < r.Bottom && !differs; y++)
                {
                    for (int x = r.Left; x < r.Right; x++)
                    {
                        int i = y * Canvas.Size + x;
                        if (before.Pixels[i] != after.Pixels[i])
                        {
                            differs = true;
                            break;
                        }
                    }
                }
                if (differs)
                {
                    list.Add(r);
                }
            }
            return list;
        }

        // The frames between before and after, empty if no card changes.
        internal static List<Canvas> Frames(Canvas before, Canvas after)
        {
            var frames = new List<Canvas>();
            List<Rectangle> cards = ChangedCards(before, after);
            if (cards.Count == 0)
            {
                return frames;
            }
            for (int step = 1; step <= 3; step++)
            {
                Canvas f = after.Clone();
                foreach (Rectangle r in cards)
                {
                    int mid = r.Y + r.Height / 2;
                    int half = mid - r.Y;
                    int lower = r.Bottom - mid;
                    // Start from the old lower half under the new upper half.
                    Copy(before, f, r.X, r.Width, mid, lower, mid, lower);
                    if (step < 3)
                    {
                        int h = step == 1 ? half * 2 / 3 : half / 3;
                        Copy(before, f, r.X, r.Width, r.Y, half, mid - h, h);
                    }
                    else
                    {
                        int h = lower / 3;
                        Copy(after, f, r.X, r.Width, mid, lower, mid, h);
                    }
                }
                frames.Add(f);
            }
            return frames;
        }

        // Copies rows srcY..srcY+srcH of src into dst rows dstY..dstY+dstH, squeezed or stretched.
        private static void Copy(Canvas src, Canvas dst, int x, int w, int srcY, int srcH, int dstY, int dstH)
        {
            for (int row = 0; row < dstH; row++)
            {
                int sy = srcY + row * srcH / Math.Max(1, dstH);
                int dy = dstY + row;
                if (sy < 0 || sy >= Canvas.Size || dy < 0 || dy >= Canvas.Size)
                {
                    continue;
                }
                Array.Copy(src.Pixels, sy * Canvas.Size + x, dst.Pixels, dy * Canvas.Size + x, w);
            }
        }
    }
}
