// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Design 1 "Saeulen": the first SysopTV screen. Green on black, two upright
// tanks of 10 segments, empty segments only as outline. Big texts use the
// built-in 6x8 GLCD font scaled up, the header the VGA font, which has the
// umlaut of the mode word.

#include "views.h"
#include "view_common.h"
#include "config.h"
#include <TFT_eSPI.h>

static const uint16_t BACK = SYSOPTV_BLACK;

static const int COL_X[2] = {60, 180};
// Header in the VGA font: CLAUDE at x 8, mode word at x 68, clock right at 232.
static const int HEADER_Y = 4;
static const int MODE_X = 68;
static const int PERCENT_Y = 50;
static const int TANK_TOP = 76;
static const int TANK_WIDTH = 60;
static const int SEG_HEIGHT = 9;
static const int SEG_PITCH = 11;
static const int LABEL_Y = 198;
static const int BOTTOM_Y = 222;

struct Column {
  bool valid;
  bool present;
  bool warn;
  char percent[6];
  uint8_t segments;
  char bottom[16];
};

static bool drawn = false;
static UsageScreen drawnScreen = USAGE_SCREEN_LIMITS;
static bool drawnUsed = false;
static char drawnClock[6];
static Column cols[2];
static SpecialCache special;

void saeulenInvalidate() {
  drawn = false;
  specialInvalidate(special);
}

static void drawFrame(bool showUsed) {
  TFT_eSPI& tft = viewTft();
  tft.fillScreen(BACK);
  vgaText("CLAUDE", 8, HEADER_Y, ALIGN_LEFT, 1, SAEULEN_MAIN, BACK);
  vgaText(showUsed ? "VERBRAUCHT" : "\xc3\x9c" "BRIG", MODE_X, HEADER_Y, ALIGN_LEFT, 1, SAEULEN_DIM, BACK);
  tft.drawFastHLine(8, 24, 224, SAEULEN_LINE);
  drawnClock[0] = '\0';
  cols[0].valid = false;
  cols[1].valid = false;
}

static void drawTank(int cx, uint8_t segments, uint16_t color) {
  TFT_eSPI& tft = viewTft();
  int x = cx - TANK_WIDTH / 2;
  for (int i = 0; i < USAGE_SEGMENTS; i++) {
    int y = TANK_TOP + (USAGE_SEGMENTS - 1 - i) * SEG_PITCH;
    if (i < segments) {
      tft.fillRect(x, y, TANK_WIDTH, SEG_HEIGHT, color);
    } else {
      tft.fillRect(x + 1, y + 1, TANK_WIDTH - 2, SEG_HEIGHT - 2, BACK);
      tft.drawRect(x, y, TANK_WIDTH, SEG_HEIGHT, SAEULEN_OUTLINE);
    }
  }
}

static void drawColumn(int index, const UsageColumn& col, const char* label) {
  Column& c = cols[index];
  int cx = COL_X[index];
  uint16_t color = col.present ? (col.warn ? SAEULEN_WARN : SAEULEN_MAIN) : SAEULEN_DIM;
  bool full = !c.valid || c.present != col.present || c.warn != col.warn;

  char pct[6];
  formatPercent(col, pct, sizeof(pct));
  if (textChanged(c.percent, sizeof(c.percent), pct) || full) {
    // Size 4: 24 x 32 pixels per character, "100%" is 96 pixels wide.
    viewText(pct, cx, PERCENT_Y, 1, 4, color, BACK, MC_DATUM, 110);
  }
  if (full || c.segments != col.segments) {
    drawTank(cx, col.segments, color);
  }
  if (full) {
    viewText(label, cx, LABEL_Y, 1, 2, color, BACK, MC_DATUM, 110);
  }
  if (textChanged(c.bottom, sizeof(c.bottom), col.bottom) || full) {
    viewText(col.bottom, cx, BOTTOM_Y, 2, 1, SAEULEN_DIM, BACK, MC_DATUM, 112);
  }
  c.valid = true;
  c.present = col.present;
  c.warn = col.warn;
  c.segments = col.segments;
}

void saeulenShow(const UsageView& view) {
  if (view.screen != USAGE_SCREEN_LIMITS) {
    static const SpecialStyle style = {BACK, SAEULEN_MAIN, SAEULEN_DIM, SAEULEN_WARN, false, false};
    drawSpecial(view, style, special, !drawn || drawnScreen != view.screen);
    drawn = true;
    drawnScreen = view.screen;
    return;
  }
  if (!drawn || drawnScreen != view.screen || drawnUsed != view.showUsed) {
    drawFrame(view.showUsed);
    drawn = true;
    drawnScreen = view.screen;
    drawnUsed = view.showUsed;
    specialInvalidate(special);
  }
  if (textChanged(drawnClock, sizeof(drawnClock), view.clock)) {
    vgaText(view.clock, 232, HEADER_Y, ALIGN_RIGHT, 1, SAEULEN_DIM, BACK, 40);
  }
  drawColumn(0, view.session, "SESSION");
  drawColumn(1, view.week, "WOCHE");
}
