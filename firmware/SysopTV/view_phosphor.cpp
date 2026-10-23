// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Design 2 "Phosphor" (draft A in docs/screen-entwuerfe-2.png): green text
// mode, header line, two narrow columns. The filled part has scan lines and
// a checkered top edge, the empty part is the light shade pattern.

#include "views.h"
#include "view_common.h"
#include "config.h"
#include <TFT_eSPI.h>

static const uint16_t BACK = SYSOPTV_BLACK;

static const int COL_X[2] = {60, 180};
static const int PERCENT_Y = 34;
static const int BAR_TOP = 74;
static const int BAR_HEIGHT = 120;
static const int BAR_WIDTH = 40;
static const int LABEL_Y = 200;
static const int BOTTOM_Y = 220;

struct Column {
  bool valid;
  bool present;
  bool warn;
  char percent[6];
  uint8_t level;
  char bottom[16];
};

static bool drawn = false;
static UsageScreen drawnScreen = USAGE_SCREEN_LIMITS;
static bool drawnUsed = false;
static char drawnClock[6];
static Column cols[2];
static SpecialCache special;

void phosphorInvalidate() {
  drawn = false;
  specialInvalidate(special);
}

static void drawFrame(bool showUsed) {
  viewTft().fillScreen(BACK);
  // CLAUDE at x 8, mode word at x 68, clock right at 232, all in the VGA font.
  vgaText("CLAUDE", 8, 6, ALIGN_LEFT, 1, PHOSPHOR_MAIN, BACK);
  vgaText(showUsed ? "VERBRAUCHT" : "\xc3\x9c" "BRIG", 68, 6, ALIGN_LEFT, 1, PHOSPHOR_DIM, BACK);
  drawnClock[0] = '\0';
  cols[0].valid = false;
  cols[1].valid = false;
}

static void drawBar(int cx, uint8_t percent, bool warn) {
  TFT_eSPI& tft = viewTft();
  uint16_t main = warn ? PHOSPHOR_WARN : PHOSPHOR_MAIN;
  uint16_t scan = warn ? PHOSPHOR_WARN_SCAN : PHOSPHOR_SCAN;
  int x = cx - BAR_WIDTH / 2;
  int filled = (percent * BAR_HEIGHT + 50) / 100;
  if (percent > 0 && filled < 4) {
    filled = 4;
  }
  int empty = BAR_HEIGHT - filled;
  // Empty part first, then the filled part below it. Nothing is cleared
  // beforehand, every pixel is drawn exactly once.
  fillShade(x, BAR_TOP, BAR_WIDTH, empty, 1, 2, PHOSPHOR_SHADE, BACK);
  if (filled > 0) {
    int top = BAR_TOP + empty;
    int edge = filled >= 4 ? 4 : filled;
    fillShade(x, top, BAR_WIDTH, edge, 2, 2, main, BACK);
    tft.fillRect(x, top + edge, BAR_WIDTH, filled - edge, main);
    for (int y = top + edge + 2; y < BAR_TOP + BAR_HEIGHT; y += 3) {
      tft.drawFastHLine(x, y, BAR_WIDTH, scan);
    }
  }
}

static void drawColumn(int index, const UsageColumn& col, const char* label) {
  Column& c = cols[index];
  int cx = COL_X[index];
  uint16_t color = col.present ? (col.warn ? PHOSPHOR_WARN : PHOSPHOR_MAIN) : PHOSPHOR_DIM;
  bool full = !c.valid || c.present != col.present || c.warn != col.warn;

  char pct[6];
  formatPercent(col, pct, sizeof(pct));
  if (textChanged(c.percent, sizeof(c.percent), pct) || full) {
    // Scale 2: 16 x 32 pixels per character.
    vgaText(pct, cx, PERCENT_Y, ALIGN_CENTER, 2, color, BACK, 112);
  }
  uint8_t level = col.present ? col.percent : 0;
  if (full || c.level != level) {
    drawBar(cx, level, col.warn);
  }
  if (full) {
    vgaText(label, cx, LABEL_Y, ALIGN_CENTER, 1, color, BACK, 112);
  }
  if (textChanged(c.bottom, sizeof(c.bottom), col.bottom) || full) {
    vgaText(col.bottom, cx, BOTTOM_Y, ALIGN_CENTER, 1, PHOSPHOR_DIM, BACK, 116);
  }
  c.valid = true;
  c.present = col.present;
  c.warn = col.warn;
  c.level = level;
}

void phosphorShow(const UsageView& view) {
  if (view.screen != USAGE_SCREEN_LIMITS) {
    static const SpecialStyle style = {BACK, PHOSPHOR_MAIN, PHOSPHOR_DIM, PHOSPHOR_WARN, true, false};
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
    vgaText(view.clock, 232, 6, ALIGN_RIGHT, 1, PHOSPHOR_DIM, BACK, 40);
  }
  drawColumn(0, view.session, "SESSION");
  drawColumn(1, view.week, "WOCHE");
}
