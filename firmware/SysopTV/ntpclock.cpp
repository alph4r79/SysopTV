// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.

#include "ntpclock.h"
#include "config.h"
#include <Arduino.h>
#include <stdio.h>
#include <time.h>

// Days since 1970-01-01 for a date in the proleptic Gregorian calendar.
// Algorithm from Howard Hinnant, "chrono-compatible low-level date algorithms".
static int64_t daysFromCivil(int year, int month, int day) {
  int64_t y = month <= 2 ? year - 1 : year;
  int64_t era = (y >= 0 ? y : y - 399) / 400;
  int64_t yoe = y - era * 400;
  int64_t mp = month > 2 ? month - 3 : month + 9;
  int64_t doy = (153 * mp + 2) / 5 + day - 1;
  int64_t doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
  return era * 146097 + doe - 719468;
}

// Offset of the local time zone (TZ rules from configTime) at time t.
static int32_t localOffset(time_t t) {
  struct tm lt;
  localtime_r(&t, &lt);
  int64_t local = daysFromCivil(lt.tm_year + 1900, lt.tm_mon + 1, lt.tm_mday) * 86400 +
                  lt.tm_hour * 3600 + lt.tm_min * 60 + lt.tm_sec;
  return (int32_t)(local - (int64_t)t);
}

bool clockSynced() {
  return time(nullptr) > (time_t)SYSOPTV_TIME_VALID_AFTER;
}

bool clockNow(int64_t& utc, int32_t& tz) {
  if (!clockSynced()) {
    return false;
  }
  time_t t = time(nullptr);
  utc = t;
  tz = localOffset(t);
  return true;
}

void clockFormat(int64_t utc, int32_t tz, char* out, size_t size) {
  if (utc <= 0) {
    snprintf(out, size, "--:--");
    return;
  }
  int64_t local = utc + tz;
  int64_t daySec = ((local % 86400) + 86400) % 86400;
  snprintf(out, size, "%02d:%02d", (int)(daySec / 3600), (int)(daySec / 60 % 60));
}
