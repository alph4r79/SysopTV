// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.

#include "vga.h"
#include "screen.h"
#include "font_vga.h"
#include <TFT_eSPI.h>

static const int SCREEN = 240;
static const int MAX_CHARS = 30;
static uint16_t lineBuf[SCREEN];

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
