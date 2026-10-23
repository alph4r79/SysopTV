// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// The clock is set from "now" in every packet and then runs on millis(), so
// the countdown keeps going between two packets. Without a recent packet the
// time comes from NTP (configured in net.cpp with the Europe/Berlin rules).

#include "usage.h"
#include "config.h"
#include "settings.h"
#include <Arduino.h>
#include <time.h>

static bool havePacket = false;
static UsageState lastState = USAGE_STATE_ERROR;
static unsigned long lastPacketMillis = 0;
static bool stale = false;

static bool hasOk = false;
static UsageLimit okSession = {false, 0, 0};
static UsageLimit okWeek = {false, 0, 0};
static int64_t lastOkUtc = 0;

static bool clockValid = false;
static int64_t clockBaseUtc = 0;
static unsigned long clockBaseMillis = 0;
static int32_t tzOffset = 0;
// Whether the sender time is recent enough to beat NTP. Kept as a flag, so it
// cannot come back to life when millis() overflows.
static bool senderTimeFresh = false;
static unsigned long senderTimeMillis = 0;

static int64_t clockNow() {
  unsigned long elapsed = millis() - clockBaseMillis;
  // Move the base forward once a day, so the millis() overflow after
  // 49 days cannot break the clock if no packet arrives for that long.
  if (elapsed >= 86400000UL) {
    unsigned long seconds = elapsed / 1000;
    clockBaseUtc += seconds;
    clockBaseMillis += seconds * 1000;
    elapsed -= seconds * 1000;
  }
  return clockBaseUtc + elapsed / 1000;
}

void usageApply(const UsagePacket& packet) {
  if (packet.hasNow) {
    clockValid = true;
    clockBaseUtc = packet.now;
    clockBaseMillis = millis();
    senderTimeFresh = true;
    senderTimeMillis = millis();
  }
  if (packet.hasTz) {
    tzOffset = packet.tz;
  }
  lastState = packet.state;
  if (packet.state == USAGE_STATE_OK) {
    hasOk = true;
    okSession = packet.session;
    okWeek = packet.week;
    lastOkUtc = packet.now;
  }
  havePacket = true;
  stale = false;
  lastPacketMillis = millis();
}

bool usageHasPacket() {
  return havePacket;
}

bool usageScreenActive() {
  // Kept as a flag, so a millis() overflow cannot bring the "Ready" screen back.
  static bool noPcTimeout = false;
  if (!noPcTimeout && millis() >= SYSOPTV_NO_PC_AFTER_MS) {
    noPcTimeout = true;
  }
  return havePacket || noPcTimeout;
}

// Offset of the local time zone (TZ rules from configTime) at time t.
static int32_t localOffset(time_t t) {
  struct tm lt;
  localtime_r(&t, &lt);
  int64_t local = usageDaysFromCivil(lt.tm_year + 1900, lt.tm_mon + 1, lt.tm_mday) * 86400 +
                  lt.tm_hour * 3600 + lt.tm_min * 60 + lt.tm_sec;
  return (int32_t)(local - (int64_t)t);
}

TimeSource usageTimeSource() {
  if (senderTimeFresh && millis() - senderTimeMillis >= SYSOPTV_SENDER_TIME_MS) {
    senderTimeFresh = false;
  }
  if (senderTimeFresh) {
    return TIME_SENDER;
  }
  if (time(nullptr) > (time_t)SYSOPTV_TIME_VALID_AFTER) {
    return TIME_NTP;
  }
  return clockValid ? TIME_SENDER_OLD : TIME_NONE;
}

bool usageTime(int64_t& utc, int32_t& tz) {
  switch (usageTimeSource()) {
    case TIME_SENDER:
    case TIME_SENDER_OLD:
      utc = clockNow();
      tz = tzOffset;
      return true;
    case TIME_NTP: {
      time_t t = time(nullptr);
      utc = t;
      tz = localOffset(t);
      return true;
    }
    default:
      return false;
  }
}

static void updateStale() {
  // Once stale, stay stale until the next packet (also across a millis() overflow).
  if (havePacket && !stale && millis() - lastPacketMillis >= SYSOPTV_USAGE_STALE_MS) {
    stale = true;
  }
}

void usageGetStatus(UsageStatus& out) {
  updateStale();
  out.havePacket = havePacket;
  out.stale = stale;
  out.state = lastState;
  out.ageSeconds = havePacket ? (millis() - lastPacketMillis) / 1000 : 0;
}

void usageBuildView(UsageView& out) {
  updateStale();
  UsageInput in;
  in.showUsed = settingsGet().showUsed;
  in.state = lastState;
  in.stale = stale || !havePacket;  // no packet yet counts as no data
  int64_t utc = 0;
  int32_t tz = 0;
  in.clockValid = usageTime(utc, tz);
  in.nowUtc = utc;
  in.tz = tz;
  in.session = okSession;
  in.week = okWeek;
  in.hasOk = hasOk;
  in.lastOkUtc = lastOkUtc;
  usageMakeView(in, out);
}
