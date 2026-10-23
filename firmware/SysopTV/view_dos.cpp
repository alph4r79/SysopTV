// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Design 3 "DOS" (draft B in docs/screen-entwuerfe-2.png): blue background,
// cyan menu bar with the clock, two windows with double frames and the title
// in the top border, big number, stack of block bars, reset at the bottom.

#include "views.h"
#include "view_common.h"
#include "config.h"
#include <TFT_eSPI.h>
#include <stdio.h>

static const uint16_t BACK = DOS_BLUE;

static const int WIN_X[2] = {4, 123};
static const int WIN_Y = 28;
static const int WIN_W = 113;
static const int WIN_H = 208;
static const int NUMBER_Y = 40;
static const int MODE_Y = 90;
static const int BARS_TOP = 114;
static const int BAR_W = 64;
static const int BAR_H = 6;
static const int BAR_PITCH = 9;
static const int BOTTOM_Y = 210;

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

void dosInvalidate() {
  drawn = false;
  specialInvalidate(special);
}

static int centreX(int index) {
  return WIN_X[index] + WIN_W / 2;
}

static void drawWindow(int index, const char* title) {
  drawDoubleFrame(WIN_X[index], WIN_Y, WIN_W, WIN_H, DOS_WHITE);
  // The title sits on the top border and interrupts both lines.
  char t[12];
  snprintf(t, sizeof(t), " %s ", title);
  vgaText(t, centreX(index), WIN_Y - 8, ALIGN_CENTER, 1, DOS_WHITE, BACK);
}

static void drawFrame(bool showUsed) {
  TFT_eSPI& tft = viewTft();
  tft.fillScreen(BACK);
  tft.fillRect(0, 0, 240, 18, DOS_CYAN);
  vgaText("CLAUDE", 8, 1, ALIGN_LEFT, 1, SYSOPTV_BLACK, DOS_CYAN);
  drawWindow(0, "SESSION");
  drawWindow(1, "WOCHE");
  for (int i = 0; i < 2; i++) {
    vgaText(showUsed ? "Verbraucht" : "Frei", centreX(i), MODE_Y, ALIGN_CENTER, 1, DOS_GRAY, BACK);
  }
  drawnClock[0] = '\0';
  cols[0].valid = false;
  cols[1].valid = false;
}

static void drawBars(int cx, uint8_t segments, uint16_t color) {
  TFT_eSPI& tft = viewTft();
  int x = cx - BAR_W / 2;
  for (int i = 0; i < USAGE_SEGMENTS; i++) {
    int y = BARS_TOP + (USAGE_SEGMENTS - 1 - i) * BAR_PITCH;
    tft.fillRect(x, y, BAR_W, BAR_H, i < segments ? color : DOS_EMPTY);
  }
}

static void drawColumn(int index, const UsageColumn& col) {
  Column& c = cols[index];
  int cx = centreX(index);
  uint16_t color = col.present ? (col.warn ? DOS_WARN : DOS_YELLOW) : DOS_GRAY;
  bool full = !c.valid || c.present != col.present || c.warn != col.warn;

  char pct[6];
  formatPercent(col, pct, sizeof(pct));
  if (textChanged(c.percent, sizeof(c.percent), pct) || full) {
    // Scale 3: 24 x 48 pixels per character, "100%" is 96 pixels wide.
    vgaText(pct, cx, NUMBER_Y, ALIGN_CENTER, 3, color, BACK, 100);
  }
  if (full || c.segments != col.segments) {
    drawBars(cx, col.segments, color);
  }
  if (textChanged(c.bottom, sizeof(c.bottom), col.bottom) || full) {
    vgaText(col.bottom, cx, BOTTOM_Y, ALIGN_CENTER, 1, DOS_CYAN, BACK, 104);
  }
  c.valid = true;
  c.present = col.present;
  c.warn = col.warn;
  c.segments = col.segments;
}

void dosShow(const UsageView& view) {
  if (view.screen != USAGE_SCREEN_LIMITS) {
    static const SpecialStyle style = {BACK, DOS_YELLOW, DOS_CYAN, DOS_WARN, true, false};
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
    vgaText(view.clock, 232, 1, ALIGN_RIGHT, 1, SYSOPTV_BLACK, DOS_CYAN);
  }
  drawColumn(0, view.session);
  drawColumn(1, view.week);
}
