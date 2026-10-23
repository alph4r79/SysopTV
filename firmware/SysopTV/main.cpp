// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Entry point. Besides starting display and network this file holds the crash
// guard: if the firmware crashes several times in a row, SysopTV starts in
// rescue mode, which only offers WLAN and the update page. It also runs the
// password reset by short power cycles, the backlight with night mode and
// hands the usage data to the selected design.

#include <Arduino.h>
#include "config.h"
#include "screen.h"
#include "net.h"
#include "usage.h"
#include "settings.h"
#include "design.h"
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

// Hands the usage data to the design. Rescue mode stays on the status screen.
static void updateView() {
  if (rescueMode) {
    return;
  }
  static uint8_t shownDesign = 0xFF;
  uint8_t id = designUpdate();
  const Design& design = designGet(id);
  if (!netScreenFree() || !usageScreenActive()) {
    viewOnScreen = false;
    return;
  }
  // The texts change once a minute at most, checking twice a second is plenty.
  if (viewOnScreen && id == shownDesign && millis() - lastViewUpdate < 500) {
    return;
  }
  lastViewUpdate = millis();
  if (!viewOnScreen || id != shownDesign) {
    // Something else drew over the screen, or the design changed: draw all once.
    design.invalidate();
    viewOnScreen = true;
    shownDesign = id;
  }
  UsageView view;
  usageBuildView(view);
  netIpText(view.ip, sizeof(view.ip));
  design.show(view);
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
  if (s.nightEnabled && usageTime(utc, tz)) {
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
