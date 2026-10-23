// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Drawing helpers shared by the designs: VGA text, shading patterns, frames,
// change detection and the special screens (login expired, NO CARRIER).

#pragma once
#include <stdint.h>
#include <stddef.h>
#include "usage_data.h"

class TFT_eSPI;
TFT_eSPI& viewTft();

enum TextAlign : uint8_t { ALIGN_LEFT, ALIGN_CENTER, ALIGN_RIGHT };

// Text in a built-in TFT_eSPI font. padding is the width that gets cleared
// with bg around the text, so a shorter new text removes the old one.
void viewText(const char* s, int x, int y, uint8_t font, uint8_t size, uint16_t fg, uint16_t bg,
              uint8_t datum, int padding);

// Text in the embedded VGA font (8x16, UTF-8, with umlauts), scaled by an
// integer factor. y is the top edge. clearWidth > 0 clears that width
// (aligned like the text) with bg first, without flicker.
int vgaWidth(const char* s, uint8_t scale);
void vgaText(const char* s, int x, int y, TextAlign align, uint8_t scale, uint16_t fg, uint16_t bg,
             int clearWidth = 0);
// Same, but only sets the pixels of the glyphs and leaves the rest untouched.
void vgaTextTransparent(const char* s, int x, int y, TextAlign align, uint8_t scale, uint16_t fg);

// Shading like the CP437 characters: level 1 light (25 %), 2 medium (50 %),
// 3 dark (75 %), 4 full. cell is the size of one pattern pixel.
void fillShade(int x, int y, int w, int h, uint8_t level, uint8_t cell, uint16_t fg, uint16_t bg);

// Two nested rectangles, 1 pixel lines with 1 pixel gap.
void drawDoubleFrame(int x, int y, int w, int h, uint16_t color);

// "77%" or "--" for a column without data.
void formatPercent(const UsageColumn& c, char* out, size_t size);

// Copies s into cache and returns true if it was different.
bool textChanged(char* cache, size_t size, const char* s);

// Special screens, drawn in the colours of the design.
struct SpecialStyle {
  uint16_t back;
  uint16_t main;
  uint16_t dim;
  uint16_t warn;
  bool vga;           // VGA font, otherwise the built-in GLCD font
  bool smallCarrier;  // ANSI style: big clock, small "NO CARRIER" at the bottom
};

struct SpecialCache {
  UsageScreen screen;
  bool drawn;
  char clock[6];
  char lastData[24];
  char ip[16];
};

void specialInvalidate(SpecialCache& cache);
// Draws USAGE_SCREEN_LOGIN or USAGE_SCREEN_NO_CARRIER. The caller clears the
// screen once when switching to it (full == true).
void drawSpecial(const UsageView& view, const SpecialStyle& style, SpecialCache& cache, bool full);
