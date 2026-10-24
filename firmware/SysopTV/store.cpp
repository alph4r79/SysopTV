// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// The sector is handled directly instead of through the EEPROM library, so
// the boot log can be appended without erasing the sector on every boot.
// Flash can only turn bits from 1 to 0 without an erase: a log entry is one
// erased word (0xFFFFFFFF) that gets overwritten once.

#include "store.h"
#include <Arduino.h>
#include <string.h>

extern "C" uint32_t _EEPROM_start;

static const uint32_t SECTOR_SIZE = 4096;
static const uint32_t RECORD_SIZE = 192;  // multiple of 4, room for later fields
static const uint32_t LOG_OFFSET = 2048;
static const uint32_t LOG_WORDS = (SECTOR_SIZE - LOG_OFFSET) / 4;

static const uint32_t RECORD_MAGIC = 0x53545953;  // "SYTS"
static const uint16_t RECORD_VERSION = 2;

static const uint32_t LOG_FREE = 0xFFFFFFFFUL;
static const uint32_t LOG_BOOT = 0x0B0B0B0BUL;
static const uint32_t LOG_STABLE = 0x00000000UL;

struct Record {
  uint32_t magic;
  uint16_t version;
  uint16_t length;
  StoredSettings settings;
  StoredAuth auth;
};
static_assert(sizeof(Record) + 4 <= RECORD_SIZE, "record too large");

// Record of 0.3.x: 128 bytes, settings without device name. Read once and
// written back as version 2 with the next save, so password and settings
// survive the update.
static const uint32_t RECORD_V1_SIZE = 128;
struct SettingsV1 {
  uint8_t unused[3];        // design settings of 0.3.x, no longer used
  uint8_t brightness;
  uint8_t nightEnabled;
  uint8_t nightBrightness;
  uint16_t nightFrom;
  uint16_t nightTo;
};
struct RecordV1 {
  uint32_t magic;
  uint16_t version;
  uint16_t length;
  SettingsV1 settings;
  StoredAuth auth;
};

static bool bootCounted = false;
static uint32_t nextLogWord = 0;

static uint32_t sectorAddress() {
  return (uint32_t)&_EEPROM_start - 0x40200000UL;
}

static uint32_t crc32(const uint8_t* data, size_t len) {
  uint32_t crc = 0xFFFFFFFFUL;
  for (size_t i = 0; i < len; i++) {
    crc ^= data[i];
    for (int b = 0; b < 8; b++) {
      crc = (crc >> 1) ^ (0xEDB88320UL & (0UL - (crc & 1)));
    }
  }
  return ~crc;
}

// The record as it is stored: Record, zero padding, CRC32 in the last 4 bytes.
static void buildRecord(uint32_t* buf, const StoredSettings& settings, const StoredAuth& auth) {
  memset(buf, 0, RECORD_SIZE);
  Record r;
  memset(&r, 0, sizeof(r));
  r.magic = RECORD_MAGIC;
  r.version = RECORD_VERSION;
  r.length = sizeof(Record);
  r.settings = settings;
  r.auth = auth;
  memcpy(buf, &r, sizeof(r));
  uint8_t* bytes = reinterpret_cast<uint8_t*>(buf);
  uint32_t crc = crc32(bytes, RECORD_SIZE - 4);
  memcpy(bytes + RECORD_SIZE - 4, &crc, 4);
}

static bool readRecord(uint32_t* buf) {
  return ESP.flashRead(sectorAddress(), buf, RECORD_SIZE);
}

static bool crcOk(const uint32_t* buf, uint32_t size) {
  const uint8_t* bytes = reinterpret_cast<const uint8_t*>(buf);
  uint32_t crc;
  memcpy(&crc, bytes + size - 4, 4);
  return crc == crc32(bytes, size - 4);
}

static bool recordValid(const uint32_t* buf) {
  if (!crcOk(buf, RECORD_SIZE)) {
    return false;
  }
  Record r;
  memcpy(&r, buf, sizeof(r));
  return r.magic == RECORD_MAGIC && r.version == RECORD_VERSION && r.length == sizeof(Record);
}

static bool recordV1Valid(const uint32_t* buf) {
  if (!crcOk(buf, RECORD_V1_SIZE)) {
    return false;
  }
  RecordV1 r;
  memcpy(&r, buf, sizeof(r));
  return r.magic == RECORD_MAGIC && r.version == 1 && r.length == sizeof(RecordV1);
}

bool storeLoad(StoredSettings& settings, StoredAuth& auth) {
  uint32_t buf[RECORD_SIZE / 4];
  if (!readRecord(buf)) {
    return false;
  }
  if (recordValid(buf)) {
    Record r;
    memcpy(&r, buf, sizeof(r));
    settings = r.settings;
    auth = r.auth;
  } else if (recordV1Valid(buf)) {
    RecordV1 r;
    memcpy(&r, buf, sizeof(r));
    memset(&settings, 0, sizeof(settings));
    settings.brightness = r.settings.brightness;
    settings.nightEnabled = r.settings.nightEnabled;
    settings.nightBrightness = r.settings.nightBrightness;
    settings.nightFrom = r.settings.nightFrom;
    settings.nightTo = r.settings.nightTo;
    auth = r.auth;
  } else {
    return false;
  }
  settings.deviceName[sizeof(settings.deviceName) - 1] = '\0';
  auth.apiKey[sizeof(auth.apiKey) - 1] = '\0';
  return true;
}

static bool writeRecordAfterErase(const uint32_t* buf) {
  if (!ESP.flashEraseSector(sectorAddress() / SECTOR_SIZE)) {
    return false;
  }
  nextLogWord = 0;
  return ESP.flashWrite(sectorAddress(), buf, RECORD_SIZE);
}

bool storeSave(const StoredSettings& settings, const StoredAuth& auth) {
  uint32_t fresh[RECORD_SIZE / 4];
  uint32_t current[RECORD_SIZE / 4];
  buildRecord(fresh, settings, auth);
  if (readRecord(current) && memcmp(fresh, current, RECORD_SIZE) == 0) {
    return true;
  }
  // The erase also clears the boot log. That is fine: whoever saves
  // settings has been running for a while, which counts as a stable boot.
  return writeRecordAfterErase(fresh);
}

void storeErase() {
  ESP.flashEraseSector(sectorAddress() / SECTOR_SIZE);
  nextLogWord = 0;
}

static bool appendLog(uint32_t value) {
  if (nextLogWord >= LOG_WORDS) {
    return false;
  }
  uint32_t word = value;
  bool ok = ESP.flashWrite(sectorAddress() + LOG_OFFSET + nextLogWord * 4, &word, 4);
  nextLogWord++;
  return ok;
}

uint8_t storeBootBegin(bool countThisBoot) {
  // Find the first free entry and count the BOOT entries right before it.
  uint32_t chunk[64];
  uint32_t used = LOG_WORDS;
  uint32_t shortBoots = 0;
  bool found = false;
  for (uint32_t base = 0; base < LOG_WORDS && !found; base += 64) {
    if (!ESP.flashRead(sectorAddress() + LOG_OFFSET + base * 4, chunk, sizeof(chunk))) {
      return 0;
    }
    for (uint32_t i = 0; i < 64; i++) {
      if (chunk[i] == LOG_FREE) {
        used = base + i;
        found = true;
        break;
      }
      // Anything else than a complete BOOT entry, also a half written one,
      // ends the series. That errs on the side of not resetting.
      shortBoots = chunk[i] == LOG_BOOT ? shortBoots + 1 : 0;
    }
  }
  nextLogWord = used;

  if (countThisBoot) {
    if (nextLogWord >= LOG_WORDS) {
      // Log is full, which happens about every 250 boots, or the sector still
      // holds old data from before SysopTV. Rewrite it with the record (if it
      // is valid) and the current series of short boots.
      uint32_t record[RECORD_SIZE / 4];
      bool haveRecord = readRecord(record) && (recordValid(record) || recordV1Valid(record));
      ESP.flashEraseSector(sectorAddress() / SECTOR_SIZE);
      nextLogWord = 0;
      if (haveRecord) {
        ESP.flashWrite(sectorAddress(), record, RECORD_SIZE);
      }
      for (uint32_t i = 0; i < shortBoots && i < 8; i++) {
        appendLog(LOG_BOOT);
      }
    }
    bootCounted = appendLog(LOG_BOOT);
  }
  return shortBoots > 255 ? 255 : (uint8_t)shortBoots;
}

void storeBootStable() {
  if (bootCounted) {
    bootCounted = false;
    appendLog(LOG_STABLE);
  }
}
