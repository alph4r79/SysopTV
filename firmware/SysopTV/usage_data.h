// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Usage data sent by the PC: JSON parsing and the texts shown on screen.
// Nothing in here depends on Arduino or the display, so it can also be
// compiled and tested on a PC.

#pragma once
#include <stddef.h>
#include <stdint.h>

enum UsageState : uint8_t {
  USAGE_STATE_OK,
  USAGE_STATE_LOGIN,
  USAGE_STATE_ERROR
};

struct UsageLimit {
  bool present;
  uint8_t used;       // percent used, 0 to 100
  uint32_t resetsAt;  // Unix seconds (UTC), 0 if unknown
};

struct UsagePacket {
  UsageState state;
  bool hasNow;
  uint32_t now;  // Unix seconds (UTC)
  bool hasTz;
  int32_t tz;    // offset to UTC in seconds, including daylight saving time
  UsageLimit session;
  UsageLimit week;
};

// Parses the body of POST /api/usage. Returns nullptr on success, otherwise a
// short error text. out is only valid on success.
const char* usageParse(const char* json, size_t len, UsagePacket& out);

enum UsageScreen : uint8_t {
  USAGE_SCREEN_LIMITS,
  USAGE_SCREEN_LOGIN,
  USAGE_SCREEN_NO_CARRIER
};

struct UsageColumn {
  bool present;
  bool warn;          // less than SYSOPTV_WARN_BELOW percent left, in both modes
  uint8_t used;       // percent used, 0 to 100
  uint8_t remaining;  // percent left, 0 to 100
  uint8_t percent;    // the value to show: remaining or used, see UsageInput::showUsed
  uint8_t segments;   // filled segments for percent, 0 to USAGE_SEGMENTS
  char bottom[16];    // countdown or reset time
  char resetClock[6]; // local time of the reset "HH:MM", empty if unknown
};

static const uint8_t USAGE_SEGMENTS = 10;

// Everything a design needs to draw the usage screen.
struct UsageView {
  UsageScreen screen;
  bool showUsed;      // percent and segments show the usage instead of the rest
  char clock[6];      // "HH:MM" or "--:--"
  UsageColumn session;  // always the last valid values, also on special screens
  UsageColumn week;
  char lastData[24];  // only for USAGE_SCREEN_NO_CARRIER
  char ip[16];        // IP address of the cube, set by main.cpp
};

// Input for usageMakeView(), collected by usage.cpp.
struct UsageInput {
  bool showUsed;        // display mode from the settings
  UsageState state;     // state of the last packet
  bool stale;           // no packet for SYSOPTV_USAGE_STALE_MS
  bool clockValid;
  int64_t nowUtc;
  int32_t tz;
  UsageLimit session;   // from the last packet with state "ok"
  UsageLimit week;
  bool hasOk;
  int64_t lastOkUtc;    // when the last packet with state "ok" arrived
};

void usageMakeView(const UsageInput& in, UsageView& out);

// Reset time rounded up to the full minute (22:59:59 becomes 23:00, 23:00:00
// stays). Every reset time and countdown on display and web page uses this.
int64_t usageResetRounded(uint32_t resetsAt);

// Days since 1970-01-01 for a calendar date (proleptic Gregorian).
int64_t usageDaysFromCivil(int year, int month, int day);

// Text helpers, public for the tests.
void usageFormatClock(int64_t localSeconds, char* out, size_t size);
void usageFormatCountdown(int64_t secondsLeft, char* out, size_t size);
void usageFormatWeekReset(int64_t nowLocal, int64_t resetLocal, char* out, size_t size);
