// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.

#pragma once
#include <Arduino.h>

void netBegin(bool rescue);
void netLoop();

// True while nothing else (setup hotspot, lost WLAN, pending restart after an update
// error) needs the display, so the design may draw.
bool netScreenFree();
