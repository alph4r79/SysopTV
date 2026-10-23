// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// User settings and login data, kept together in one record in store.cpp.
// In rescue mode the settings are ignored (defaults), the login data is
// still loaded so /update keeps its password.

#pragma once
#include <stdint.h>
#include "store.h"

using Settings = StoredSettings;

// Loads the record. Damaged or missing data means defaults, never a crash.
void settingsBegin(bool rescue);

const Settings& settingsGet();
Settings settingsDefaults();

// Checks and corrects every field, so a value from a form can be passed directly.
void settingsSanitize(Settings& s);

// Takes new settings and writes them only if something changed.
// Returns false if writing failed. Not available in rescue mode.
bool settingsUpdate(const Settings& s);

// Login data for auth.cpp. Change it, then call settingsSaveAuth().
StoredAuth& settingsAuth();
bool settingsSaveAuth();

// Password reset: password, API key and settings back to defaults, WLAN stays.
void settingsResetAll();
