// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Design 5 "Bernstein" (draft D in docs/screen-entwuerfe-2.png): two tanks
// with scale marks, filled from the bottom, the number inside the tank, and
// a status bar at the bottom. Where the fill reaches the number, the digits
// are dark on amber, above it amber on dark. Clipping does the split.

#include "views.h"
#include "view_common.h"
#include "config.h"
#include <TFT_eSPI.h>
#include <stdio.h>

static const uint16_t BACK = SYSOPTV_BLACK;

static const int TANK_X[2] = {16, 128};
static const int TANK_Y = 8;
static const int TANK_W = 96;
static const int TANK_H = 168;
static const int INNER = 3;                  // frame thickness plus gap
static const int NUMBER_Y = TANK_Y + 60;     // top of the digits, 48 pixels high
static const int LABEL_Y = 182;
static const int BOTTOM_Y = 200;
static const int BAR_Y = 222;

struct Column {
  bool valid;
  bool present;
  bool warn;
  uint8_t level;
  char bottom[16];
};

static bool drawn = false;
static UsageScreen drawnScreen = USAGE_SCREEN_LIMITS;
static bool drawnUsed = false;
static char drawnClock[6];
static Column cols[2];
static SpecialCache special;

void amberInvalidate() {
  drawn = false;
  specialInvalidate(special);
}

static void drawTankFrame(int index) {
  TFT_eSPI& tft = viewTft();
  int x = TANK_X[index];
  tft.drawRect(x, TANK_Y, TANK_W, TANK_H, AMBER_FRAME);
  tft.drawRect(x + 1, TANK_Y + 1, TANK_W - 2, TANK_H - 2, AMBER_FRAME);
}

static void drawFrame(bool showUsed) {
  TFT_eSPI& tft = viewTft();
  tft.fillScreen(BACK);
  drawTankFrame(0);
  drawTankFrame(1);
  vgaText("SESSION", TANK_X[0] + TANK_W / 2, LABEL_Y, ALIGN_CENTER, 1, AMBER_MAIN, BACK);
  vgaText("WOCHE", TANK_X[1] + TANK_W / 2, LABEL_Y, ALIGN_CENTER, 1, AMBER_MAIN, BACK);
  tft.fillRect(0, BAR_Y, 240, 240 - BAR_Y, AMBER_BAR);
  vgaText(showUsed ? "VERBRAUCHT IN %" : "\xc3\x9c" "BRIG IN %", 6, BAR_Y + 1, ALIGN_LEFT, 1, SYSOPTV_BLACK, AMBER_BAR);
  drawnClock[0] = '\0';
  cols[0].valid = false;
  cols[1].valid = false;
}

static void drawTank(int index, const UsageColumn& col) {
  TFT_eSPI& tft = viewTft();
  int x = TANK_X[index] + INNER;
  int y = TANK_Y + INNER;
  int w = TANK_W - 2 * INNER;
  int h = TANK_H - 2 * INNER;
  uint8_t level = col.present ? col.percent : 0;
  uint16_t fill = col.warn ? AMBER_WARN : AMBER_MAIN;
  int filled = (level * h + 50) / 100;
  int fillTop = y + h - filled;

  // Empty part black with a 1 pixel outline, like the empty segments of Klassik.
  if (filled < h) {
    tft.fillRect(x + 1, y + 1, w - 2, h - filled - 2, SYSOPTV_BLACK);
    tft.drawRect(x, y, w, h - filled, col.warn ? ANSI_RED_DIM : AMBER_DIM);
  }
  if (filled > 0) {
    tft.fillRect(x, fillTop, w, filled, fill);
  }
  // Scale marks on the left at 25, 50 and 75 percent.
  for (int q = 1; q <= 3; q++) {
    int my = y + h - (h * q) / 4;
    tft.drawFastHLine(x, my, 10, my >= fillTop ? SYSOPTV_BLACK : AMBER_FRAME);
  }

  char number[6];
  if (col.present) {
    snprintf(number, sizeof(number), "%u", level);
  } else {
    snprintf(number, sizeof(number), "--");
  }
  int cx = x + w / 2;
  uint16_t upper = col.present ? fill : AMBER_DIM;
  // Above the fill: digits in the fill colour on the dark empty part.
  if (fillTop > NUMBER_Y) {
    tft.setViewport(x, y, w, fillTop - y, false);
    vgaTextTransparent(number, cx, NUMBER_Y, ALIGN_CENTER, 3, upper);
    tft.resetViewport();
  }
  // Inside the fill: dark digits.
  if (filled > 0 && fillTop < NUMBER_Y + 48) {
    tft.setViewport(x, fillTop, w, y + h - fillTop, false);
    vgaTextTransparent(number, cx, NUMBER_Y, ALIGN_CENTER, 3, SYSOPTV_BLACK);
    tft.resetViewport();
  }
}

static void drawColumn(int index, const UsageColumn& col) {
  Column& c = cols[index];
  uint8_t level = col.present ? col.percent : 0;
  bool full = !c.valid || c.present != col.present || c.warn != col.warn;
  if (full || c.level != level) {
    drawTank(index, col);
  }
  if (textChanged(c.bottom, sizeof(c.bottom), col.bottom) || full) {
    vgaText(col.bottom, TANK_X[index] + TANK_W / 2, BOTTOM_Y, ALIGN_CENTER, 1, AMBER_DIM, BACK, 110);
  }
  c.valid = true;
  c.present = col.present;
  c.warn = col.warn;
  c.level = level;
}

void amberShow(const UsageView& view) {
  if (view.screen != USAGE_SCREEN_LIMITS) {
    static const SpecialStyle style = {BACK, AMBER_MAIN, AMBER_DIM, AMBER_WARN, true, false};
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
    vgaText(view.clock, 234, BAR_Y + 1, ALIGN_RIGHT, 1, SYSOPTV_BLACK, AMBER_BAR);
  }
  drawColumn(0, view.session);
  drawColumn(1, view.week);
}
