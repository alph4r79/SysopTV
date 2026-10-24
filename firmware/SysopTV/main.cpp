// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Entry point. Besides starting display and network this file holds the crash
// guard: if the firmware crashes several times in a row, SysopTV starts in
// rescue mode, which only offers WLAN and the update page. It also runs the
// password reset by short power cycles, the backlight with night mode and
// the NO CARRIER screen whenever the PC sends no pictures.

#include <Arduino.h>
#include "config.h"
#include "screen.h"
#include "net.h"
#include "ntpclock.h"
#include "settings.h"
#include "frame.h"
#include "auth.h"
#include "store.h"

extern "C" {
#include <user_interface.h>
}

struct CrashGuard {
  uint32_t magic;
  uint32_t crashes;
};

static const uint32_t GUARD_MAGIC = 0x56545953;  // "SYTV"

static bool rescueMode = false;
static bool guardCleared = false;
static bool bootStable = false;
static unsigned long lastViewUpdate = 0;
static bool viewOnScreen = false;
static unsigned long lastLightUpdate = 0;

static void writeGuard(uint32_t crashes) {
  CrashGuard g;
  g.magic = GUARD_MAGIC;
  g.crashes = crashes;
  ESP.rtcUserMemoryWrite(SYSOPTV_GUARD_RTC_OFFSET, reinterpret_cast<uint32_t*>(&g), sizeof(g));
}

static void checkGuard() {
  CrashGuard g;
  ESP.rtcUserMemoryRead(SYSOPTV_GUARD_RTC_OFFSET, reinterpret_cast<uint32_t*>(&g), sizeof(g));
  if (g.magic != GUARD_MAGIC) {
    g.crashes = 0;
  }
  const rst_info* info = ESP.getResetInfoPtr();
  bool crashed = info->reason == REASON_EXCEPTION_RST ||
                 info->reason == REASON_SOFT_WDT_RST ||
                 info->reason == REASON_WDT_RST;
  uint32_t crashes = crashed ? g.crashes + 1 : 0;
  rescueMode = crashes >= SYSOPTV_CRASHES_FOR_RESCUE;
  writeGuard(crashes);
}

// Password reset: SYSOPTV_RESET_SHORT_BOOTS power-on boots in a row that
// were cut off within SYSOPTV_RESET_STABLE_MS. Crashes and software restarts
// do not count, so a crash loop cannot wipe the password.
static void checkPasswordReset() {
  uint32_t reason = ESP.getResetInfoPtr()->reason;
  bool powerOn = reason == REASON_DEFAULT_RST || reason == REASON_EXT_SYS_RST;
  uint8_t shortBoots = storeBootBegin(powerOn);
  if (powerOn && shortBoots >= SYSOPTV_RESET_SHORT_BOOTS) {
    settingsResetAll();
    screenResetDone();
    delay(SYSOPTV_RESET_MESSAGE_MS);
  }
}

void setup() {
  Serial.begin(115200);
  checkGuard();
  screenBegin();
  settingsBegin(rescueMode);
  checkPasswordReset();
  authBegin();
  screenBoot(rescueMode);
  delay(SYSOPTV_BOOT_SCREEN_MS);
  netBegin(rescueMode);
}

// True once the "Ready" screen should make way for NO CARRIER although no
// picture arrived yet. Kept as a flag, so a millis() overflow cannot bring
// the "Ready" screen back.
static bool readyTimedOut() {
  static bool timedOut = false;
  if (!timedOut && millis() >= SYSOPTV_NO_PC_AFTER_MS) {
    timedOut = true;
  }
  return timedOut;
}

// Text under NO CARRIER: when the last picture came.
static void lastPictureText(bool haveTime, int64_t utc, int32_t tz, char* out, size_t size) {
  if (!frameSeen()) {
    snprintf(out, size, "noch kein Bild");
    return;
  }
  uint32_t age = frameAgeSeconds();
  if (haveTime) {
    char hhmm[6];
    clockFormat(utc - (int64_t)age, tz, hhmm, sizeof(hhmm));
    snprintf(out, size, "letztes Bild %s", hhmm);
  } else if (age < 3600) {
    snprintf(out, size, "letztes Bild vor %u min", (unsigned)(age / 60));
  } else {
    snprintf(out, size, "letztes Bild vor %u h", (unsigned)(age / 3600));
  }
}

// The PC draws everything. Without pictures the firmware shows NO CARRIER
// with the clock. Rescue mode stays on the status screen.
static void updateView() {
  if (rescueMode) {
    return;
  }
  if (!netScreenFree() || frameModeActive() || !(frameSeen() || frameEnded() || readyTimedOut())) {
    viewOnScreen = false;
    return;
  }
  // The texts change once a minute at most, checking twice a second is plenty.
  if (viewOnScreen && millis() - lastViewUpdate < 500) {
    return;
  }
  lastViewUpdate = millis();
  bool full = !viewOnScreen;
  viewOnScreen = true;
  int64_t utc = 0;
  int32_t tz = 0;
  bool haveTime = clockNow(utc, tz);
  char clock[6];
  char last[32];
  char ip[16];
  clockFormat(haveTime ? utc : 0, tz, clock, sizeof(clock));
  lastPictureText(haveTime, utc, tz, last, sizeof(last));
  netIpText(ip, sizeof(ip));
  screenNoCarrier(full, clock, last, ip);
}

static bool inWindow(uint16_t minute, uint16_t from, uint16_t to) {
  if (from == to) {
    return false;
  }
  if (from < to) {
    return minute >= from && minute < to;
  }
  return minute >= from || minute < to;  // over midnight
}

// Day or night brightness, once a second.
static void updateBrightness() {
  if (millis() - lastLightUpdate < 1000) {
    return;
  }
  lastLightUpdate = millis();
  const Settings& s = settingsGet();
  uint8_t level = s.brightness;
  int64_t utc;
  int32_t tz;
  if (s.nightEnabled && clockNow(utc, tz)) {
    int64_t local = utc + tz;
    int64_t daySec = ((local % 86400) + 86400) % 86400;
    if (inWindow((uint16_t)(daySec / 60), s.nightFrom, s.nightTo)) {
      level = s.nightBrightness;
    }
  }
  screenBrightness(level);
}

void loop() {
  netLoop();
  updateView();
  updateBrightness();
  if (!bootStable && millis() > SYSOPTV_RESET_STABLE_MS) {
    bootStable = true;
    storeBootStable();
  }
  if (!guardCleared && millis() > SYSOPTV_STABLE_AFTER_MS) {
    guardCleared = true;
    writeGuard(0);
  }
}
