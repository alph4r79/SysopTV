// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.

#pragma once
#include <Arduino.h>

class TFT_eSPI;

// The display driver, for the VGA text and the picture decoder.
TFT_eSPI& screenTft();

void screenBegin();
// Backlight 0 to 100 percent (0 is off). Only changes the PWM when needed.
void screenBrightness(uint8_t percent);
// Shown after the password reset by short power cycles.
void screenResetDone();
void screenBoot(bool rescue);
void screenConnecting(const String& ssid);
void screenReady(const String& ip, const String& host, bool rescue);
void screenLost();
void screenSetup(const char* apName);
void screenUpdateStart();
void screenUpdateProgress(size_t bytes);
void screenUpdateResult(bool ok, const String& message);
// Shown when no picture arrives: big clock, "NO CARRIER", when the last
// picture came and the IP address. full clears the screen first, otherwise
// only the texts that changed are redrawn.
void screenNoCarrier(bool full, const char* clock, const char* lastData, const char* ip);
