// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.

#include "settings.h"
#include "config.h"
#include <Arduino.h>
#include <stdio.h>
#include <string.h>

static Settings settings;
static StoredAuth auth;
static bool rescueMode = false;

Settings settingsDefaults() {
  Settings s;
  memset(&s, 0, sizeof(s));
  s.brightness = 100;
  s.nightEnabled = 0;
  s.nightBrightness = 10;
  s.nightFrom = 22 * 60;
  s.nightTo = 7 * 60;
  snprintf(s.deviceName, sizeof(s.deviceName), SYSOPTV_NAME_PREFIX "%s", settingsChipSuffix());
  return s;
}

const char* settingsChipSuffix() {
  static char suffix[5] = "";
  if (!suffix[0]) {
    snprintf(suffix, sizeof(suffix), "%04x", (unsigned)(ESP.getChipId() & 0xFFFF));
  }
  return suffix;
}

bool settingsNameValid(const char* name) {
  size_t len = strlen(name);
  if (len < 1 || len > SYSOPTV_NAME_MAX) {
    return false;
  }
  for (size_t i = 0; i < len; i++) {
    char c = name[i];
    bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-';
    if (!ok) {
      return false;
    }
  }
  return true;
}

void settingsSanitize(Settings& s) {
  Settings d = settingsDefaults();
  // Old design settings from 0.4.x must not matter any more.
  memset(s.unused, 0, sizeof(s.unused));
  if (s.brightness < 10) {
    s.brightness = 10;
  } else if (s.brightness > 100) {
    s.brightness = 100;
  }
  s.nightEnabled = s.nightEnabled ? 1 : 0;
  if (s.nightBrightness > 100) {
    s.nightBrightness = 100;
  }
  if (s.nightFrom >= 24 * 60) {
    s.nightFrom = d.nightFrom;
  }
  if (s.nightTo >= 24 * 60) {
    s.nightTo = d.nightTo;
  }
  s.deviceName[sizeof(s.deviceName) - 1] = '\0';
  if (!settingsNameValid(s.deviceName)) {
    memcpy(s.deviceName, d.deviceName, sizeof(s.deviceName));
  }
}

static void sanitizeAuth(StoredAuth& a) {
  a.hasPassword = a.hasPassword ? 1 : 0;
  a.apiKey[sizeof(a.apiKey) - 1] = '\0';
  size_t len = strlen(a.apiKey);
  bool hex = len == 32;
  for (size_t i = 0; i < len && hex; i++) {
    char c = a.apiKey[i];
    hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
  }
  if (!hex) {
    memset(a.apiKey, 0, sizeof(a.apiKey));
  }
}

void settingsBegin(bool rescue) {
  rescueMode = rescue;
  settings = settingsDefaults();
  memset(&auth, 0, sizeof(auth));
  Settings loaded;
  StoredAuth loadedAuth;
  if (storeLoad(loaded, loadedAuth)) {
    sanitizeAuth(loadedAuth);
    auth = loadedAuth;
    if (!rescue) {
      settingsSanitize(loaded);
      settings = loaded;
    }
  }
}

const Settings& settingsGet() {
  return settings;
}

bool settingsUpdate(const Settings& s) {
  if (rescueMode) {
    return false;
  }
  Settings clean = s;
  settingsSanitize(clean);
  settings = clean;
  return storeSave(settings, auth);
}

StoredAuth& settingsAuth() {
  return auth;
}

bool settingsSaveAuth() {
  if (rescueMode) {
    return false;
  }
  return storeSave(settings, auth);
}

void settingsResetAll() {
  storeErase();
  settings = settingsDefaults();
  memset(&auth, 0, sizeof(auth));
}
