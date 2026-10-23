// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Design 4 "ANSI" (draft C in docs/screen-entwuerfe-2.png): logo line with a
// shade gradient, two blocks with name, big number, a bar that runs out in
// shades and dots, and the reset line. Without data the special screen
// shows a small "NO CARRIER" at the bottom.

#include "views.h"
#include "view_common.h"
#include "config.h"
#include <TFT_eSPI.h>
#include <stdio.h>

static const uint16_t BACK = SYSOPTV_BLACK;

static const int BLOCK_Y[2] = {32, 128};  // top of the session and week block
static const int BAR_X = 8;
static const int BAR_W = 224;
static const int BAR_H = 14;
static const int CELL = 8;  // width of one shade step, like one text column

struct Block {
  uint16_t main;
  uint16_t dim;
  const char* name;
};
static const Block BLOCKS[2] = {
  {ANSI_CYAN, ANSI_CYAN_DIM, "SESSION"},
  {ANSI_YELLOW, ANSI_YELLOW_DIM, "WOCHE"},
};

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

void ansiInvalidate() {
  drawn = false;
  specialInvalidate(special);
}

static void drawLogo() {
  // Dark, medium, light shade, the spaced name, then the same mirrored.
  for (int i = 0; i < 3; i++) {
    fillShade(28 + i * CELL, 4, CELL, 16, 3 - i, 1, ANSI_MAGENTA, BACK);
    fillShade(188 + i * CELL, 4, CELL, 16, i + 1, 1, ANSI_MAGENTA, BACK);
  }
  vgaText("C L A U D E", 120, 4, ALIGN_CENTER, 1, ANSI_MAGENTA, BACK);
  viewTft().drawFastHLine(4, 24, 232, ANSI_MAGENTA);
}

static void drawFrame(bool showUsed) {
  viewTft().fillScreen(BACK);
  drawLogo();
  for (int i = 0; i < 2; i++) {
    vgaText(BLOCKS[i].name, 8, BLOCK_Y[i], ALIGN_LEFT, 1, BLOCKS[i].main, BACK);
    vgaText(showUsed ? "Verbraucht" : "\xc3\x9c" "brig", 8, BLOCK_Y[i] + 18, ALIGN_LEFT, 1, ANSI_WHITE, BACK);
  }
  drawnClock[0] = '\0';
  cols[0].valid = false;
  cols[1].valid = false;
}

// Solid part, then three cells running out in shades, then dots.
static void drawBar(int y, uint8_t percent, uint16_t color, uint16_t trail) {
  TFT_eSPI& tft = viewTft();
  int solid = (percent * BAR_W + 50) / 100;
  int x = BAR_X;
  if (solid > 0) {
    tft.fillRect(x, y, solid, BAR_H, color);
    x += solid;
  }
  for (int level = 3; level >= 1 && x < BAR_X + BAR_W && percent > 0; level--) {
    int w = CELL;
    if (x + w > BAR_X + BAR_W) {
      w = BAR_X + BAR_W - x;
    }
    fillShade(x, y, w, BAR_H, level, 1, trail, BACK);
    x += w;
  }
  if (x < BAR_X + BAR_W) {
    tft.fillRect(x, y, BAR_X + BAR_W - x, BAR_H, BACK);
    // Dots in the middle of every text column, like "........".
    int first = ((x - BAR_X + CELL - 1) / CELL) * CELL + BAR_X;
    for (int dx = first; dx + 2 <= BAR_X + BAR_W; dx += CELL) {
      tft.fillRect(dx + 3, y + BAR_H / 2 - 1, 2, 2, ANSI_DOT);
    }
  }
}

static void drawColumn(int index, const UsageColumn& col) {
  Column& c = cols[index];
  const Block& b = BLOCKS[index];
  int y = BLOCK_Y[index];
  uint16_t color = col.present ? (col.warn ? ANSI_RED : b.main) : ANSI_GRAY;
  uint16_t trail = col.warn ? ANSI_RED : b.main;
  bool full = !c.valid || c.present != col.present || c.warn != col.warn;

  char pct[6];
  formatPercent(col, pct, sizeof(pct));
  if (textChanged(c.percent, sizeof(c.percent), pct) || full) {
    // Scale 3: 24 x 48 pixels per character, right aligned.
    vgaText(pct, 232, y - 2, ALIGN_RIGHT, 3, color, BACK, 100);
  }
  uint8_t level = col.present ? col.percent : 0;
  if (full || c.level != level) {
    drawBar(y + 52, level, color, trail);
  }
  if (textChanged(c.bottom, sizeof(c.bottom), col.bottom) || full) {
    char line[24];
    usageResetLine(col, line, sizeof(line));
    vgaText(line, 8, y + 72, ALIGN_LEFT, 1, b.dim, BACK, 224);
  }
  c.valid = true;
  c.present = col.present;
  c.warn = col.warn;
  c.level = level;
}

void ansiShow(const UsageView& view) {
  if (view.screen != USAGE_SCREEN_LIMITS) {
    static const SpecialStyle style = {BACK, ANSI_CYAN, ANSI_GRAY, ANSI_RED, true, true};
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
    vgaText(view.clock, 232, 222, ALIGN_RIGHT, 1, ANSI_GRAY, BACK);
  }
  drawColumn(0, view.session);
  drawColumn(1, view.week);
}
