// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Permanent storage in the 4 KB EEPROM sector of the flash (0x3FB000).
// The original GeekMagic firmware keeps its web interface in the filesystem
// area 0x100000 to 0x3FA000 and does not use this sector, so SysopTV never
// writes anywhere near the data of the original.
//
// Layout of the sector:
//   0 to 2047      one record with settings and login data, with CRC32
//                  (version 2 since 0.4.0, version 1 records are taken over)
//   2048 to 4095   boot log for the password reset, see storeBootBegin()

#pragma once
#include <stdint.h>
#include <stddef.h>

struct StoredSettings {
  uint8_t design;
  uint8_t cycleSeconds;     // 0, 30 or 60
  uint8_t showUsed;         // 1: show usage, 0: show what is left
  uint8_t brightness;       // 10 to 100 percent
  uint8_t nightEnabled;
  uint8_t nightBrightness;  // 0 to 100 percent, 0 switches the display off
  uint16_t nightFrom;       // minutes after midnight
  uint16_t nightTo;
  char deviceName[25];      // mDNS name without ".local", empty means default
};

struct StoredAuth {
  uint8_t hasPassword;
  uint8_t salt[16];
  uint8_t hash[32];
  char apiKey[33];          // 32 hex characters or empty
};

// Reads the record. Returns false if the sector is empty or the record is
// damaged, the outputs are then left untouched.
bool storeLoad(StoredSettings& settings, StoredAuth& auth);

// Writes the record, but only if it differs from what is in the flash.
// Returns false if writing failed.
bool storeSave(const StoredSettings& settings, const StoredAuth& auth);

// Erases the whole sector: settings and password are gone, WLAN stays.
void storeErase();

// Boot log for the password reset. Call once at boot. Counts the boots in a
// row that ended before storeBootStable() was called. Only power-on and reset
// pin boots are counted, crashes and software restarts are not.
// Returns the number of such short boots before this one.
uint8_t storeBootBegin(bool countThisBoot);

// Call after SYSOPTV_RESET_STABLE_MS of uptime.
void storeBootStable();
