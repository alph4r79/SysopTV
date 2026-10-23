// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.

#include "view_common.h"
#include "screen.h"
#include "font_vga.h"
#include <TFT_eSPI.h>
#include <stdio.h>
#include <string.h>

static const int SCREEN = 240;
static const int MAX_CHARS = 30;
static uint16_t lineBuf[SCREEN];

TFT_eSPI& viewTft() {
  return screenTft();
}

void viewText(const char* s, int x, int y, uint8_t font, uint8_t size, uint16_t fg, uint16_t bg,
              uint8_t datum, int padding) {
  TFT_eSPI& tft = screenTft();
  tft.setTextSize(size);
  tft.setTextColor(fg, bg);
  tft.setTextDatum(datum);
  tft.setTextPadding(padding);
  tft.drawString(s, x, y, font);
  tft.setTextPadding(0);
  tft.setTextSize(1);
}

// ---------------------------------------------------------------------------
// VGA font
// ---------------------------------------------------------------------------

static uint32_t nextCodepoint(const char*& p) {
  uint8_t c = (uint8_t)*p++;
  if (c < 0x80) {
    return c;
  }
  if ((c & 0xE0) == 0xC0 && (*p & 0xC0) == 0x80) {
    return ((uint32_t)(c & 0x1F) << 6) | ((uint8_t)*p++ & 0x3F);
  }
  if ((c & 0xF0) == 0xE0 && (p[0] & 0xC0) == 0x80 && (p[1] & 0xC0) == 0x80) {
    uint32_t cp = ((uint32_t)(c & 0x0F) << 12) | ((uint32_t)((uint8_t)p[0] & 0x3F) << 6) | ((uint8_t)p[1] & 0x3F);
    p += 2;
    return cp;
  }
  return '?';
}

static uint8_t glyphIndex(uint32_t cp) {
  if (cp >= VGA_FIRST && cp < VGA_FIRST + VGA_ASCII_COUNT) {
    return (uint8_t)(cp - VGA_FIRST);
  }
  for (uint8_t i = 0; i < VGA_EXTRA_COUNT; i++) {
    if (VGA_EXTRA[i] == cp) {
      return VGA_ASCII_COUNT + i;
    }
  }
  return '?' - VGA_FIRST;
}

static int decode(const char* s, uint8_t* glyphs) {
  int n = 0;
  while (*s && n < MAX_CHARS) {
    glyphs[n++] = glyphIndex(nextCodepoint(s));
  }
  return n;
}

int vgaWidth(const char* s, uint8_t scale) {
  uint8_t glyphs[MAX_CHARS];
  return decode(s, glyphs) * 8 * scale;
}

// Left edge for a block of width w aligned at x.
static int alignedLeft(int x, int w, TextAlign align) {
  if (align == ALIGN_CENTER) {
    return x - w / 2;
  }
  if (align == ALIGN_RIGHT) {
    return x - w;
  }
  return x;
}

void vgaText(const char* s, int x, int y, TextAlign align, uint8_t scale, uint16_t fg, uint16_t bg,
             int clearWidth) {
  TFT_eSPI& tft = screenTft();
  uint8_t glyphs[MAX_CHARS];
  int n = decode(s, glyphs);
  int cw = 8 * scale;
  // Drop characters that would not fit on the screen.
  while (n > 0 && n * cw > SCREEN) {
    n--;
  }
  int w = n * cw;
  int h = 16 * scale;
  int left = alignedLeft(x, w, align);
  if (left < 0) {
    left = 0;
  }
  if (left + w > SCREEN) {
    left = SCREEN - w;
  }

  if (clearWidth > w) {
    int cl = alignedLeft(x, clearWidth, align);
    if (cl < left) {
      tft.fillRect(cl, y, left - cl, h, bg);
    }
    int cr = cl + clearWidth;
    if (cr > left + w) {
      tft.fillRect(left + w, y, cr - (left + w), h, bg);
    }
  }
  if (n == 0) {
    return;
  }

  tft.startWrite();
  tft.setAddrWindow(left, y, w, h);
  for (int row = 0; row < 16; row++) {
    int i = 0;
    for (int g = 0; g < n; g++) {
      uint8_t bits = pgm_read_byte(&VGA_GLYPHS[glyphs[g]][row]);
      for (int b = 0; b < 8; b++) {
        uint16_t color = (bits & (0x80 >> b)) ? fg : bg;
        for (int k = 0; k < scale; k++) {
          lineBuf[i++] = color;
        }
      }
    }
    for (int k = 0; k < scale; k++) {
      tft.pushColors(lineBuf, w, true);
    }
  }
  tft.endWrite();
}

void vgaTextTransparent(const char* s, int x, int y, TextAlign align, uint8_t scale, uint16_t fg) {
  TFT_eSPI& tft = screenTft();
  uint8_t glyphs[MAX_CHARS];
  int n = decode(s, glyphs);
  int cw = 8 * scale;
  int left = alignedLeft(x, n * cw, align);
  for (int g = 0; g < n; g++) {
    int gx = left + g * cw;
    for (int row = 0; row < 16; row++) {
      uint8_t bits = pgm_read_byte(&VGA_GLYPHS[glyphs[g]][row]);
      int b = 0;
      while (b < 8) {
        if (!(bits & (0x80 >> b))) {
          b++;
          continue;
        }
        int start = b;
        while (b < 8 && (bits & (0x80 >> b))) {
          b++;
        }
        tft.fillRect(gx + start * scale, y + row * scale, (b - start) * scale, scale, fg);
      }
    }
  }
}

// ---------------------------------------------------------------------------
// Shapes
// ---------------------------------------------------------------------------

// Patterns as in the VGA glyphs 0xB0 to 0xB2.
static bool shadeSet(uint8_t level, int px, int py) {
  bool light = (py & 1) ? ((px & 3) == 0) : ((px & 3) == 2);
  switch (level) {
    case 1: return light;
    case 2: return ((px + py) & 1) == 0;
    case 3: return !light;
    default: return true;
  }
}

void fillShade(int x, int y, int w, int h, uint8_t level, uint8_t cell, uint16_t fg, uint16_t bg) {
  if (w <= 0 || h <= 0) {
    return;
  }
  TFT_eSPI& tft = screenTft();
  if (level >= 4) {
    tft.fillRect(x, y, w, h, fg);
    return;
  }
  if (level == 0) {
    tft.fillRect(x, y, w, h, bg);
    return;
  }
  if (cell < 1) {
    cell = 1;
  }
  if (w > SCREEN) {
    w = SCREEN;
  }
  tft.startWrite();
  tft.setAddrWindow(x, y, w, h);
  for (int row = 0; row < h; row++) {
    int py = row / cell;
    for (int i = 0; i < w; i++) {
      lineBuf[i] = shadeSet(level, i / cell, py) ? fg : bg;
    }
    tft.pushColors(lineBuf, w, true);
  }
  tft.endWrite();
}

void drawDoubleFrame(int x, int y, int w, int h, uint16_t color) {
  TFT_eSPI& tft = screenTft();
  tft.drawRect(x, y, w, h, color);
  tft.drawRect(x + 3, y + 3, w - 6, h - 6, color);
}

void formatPercent(const UsageColumn& c, char* out, size_t size) {
  if (c.present) {
    snprintf(out, size, "%u%%", c.percent);
  } else {
    snprintf(out, size, "--");
  }
}

bool textChanged(char* cache, size_t size, const char* s) {
  if (strncmp(cache, s, size) == 0) {
    return false;
  }
  strncpy(cache, s, size - 1);
  cache[size - 1] = '\0';
  return true;
}

// ---------------------------------------------------------------------------
// Special screens
// ---------------------------------------------------------------------------

void specialInvalidate(SpecialCache& cache) {
  cache.drawn = false;
  cache.clock[0] = '\0';
  cache.lastData[0] = '\0';
}

static void specialText(const SpecialStyle& st, const char* s, int y, uint8_t scale, uint16_t color, int clear) {
  if (st.vga) {
    vgaText(s, 120, y, ALIGN_CENTER, scale, color, st.back, clear);
  } else {
    // GLCD is 8 pixels high, font 2 is 16, the y here is the top edge like for VGA.
    if (scale == 1) {
      viewText(s, 120, y + 8, 2, 1, color, st.back, MC_DATUM, clear);
    } else {
      viewText(s, 120, y + scale * 4, 1, scale, color, st.back, MC_DATUM, clear);
    }
  }
}

void drawSpecial(const UsageView& view, const SpecialStyle& st, SpecialCache& c, bool full) {
  TFT_eSPI& tft = screenTft();
  if (full || !c.drawn || c.screen != view.screen) {
    tft.fillScreen(st.back);
    specialInvalidate(c);
    c.screen = view.screen;
    c.drawn = true;
    if (view.screen == USAGE_SCREEN_LOGIN) {
      specialText(st, "ANMELDUNG", 60, st.vga ? 2 : 3, st.warn, 0);
      specialText(st, "ABGELAUFEN", 100, st.vga ? 2 : 3, st.warn, 0);
      specialText(st, "Claude Code starten", 150, 1, st.main, 0);
    } else if (view.screen == USAGE_SCREEN_NO_CARRIER) {
      if (st.smallCarrier) {
        specialText(st, "NO CARRIER", 210, 1, st.dim, 0);
      } else {
        specialText(st, "NO CARRIER", 140, st.vga ? 2 : 3, st.main, 0);
      }
    }
  }

  if (view.screen == USAGE_SCREEN_LOGIN) {
    if (textChanged(c.clock, sizeof(c.clock), view.clock)) {
      specialText(st, view.clock, 200, 1, st.dim, 80);
    }
    return;
  }
  // NO CARRIER: big clock on top.
  if (textChanged(c.clock, sizeof(c.clock), view.clock)) {
    if (st.vga) {
      // Scale 5: 40 x 80 pixels per character, "14:09" is 200 pixels wide.
      vgaText(view.clock, 120, 40, ALIGN_CENTER, 5, st.main, st.back, 220);
    } else {
      // GLCD scale 6: 36 x 48 pixels per character.
      viewText(view.clock, 120, 80, 1, 6, st.main, st.back, MC_DATUM, 220);
    }
  }
  if (textChanged(c.lastData, sizeof(c.lastData), view.lastData)) {
    specialText(st, view.lastData, st.smallCarrier ? 150 : 180, 1, st.dim, 220);
  }
}
