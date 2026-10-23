// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Design 6 "Klassik": the layout of the image the old PC script made for
// the original firmware. Session big in the middle with the reset time
// below, the week with a bar of 16 segments at the bottom. Colours are the
// amber tones of the design "Bernstein", the outline of empty segments in
// warning uses the dimmed red of the design "ANSI". The big digits use TFT_eSPI font 8
// (75 pixels high), the scan line darkening of the old image is left out,
// there is no frame buffer for it.

#include "views.h"
#include "view_common.h"
#include "config.h"
#include <TFT_eSPI.h>
#include <stdio.h>
#include <string.h>

static const uint16_t BACK = SYSOPTV_BLACK;
static const uint16_t MAIN = AMBER_MAIN;
static const uint16_t DIM = AMBER_DIM;
static const uint16_t LINE = AMBER_FRAME;
// Empty segments: black with a 1 pixel outline, dimmed red when warning.
static const uint16_t OUTLINE = AMBER_DIM;
static const uint16_t OUTLINE_WARN = ANSI_RED_DIM;
static const uint16_t WARN = AMBER_WARN;

static const int LEFT = 16;
static const int RIGHT = 224;
static const int BIG_Y = 26;
static const int BIG_H = 75;
static const int RESET_Y = 109;
static const int LINE_Y = 148;
static const int WEEK_Y = 158;
static const int WEEK_PCT_Y = 154;
static const int SEG_Y = 184;
static const int SEG_H = 16;
static const int SEG_COUNT = 16;
static const int SEG_GAP = 2;
static const int BOTTOM_Y = 206;
static const int BORDER = 6;
// Red frame around the screen when a value is this close (percent left).
static const uint8_t CRITICAL_LEFT = 5;

static bool drawn = false;
static UsageScreen drawnScreen = USAGE_SCREEN_LIMITS;
static bool drawnUsed = false;
static bool drawnBorder = false;
static SpecialCache special;

static bool bigValid = false;
static bool bigPresent = false;
static bool bigWarn = false;
static uint8_t bigPercent = 0;
static int bigLeft = 120;
static int bigRight = 120;
static char resetLine[16];
static char weekPct[8];
static bool weekWarn = false;
static int segLit = -1;
static bool segWarn = false;
static char bottomLine[24];

void klassikInvalidate() {
  drawn = false;
  specialInvalidate(special);
}

static bool critical(const UsageColumn& c) {
  return c.present && c.remaining <= CRITICAL_LEFT;
}

static void drawFrame(bool showUsed, bool border) {
  TFT_eSPI& tft = viewTft();
  tft.fillScreen(BACK);
  if (border) {
    tft.fillRect(0, 0, 240, BORDER, WARN);
    tft.fillRect(0, 240 - BORDER, 240, BORDER, WARN);
    tft.fillRect(0, 0, BORDER, 240, WARN);
    tft.fillRect(240 - BORDER, 0, BORDER, 240, WARN);
  }
  vgaText("SESSION", LEFT, 10, ALIGN_LEFT, 1, DIM, BACK);
  vgaText(showUsed ? "VERBRAUCHT" : "\xc3\x9c" "BRIG", RIGHT, 10, ALIGN_RIGHT, 1, DIM, BACK);
  tft.fillRect(LEFT, LINE_Y, RIGHT - LEFT, 2, LINE);
  vgaText("WOCHE", LEFT, WEEK_Y, ALIGN_LEFT, 1, DIM, BACK);
  bigValid = false;
  bigLeft = bigRight = 120;
  resetLine[0] = '\0';
  weekPct[0] = '\0';
  segLit = -1;
  bottomLine[0] = '\0';
}

// Clears what the old big value covered outside the new one.
static void clearOutside(int left, int right) {
  TFT_eSPI& tft = viewTft();
  if (bigLeft < left) {
    tft.fillRect(bigLeft, BIG_Y, left - bigLeft, BIG_H, BACK);
  }
  if (bigRight > right) {
    tft.fillRect(right, BIG_Y, bigRight - right, BIG_H, BACK);
  }
  bigLeft = left;
  bigRight = right;
}

static void drawBig(const UsageColumn& c) {
  TFT_eSPI& tft = viewTft();
  uint16_t color = c.present ? (c.warn ? WARN : MAIN) : DIM;
  if (!c.present) {
    // Font 8 only has digits, "--" comes in the VGA font (32 x 64 per character).
    int w = vgaWidth("--", 4);
    int left = 120 - w / 2;
    clearOutside(left, left + w);
    tft.fillRect(left, BIG_Y, w, 5, BACK);
    vgaText("--", 120, BIG_Y + 5, ALIGN_CENTER, 4, color, BACK);
    tft.fillRect(left, BIG_Y + 69, w, BIG_H - 69, BACK);
    return;
  }
  char num[4];
  snprintf(num, sizeof(num), "%u", c.percent);
  tft.setTextSize(1);
  tft.setTextPadding(0);
  int wn = tft.textWidth(num, 8);
  int wp = tft.textWidth("%", 4);
  const int gap = 4;
  int left = 120 - (wn + gap + wp) / 2;
  int right = left + wn + gap + wp;
  clearOutside(left, right);
  // Digits with background, so the old ones are overwritten without flicker.
  tft.setTextColor(color, BACK);
  tft.setTextDatum(TL_DATUM);
  tft.drawString(num, left, BIG_Y, 8);
  // "%" in font 4 at the bottom of the digits, the rest of that column black.
  int px = left + wn;
  int py = BIG_Y + BIG_H - 30;
  tft.fillRect(px, BIG_Y, gap + wp, py - BIG_Y, BACK);
  tft.fillRect(px, py, gap, 26, BACK);
  tft.drawString("%", px + gap, py, 4);
  tft.fillRect(px, py + 26, gap + wp, BIG_Y + BIG_H - py - 26, BACK);
}

static void drawSegments(uint8_t percent, bool present, bool warn) {
  TFT_eSPI& tft = viewTft();
  int lit = 0;
  if (present) {
    lit = (percent * SEG_COUNT + 50) / 100;
    if (percent > 0 && lit == 0) {
      lit = 1;
    }
  }
  if (lit == segLit && warn == segWarn) {
    return;
  }
  segLit = lit;
  segWarn = warn;
  int span = RIGHT - LEFT + SEG_GAP;  // 16 segments and 15 gaps fill exactly 208 pixels
  for (int i = 0; i < SEG_COUNT; i++) {
    int x0 = LEFT + i * span / SEG_COUNT;
    int x1 = LEFT + (i + 1) * span / SEG_COUNT - SEG_GAP;
    if (i < lit) {
      tft.fillRect(x0, SEG_Y, x1 - x0, SEG_H, warn ? WARN : MAIN);
    } else {
      tft.fillRect(x0 + 1, SEG_Y + 1, x1 - x0 - 2, SEG_H - 2, BACK);
      tft.drawRect(x0, SEG_Y, x1 - x0, SEG_H, warn ? OUTLINE_WARN : OUTLINE);
    }
  }
}

static void drawLimits(const UsageView& view) {
  const UsageColumn& s = view.session;
  const UsageColumn& w = view.week;

  if (!bigValid || bigPresent != s.present || bigWarn != s.warn || bigPercent != s.percent) {
    drawBig(s);
    bigValid = true;
    bigPresent = s.present;
    bigWarn = s.warn;
    bigPercent = s.percent;
  }

  // Session reset as clock time, not as countdown.
  char line[16];
  if (s.present && s.resetClock[0]) {
    snprintf(line, sizeof(line), "Reset %s", s.resetClock);
  } else {
    snprintf(line, sizeof(line), "nicht aktiv");
  }
  if (textChanged(resetLine, sizeof(resetLine), line)) {
    viewText(line, 120, RESET_Y, 4, 1, MAIN, BACK, TC_DATUM, RIGHT - LEFT);
  }

  char pct[8];
  formatPercent(w, pct, sizeof(pct));
  if (textChanged(weekPct, sizeof(weekPct), pct) || weekWarn != w.warn) {
    weekWarn = w.warn;
    viewText(pct, RIGHT, WEEK_PCT_Y, 4, 1, w.present ? (w.warn ? WARN : MAIN) : DIM, BACK, TR_DATUM, 100);
  }

  drawSegments(w.percent, w.present, w.warn);

  char bottom[24];
  if (!w.present) {
    snprintf(bottom, sizeof(bottom), "%s", w.bottom);
  } else if (strncmp(w.bottom, "Reset", 5) == 0) {
    snprintf(bottom, sizeof(bottom), "%s", w.bottom);
  } else {
    snprintf(bottom, sizeof(bottom), "Reset %s", w.bottom);
  }
  if (textChanged(bottomLine, sizeof(bottomLine), bottom)) {
    vgaText(bottom, LEFT, BOTTOM_Y, ALIGN_LEFT, 1, DIM, BACK, RIGHT - LEFT);
  }
}

void klassikShow(const UsageView& view) {
  if (view.screen != USAGE_SCREEN_LIMITS) {
    static const SpecialStyle style = {BACK, MAIN, DIM, WARN, true, false};
    drawSpecial(view, style, special, !drawn || drawnScreen != view.screen);
    drawn = true;
    drawnScreen = view.screen;
    return;
  }
  bool border = critical(view.session) || critical(view.week);
  if (!drawn || drawnScreen != view.screen || drawnUsed != view.showUsed || drawnBorder != border) {
    drawFrame(view.showUsed, border);
    drawn = true;
    drawnScreen = view.screen;
    drawnUsed = view.showUsed;
    drawnBorder = border;
    specialInvalidate(special);
  }
  drawLimits(view);
}
