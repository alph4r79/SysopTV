// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.

#include "usage_data.h"
#include "config.h"
#include <ArduinoJson.h>
#include <math.h>
#include <stdio.h>
#include <string.h>

// Plausible range for Unix times: 2020-01-01 to 2100-01-01.
static const uint32_t TIME_MIN = 1577836800UL;
static const uint32_t TIME_MAX = 4102444800UL;
// Time zones range from UTC-12 to UTC+14.
static const int32_t TZ_MIN = -12 * 3600;
static const int32_t TZ_MAX = 14 * 3600;

static bool readTime(JsonVariantConst v, uint32_t& out) {
  if (!v.is<uint32_t>()) {
    return false;
  }
  uint32_t t = v.as<uint32_t>();
  if (t < TIME_MIN || t > TIME_MAX) {
    return false;
  }
  out = t;
  return true;
}

// A limit object may be missing or null. If it is there, "used" is required,
// "resetsAt" is optional because Anthropic leaves it empty without an active session.
static const char* readLimit(JsonVariantConst v, UsageLimit& out, const char* errType,
                             const char* errUsed, const char* errReset) {
  out.present = false;
  out.used = 0;
  out.resetsAt = 0;
  if (v.isNull()) {
    return nullptr;
  }
  if (!v.is<JsonObjectConst>()) {
    return errType;
  }
  JsonVariantConst used = v["used"];
  if (!used.is<float>()) {
    return errUsed;
  }
  float u = used.as<float>();
  if (isnan(u)) {
    return errUsed;
  }
  if (u < 0) {
    u = 0;
  } else if (u > 100) {
    u = 100;
  }
  JsonVariantConst reset = v["resetsAt"];
  if (!reset.isNull() && !readTime(reset, out.resetsAt)) {
    return errReset;
  }
  out.used = (uint8_t)lroundf(u);
  out.present = true;
  return nullptr;
}

const char* usageParse(const char* json, size_t len, UsagePacket& out) {
  JsonDocument doc;
  DeserializationError err = deserializeJson(doc, json, len, DeserializationOption::NestingLimit(4));
  if (err) {
    return "JSON fehlerhaft";
  }
  JsonObjectConst root = doc.as<JsonObjectConst>();
  if (root.isNull()) {
    return "kein JSON-Objekt";
  }

  JsonVariantConst state = root["state"];
  if (!state.is<const char*>()) {
    return "state fehlt";
  }
  const char* s = state.as<const char*>();
  if (strcmp(s, "ok") == 0) {
    out.state = USAGE_STATE_OK;
  } else if (strcmp(s, "login") == 0) {
    out.state = USAGE_STATE_LOGIN;
  } else if (strcmp(s, "error") == 0) {
    out.state = USAGE_STATE_ERROR;
  } else {
    return "state unbekannt";
  }

  // now and tz are required with "ok", because the countdown needs them.
  // With "login" and "error" they are optional but used if present.
  JsonVariantConst now = root["now"];
  out.hasNow = !now.isNull();
  out.now = 0;
  if (out.hasNow && !readTime(now, out.now)) {
    return "now ungueltig";
  }
  JsonVariantConst tz = root["tz"];
  out.hasTz = !tz.isNull();
  out.tz = 0;
  if (out.hasTz) {
    if (!tz.is<int32_t>()) {
      return "tz ungueltig";
    }
    out.tz = tz.as<int32_t>();
    if (out.tz < TZ_MIN || out.tz > TZ_MAX) {
      return "tz ungueltig";
    }
  }
  if (out.state == USAGE_STATE_OK && (!out.hasNow || !out.hasTz)) {
    return "now oder tz fehlt";
  }

  const char* e = readLimit(root["session"], out.session, "session ungueltig",
                            "session.used ungueltig", "session.resetsAt ungueltig");
  if (e) {
    return e;
  }
  e = readLimit(root["week"], out.week, "week ungueltig",
                "week.used ungueltig", "week.resetsAt ungueltig");
  if (e) {
    return e;
  }
  if (out.state != USAGE_STATE_OK) {
    // Numbers only count with "ok".
    out.session.present = false;
    out.week.present = false;
  }
  return nullptr;
}

// ---------------------------------------------------------------------------
// Texts
// ---------------------------------------------------------------------------

int64_t usageResetRounded(uint32_t resetsAt) {
  return ((int64_t)resetsAt + 59) / 60 * 60;
}

int64_t usageDaysFromCivil(int year, int month, int day) {
  // Algorithm from Howard Hinnant, "chrono-compatible low-level date algorithms".
  int64_t y = month <= 2 ? year - 1 : year;
  int64_t era = (y >= 0 ? y : y - 399) / 400;
  int64_t yoe = y - era * 400;
  int64_t mp = month > 2 ? month - 3 : month + 9;
  int64_t doy = (153 * mp + 2) / 5 + day - 1;
  int64_t doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
  return era * 146097 + doe - 719468;
}

static int64_t floorDiv(int64_t a, int64_t b) {
  int64_t q = a / b;
  if ((a % b != 0) && ((a < 0) != (b < 0))) {
    q--;
  }
  return q;
}

void usageFormatClock(int64_t localSeconds, char* out, size_t size) {
  int64_t daySec = localSeconds - floorDiv(localSeconds, 86400) * 86400;
  snprintf(out, size, "%02d:%02d", (int)(daySec / 3600), (int)(daySec % 3600 / 60));
}

void usageFormatCountdown(int64_t secondsLeft, char* out, size_t size) {
  if (secondsLeft <= 0) {
    snprintf(out, size, "Reset faellig");
    return;
  }
  // Round up, so "noch 0:01" stays until the reset is really due.
  int64_t minutes = (secondsLeft + 59) / 60;
  int64_t hours = minutes / 60;
  if (hours > 99) {
    hours = 99;
  }
  snprintf(out, size, "noch %d:%02d", (int)hours, (int)(minutes % 60));
}

void usageFormatWeekReset(int64_t nowLocal, int64_t resetLocal, char* out, size_t size) {
  static const char* const days[] = {"So", "Mo", "Di", "Mi", "Do", "Fr", "Sa"};
  if (resetLocal <= nowLocal) {
    snprintf(out, size, "Reset faellig");
    return;
  }
  char clock[6];
  usageFormatClock(resetLocal, clock, sizeof(clock));
  int64_t today = floorDiv(nowLocal, 86400);
  int64_t day = floorDiv(resetLocal, 86400);
  if (day == today) {
    snprintf(out, size, "heute %s", clock);
  } else if (day == today + 1) {
    snprintf(out, size, "morgen %s", clock);
  } else {
    // 1970-01-01 was a Thursday.
    int wday = (int)((day % 7 + 7 + 4) % 7);
    snprintf(out, size, "%s %s", days[wday], clock);
  }
}

static void makeColumn(const UsageLimit& limit, bool isSession, const UsageInput& in, UsageColumn& col) {
  col.present = limit.present;
  col.bottomPlain = true;
  col.resetClock[0] = '\0';
  if (!limit.present) {
    col.warn = false;
    col.used = 0;
    col.remaining = 0;
    col.percent = 0;
    col.segments = 0;
    snprintf(col.bottom, sizeof(col.bottom), "keine Daten");
    return;
  }
  col.used = limit.used;
  col.remaining = 100 - limit.used;
  col.warn = col.remaining < SYSOPTV_WARN_BELOW;
  col.percent = in.showUsed ? col.used : col.remaining;
  // Round up, so anything above 0 shows at least one segment.
  col.segments = (col.percent * USAGE_SEGMENTS + 99) / 100;
  int64_t reset = usageResetRounded(limit.resetsAt);
  if (limit.resetsAt != 0) {
    usageFormatClock(reset + in.tz, col.resetClock, sizeof(col.resetClock));
  }
  if (limit.resetsAt == 0 && isSession) {
    // Anthropic sends no reset time while no session is running.
    snprintf(col.bottom, sizeof(col.bottom), "nicht aktiv");
  } else if (limit.resetsAt == 0 || !in.clockValid) {
    snprintf(col.bottom, sizeof(col.bottom), isSession ? "noch -:--" : "--");
  } else if (isSession) {
    // Counted to the rounded reset, so clock plus countdown gives the reset time.
    usageFormatCountdown(reset - in.nowUtc, col.bottom, sizeof(col.bottom));
  } else {
    usageFormatWeekReset(in.nowUtc + in.tz, reset + in.tz, col.bottom, sizeof(col.bottom));
  }
  col.bottomPlain = limit.resetsAt == 0 || !in.clockValid || strncmp(col.bottom, "Reset", 5) == 0;
}

void usageResetLine(const UsageColumn& col, char* out, size_t size) {
  if (col.bottomPlain) {
    snprintf(out, size, "%s", col.bottom);
  } else {
    snprintf(out, size, "Reset %s", col.bottom);
  }
}

void usageMakeView(const UsageInput& in, UsageView& out) {
  memset(&out, 0, sizeof(out));
  out.showUsed = in.showUsed;
  if (in.clockValid) {
    usageFormatClock(in.nowUtc + in.tz, out.clock, sizeof(out.clock));
  } else {
    snprintf(out.clock, sizeof(out.clock), "--:--");
  }
  // The columns always hold the last valid values, the overview page in the
  // browser shows them also while the display shows a special screen.
  makeColumn(in.session, true, in, out.session);
  makeColumn(in.week, false, in, out.week);

  if (in.stale || in.state == USAGE_STATE_ERROR) {
    out.screen = USAGE_SCREEN_NO_CARRIER;
    if (in.hasOk) {
      char clock[6];
      usageFormatClock(in.lastOkUtc + in.tz, clock, sizeof(clock));
      snprintf(out.lastData, sizeof(out.lastData), "letzte Daten %s", clock);
    } else {
      snprintf(out.lastData, sizeof(out.lastData), "noch keine Daten");
    }
    return;
  }
  if (in.state == USAGE_STATE_LOGIN) {
    out.screen = USAGE_SCREEN_LOGIN;
    return;
  }
  out.screen = USAGE_SCREEN_LIMITS;
}
