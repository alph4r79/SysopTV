// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.

#pragma once
#include <stdint.h>

#define SYSOPTV_VERSION "0.5.0"
// Default device name and hotspot name get the last 4 hex digits of the chip
// ID appended, so several cubes in one network have different names.
#define SYSOPTV_NAME_PREFIX "sysoptv-"
#define SYSOPTV_AP_PREFIX "SysopTV-Setup-"
// Device name: letters, digits and hyphen.
#define SYSOPTV_NAME_MAX 24

// How long the boot screen stays visible.
#define SYSOPTV_BOOT_SCREEN_MS 2000UL

// How long to try the saved WLAN at boot before opening the setup hotspot.
#define SYSOPTV_CONNECT_TIMEOUT_MS 20000UL

// Setup hotspot: restart and retry the saved WLAN if nobody connects for this long.
#define SYSOPTV_AP_IDLE_RESTART_MS 180000UL

// Crash guard: this many crash resets in a row switch to rescue mode.
#define SYSOPTV_CRASHES_FOR_RESCUE 3
// Uptime after which a boot counts as stable and the crash counter is cleared.
#define SYSOPTV_STABLE_AFTER_MS 60000UL
// Where the crash counter lives in RTC user memory, in 4-byte blocks. The first
// 128 bytes (blocks 0 to 31) hold the eboot command for a pending OTA update and
// must not be touched, so the guard sits right behind them.
#define SYSOPTV_GUARD_RTC_OFFSET 32

// After a failed update: show the error, then restart after this long.
#define SYSOPTV_UPDATE_FAIL_RESTART_MS 8000UL

// POST /api/settings, larger bodies are rejected.
#define SYSOPTV_SETTINGS_MAX_BODY 1024
// Picture mode (POST /api/frame): without a new picture for this long the
// cube shows NO CARRIER with the clock.
#define SYSOPTV_FRAME_TIMEOUT_MS 180000UL
// Longest name of a picture source from the header X-SysopTV-Source, in bytes.
#define SYSOPTV_FRAME_SOURCE_MAX 32
// If no picture at all arrives this long after boot, the "Ready" screen
// makes way for NO CARRIER (clock and IP address).
#define SYSOPTV_NO_PC_AFTER_MS 120000UL

// Clock from NTP for NO CARRIER and the night mode.
#define SYSOPTV_NTP_SERVER "pool.ntp.org"
#define SYSOPTV_TZ "CET-1CEST,M3.5.0,M10.5.0/3"
// Unix times before this count as "clock not set" (2020-01-01).
#define SYSOPTV_TIME_VALID_AFTER 1577836800UL

// Admin password.
#define SYSOPTV_PASSWORD_MIN 8
#define SYSOPTV_PASSWORD_MAX 64
// SHA-256 rounds for the password hash. About 50 ms on the ESP8266, and the
// result for a browser login is cached, so it only runs once per login.
#define SYSOPTV_HASH_ROUNDS 1000

// Password reset: this many power-on boots in a row that end before
// SYSOPTV_RESET_STABLE_MS reset password and settings (WLAN stays).
#define SYSOPTV_RESET_SHORT_BOOTS 3
#define SYSOPTV_RESET_STABLE_MS 10000UL
#define SYSOPTV_RESET_MESSAGE_MS 3000UL

// Filesystem area of the original GeekMagic firmware (layout 4M3M), offered
// read-only as /flashbackup.
#define SYSOPTV_BACKUP_START 0x100000UL
#define SYSOPTV_BACKUP_END 0x3FA000UL

// ---------------------------------------------------------------------------
// Colours of the screens the firmware draws itself, as RGB565. This panel
// has a strong green cast and shows dark tones much too bright: amber needs
// much less green than on a monitor, dark shades must be very dark.
// ---------------------------------------------------------------------------

constexpr uint16_t sysoptvRgb(uint8_t r, uint8_t g, uint8_t b) {
  return (uint16_t)(((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3));
}

constexpr uint16_t AMBER_MAIN = sysoptvRgb(255, 110, 0);
constexpr uint16_t AMBER_DIM = sysoptvRgb(140, 55, 0);
constexpr uint16_t AMBER_FRAME = sysoptvRgb(110, 42, 0);
constexpr uint16_t AMBER_WARN = sysoptvRgb(255, 40, 16);
