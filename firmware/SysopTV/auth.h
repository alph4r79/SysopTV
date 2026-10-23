// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Admin password, CSRF token and the optional API key for /api/usage.
// The password is only stored as salted, iterated SHA-256.

#pragma once
#include <Arduino.h>

// Creates the CSRF token for this boot. Call after settingsBegin().
void authBegin();

bool authHasPassword();

// Checks the value of an "Authorization: Basic ..." header. The user name is ignored.
bool authCheckBasic(const String& header);

// Sets a new password (8 to 64 characters) with a new salt and saves it.
bool authSetPassword(const String& password);

// Token that every form sends back with a POST.
const char* authCsrfToken();
bool authCsrfValid(const String& token);

bool authHasApiKey();
const char* authApiKey();
bool authNewApiKey();
bool authClearApiKey();
bool authApiKeyValid(const String& key);
