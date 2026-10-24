// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Local time from NTP (configured in net.cpp with the Europe/Berlin rules).
// Used for the clock on the NO CARRIER screen and for the night mode.

#pragma once
#include <stddef.h>
#include <stdint.h>

// True once NTP has delivered a time.
bool clockSynced();

// Current time in UTC and offset to local time in seconds. Returns false
// while the clock is not set.
bool clockNow(int64_t& utc, int32_t& tz);

// Local time "HH:MM" of utc, or "--:--" if utc is 0.
void clockFormat(int64_t utc, int32_t tz, char* out, size_t size);
