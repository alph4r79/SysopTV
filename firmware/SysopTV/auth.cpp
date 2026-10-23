// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.

#include "auth.h"
#include "config.h"
#include "settings.h"
#include <bearssl/bearssl_hash.h>
#include <string.h>

static char csrfToken[33];
// SHA-256 of the last Authorization header that passed. Browsers send the
// same header with every request, so the slow password hash only runs once.
static uint8_t acceptedHeader[32];
static bool haveAcceptedHeader = false;

static void toHex(const uint8_t* data, size_t len, char* out) {
  static const char digits[] = "0123456789abcdef";
  for (size_t i = 0; i < len; i++) {
    out[i * 2] = digits[data[i] >> 4];
    out[i * 2 + 1] = digits[data[i] & 0x0F];
  }
  out[len * 2] = '\0';
}

static void randomHex(char* out, size_t bytes) {
  uint8_t buf[32];
  if (bytes > sizeof(buf)) {
    bytes = sizeof(buf);
  }
  ESP.random(buf, bytes);
  toHex(buf, bytes, out);
}

// Compares without stopping at the first difference.
static bool sameBytes(const uint8_t* a, const uint8_t* b, size_t len) {
  uint8_t diff = 0;
  for (size_t i = 0; i < len; i++) {
    diff |= a[i] ^ b[i];
  }
  return diff == 0;
}

static bool sameString(const String& a, const char* b) {
  size_t len = strlen(b);
  if (a.length() != len || len == 0) {
    return false;
  }
  return sameBytes(reinterpret_cast<const uint8_t*>(a.c_str()), reinterpret_cast<const uint8_t*>(b), len);
}

static void sha256(const uint8_t* a, size_t alen, const uint8_t* b, size_t blen,
                   const uint8_t* c, size_t clen, uint8_t* out) {
  br_sha256_context ctx;
  br_sha256_init(&ctx);
  br_sha256_update(&ctx, a, alen);
  if (b) {
    br_sha256_update(&ctx, b, blen);
  }
  if (c) {
    br_sha256_update(&ctx, c, clen);
  }
  br_sha256_out(&ctx, out);
}

// h = SHA256(salt | password), then SYSOPTV_HASH_ROUNDS - 1 times
// h = SHA256(h | salt | password).
static void hashPassword(const uint8_t* salt, const char* password, size_t len, uint8_t* out) {
  const uint8_t* pw = reinterpret_cast<const uint8_t*>(password);
  sha256(salt, 16, pw, len, nullptr, 0, out);
  for (int i = 1; i < SYSOPTV_HASH_ROUNDS; i++) {
    uint8_t prev[32];
    memcpy(prev, out, 32);
    sha256(prev, 32, salt, 16, pw, len, out);
    if ((i & 63) == 0) {
      yield();
    }
  }
}

void authBegin() {
  // The token is made on first use: the hardware random generator of the
  // ESP8266 only delivers real random numbers once the radio is running.
  csrfToken[0] = '\0';
  haveAcceptedHeader = false;
}

bool authHasPassword() {
  return settingsAuth().hasPassword != 0;
}

static int base64Value(char c) {
  if (c >= 'A' && c <= 'Z') return c - 'A';
  if (c >= 'a' && c <= 'z') return c - 'a' + 26;
  if (c >= '0' && c <= '9') return c - '0' + 52;
  if (c == '+') return 62;
  if (c == '/') return 63;
  return -1;
}

// Decodes base64 into out (at most outSize - 1 bytes, zero terminated).
static bool base64Decode(const char* in, char* out, size_t outSize, size_t& outLen) {
  uint32_t acc = 0;
  int bits = 0;
  outLen = 0;
  for (const char* p = in; *p; p++) {
    if (*p == '=') {
      break;
    }
    int v = base64Value(*p);
    if (v < 0) {
      return false;
    }
    acc = (acc << 6) | (uint32_t)v;
    bits += 6;
    if (bits >= 8) {
      bits -= 8;
      if (outLen + 1 >= outSize) {
        return false;
      }
      out[outLen++] = (char)((acc >> bits) & 0xFF);
    }
  }
  out[outLen] = '\0';
  return true;
}

bool authCheckBasic(const String& header) {
  if (!authHasPassword()) {
    return true;
  }
  if (!header.startsWith("Basic ") || header.length() > 200) {
    return false;
  }
  uint8_t headerHash[32];
  sha256(reinterpret_cast<const uint8_t*>(header.c_str()), header.length(), nullptr, 0, nullptr, 0, headerHash);
  if (haveAcceptedHeader && sameBytes(headerHash, acceptedHeader, 32)) {
    return true;
  }

  char decoded[150];
  size_t len = 0;
  if (!base64Decode(header.c_str() + 6, decoded, sizeof(decoded), len)) {
    return false;
  }
  const char* colon = strchr(decoded, ':');
  if (!colon) {
    return false;
  }
  const char* password = colon + 1;
  size_t pwLen = len - (size_t)(password - decoded);
  const StoredAuth& a = settingsAuth();
  uint8_t hash[32];
  hashPassword(a.salt, password, pwLen, hash);
  memset(decoded, 0, sizeof(decoded));
  if (!sameBytes(hash, a.hash, 32)) {
    return false;
  }
  memcpy(acceptedHeader, headerHash, 32);
  haveAcceptedHeader = true;
  return true;
}

bool authSetPassword(const String& password) {
  if (password.length() < SYSOPTV_PASSWORD_MIN || password.length() > SYSOPTV_PASSWORD_MAX) {
    return false;
  }
  StoredAuth& a = settingsAuth();
  StoredAuth before = a;
  ESP.random(a.salt, sizeof(a.salt));
  hashPassword(a.salt, password.c_str(), password.length(), a.hash);
  a.hasPassword = 1;
  if (!settingsSaveAuth()) {
    a = before;
    return false;
  }
  haveAcceptedHeader = false;
  return true;
}

const char* authCsrfToken() {
  if (csrfToken[0] == '\0') {
    randomHex(csrfToken, 16);
  }
  return csrfToken;
}

bool authCsrfValid(const String& token) {
  return sameString(token, authCsrfToken());
}

bool authHasApiKey() {
  return settingsAuth().apiKey[0] != '\0';
}

const char* authApiKey() {
  return settingsAuth().apiKey;
}

bool authNewApiKey() {
  StoredAuth& a = settingsAuth();
  StoredAuth before = a;
  randomHex(a.apiKey, 16);
  if (!settingsSaveAuth()) {
    a = before;
    return false;
  }
  return true;
}

bool authClearApiKey() {
  StoredAuth& a = settingsAuth();
  StoredAuth before = a;
  memset(a.apiKey, 0, sizeof(a.apiKey));
  if (!settingsSaveAuth()) {
    a = before;
    return false;
  }
  return true;
}

bool authApiKeyValid(const String& key) {
  if (!authHasApiKey()) {
    return true;
  }
  return sameString(key, settingsAuth().apiKey);
}
