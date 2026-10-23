// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.

#pragma once
#include <Arduino.h>

class TFT_eSPI;

// The display driver, for the designs (view_*.cpp).
TFT_eSPI& screenTft();

void screenBegin();
// Backlight 0 to 100 percent (0 is off). Only changes the PWM when needed.
void screenBrightness(uint8_t percent);
// Shown after the password reset by short power cycles.
void screenResetDone();
void screenBoot(bool rescue);
void screenConnecting(const String& ssid);
void screenReady(const String& ip, bool rescue);
void screenLost();
void screenSetup(const char* apName);
void screenUpdateStart();
void screenUpdateProgress(size_t bytes);
void screenUpdateResult(bool ok, const String& message);
