// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.

#pragma once
#include <stdint.h>

#define SYSOPTV_VERSION "0.3.2"
#define SYSOPTV_HOSTNAME "sysoptv"
#define SYSOPTV_AP_NAME "SysopTV-Setup"

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

// Usage data from the PC (POST /api/usage).
// Larger bodies are rejected. A full packet is about 150 bytes.
#define SYSOPTV_USAGE_MAX_BODY 512
// Without any packet for this long the screen shows NO CARRIER.
#define SYSOPTV_USAGE_STALE_MS 600000UL
// If no packet at all arrives this long after boot, the "Ready" screen
// makes way for the display without PC (clock, NO CARRIER, IP address).
#define SYSOPTV_NO_PC_AFTER_MS 120000UL
// A column turns to the warning colour below this many percent left.
#define SYSOPTV_WARN_BELOW 20

// Time: the sender has priority, NTP is the fallback. The sender time counts
// as recent for this long after the last packet.
#define SYSOPTV_SENDER_TIME_MS 900000UL
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
// Colours of the designs, as RGB565. This panel has a strong green cast and
// shows dark tones much too bright: greens need little blue, amber needs much
// less green than on a monitor, dark shades must be very dark.
// ---------------------------------------------------------------------------

constexpr uint16_t sysoptvRgb(uint8_t r, uint8_t g, uint8_t b) {
  return (uint16_t)(((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3));
}
constexpr uint16_t SYSOPTV_BLACK = 0x0000;

// 1 "Saeulen"
constexpr uint16_t SAEULEN_MAIN = sysoptvRgb(70, 240, 0);     // numbers, filled segments
constexpr uint16_t SAEULEN_DIM = sysoptvRgb(30, 120, 0);      // secondary texts
constexpr uint16_t SAEULEN_OUTLINE = sysoptvRgb(24, 96, 0);   // outline of empty segments
constexpr uint16_t SAEULEN_LINE = sysoptvRgb(0, 40, 0);       // line under the header
constexpr uint16_t SAEULEN_WARN = sysoptvRgb(255, 40, 16);

// 2 "Phosphor" (draft A)
constexpr uint16_t PHOSPHOR_MAIN = sysoptvRgb(70, 240, 0);
constexpr uint16_t PHOSPHOR_DIM = sysoptvRgb(30, 110, 0);
constexpr uint16_t PHOSPHOR_SHADE = sysoptvRgb(20, 80, 0);    // dotted empty part of a column
constexpr uint16_t PHOSPHOR_SCAN = sysoptvRgb(30, 130, 0);    // scan lines in the filled part
constexpr uint16_t PHOSPHOR_WARN = sysoptvRgb(255, 110, 0);   // amber, as in the draft
constexpr uint16_t PHOSPHOR_WARN_SCAN = sysoptvRgb(140, 50, 0);

// 3 "DOS" (draft B)
constexpr uint16_t DOS_BLUE = sysoptvRgb(0, 0, 168);
constexpr uint16_t DOS_CYAN = sysoptvRgb(40, 220, 230);       // menu bar, bottom texts
constexpr uint16_t DOS_WHITE = sysoptvRgb(230, 230, 230);     // frames, titles
constexpr uint16_t DOS_GRAY = sysoptvRgb(160, 160, 190);      // label under the number
constexpr uint16_t DOS_YELLOW = sysoptvRgb(255, 225, 30);     // number, filled bars
constexpr uint16_t DOS_EMPTY = sysoptvRgb(0, 0, 80);          // empty bars
constexpr uint16_t DOS_WARN = sysoptvRgb(255, 70, 60);

// 4 "ANSI" (draft C)
constexpr uint16_t ANSI_MAGENTA = sysoptvRgb(220, 40, 220);
constexpr uint16_t ANSI_CYAN = sysoptvRgb(40, 230, 240);
constexpr uint16_t ANSI_CYAN_DIM = sysoptvRgb(20, 110, 120);
constexpr uint16_t ANSI_YELLOW = sysoptvRgb(255, 225, 30);
constexpr uint16_t ANSI_YELLOW_DIM = sysoptvRgb(120, 100, 0);
constexpr uint16_t ANSI_RED = sysoptvRgb(255, 60, 50);
constexpr uint16_t ANSI_RED_DIM = sysoptvRgb(120, 20, 10);
constexpr uint16_t ANSI_WHITE = sysoptvRgb(210, 210, 210);
constexpr uint16_t ANSI_GRAY = sysoptvRgb(100, 100, 100);
constexpr uint16_t ANSI_DOT = sysoptvRgb(50, 50, 50);         // dotted rest of a bar

// 5 "Bernstein" (draft D)
constexpr uint16_t AMBER_MAIN = sysoptvRgb(255, 110, 0);
constexpr uint16_t AMBER_DIM = sysoptvRgb(140, 55, 0);
constexpr uint16_t AMBER_FRAME = sysoptvRgb(110, 42, 0);
constexpr uint16_t AMBER_BAR = sysoptvRgb(120, 46, 0);        // status bar at the bottom
constexpr uint16_t AMBER_WARN = sysoptvRgb(255, 40, 16);
