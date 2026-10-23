// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Table of screen designs. Each design only draws a UsageView and knows
// nothing about WLAN, web pages or updates. A new design gets its own
// view_<name>.cpp, its two functions in views.h and one entry in the table.

#pragma once
#include <stdint.h>
#include "usage_data.h"

struct Design {
  const char* name;      // UTF-8, for /api/info
  const char* htmlName;  // for the web pages, umlauts as HTML entities
  // The screen was drawn over by something else, draw everything again next time.
  void (*invalidate)();
  // Draw the view. Called about twice a second, should only redraw what changed.
  void (*show)(const UsageView& view);
};

enum DesignId : uint8_t {
  DESIGN_SAEULEN,
  DESIGN_PHOSPHOR,
  DESIGN_DOS,
  DESIGN_ANSI,
  DESIGN_AMBER,
  DESIGN_KLASSIK,  // new designs only at the end, the numbers are stored in the settings
  DESIGN_COUNT
};

// Returns the design with this id, or the first one for an unknown id.
const Design& designGet(uint8_t id);

// Design that is on the display now: the selected one, or with automatic
// change the next one every 30 or 60 seconds. designUpdate() moves the
// automatic change forward and is called from the main loop.
uint8_t designUpdate();
uint8_t designActive();
