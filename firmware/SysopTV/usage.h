// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Keeps the last usage data from the PC and knows the current time.
// Time from the sender has priority, NTP is the fallback when the PC is off.

#pragma once
#include "usage_data.h"

// Stores a parsed packet and sets the clock from its "now" and "tz".
void usageApply(const UsagePacket& packet);

// True once any valid packet has arrived since boot.
bool usageHasPacket();

// True once the design should own the screen: a packet has arrived, or
// SYSOPTV_NO_PC_AFTER_MS have passed since boot without one.
bool usageScreenActive();

// Current time in UTC and offset to local time. Returns false if neither the
// sender nor NTP has delivered a time yet.
bool usageTime(int64_t& utc, int32_t& tz);

// Builds what the screen should show right now.
void usageBuildView(UsageView& out);

// State of the data for the overview page.
struct UsageStatus {
  bool havePacket;      // any valid packet since boot
  bool stale;           // no packet for SYSOPTV_USAGE_STALE_MS
  UsageState state;     // state of the last packet
  uint32_t ageSeconds;  // seconds since the last packet
};
void usageGetStatus(UsageStatus& out);

enum TimeSource : uint8_t { TIME_NONE, TIME_SENDER, TIME_NTP, TIME_SENDER_OLD };
// Where usageTime() takes the time from right now.
TimeSource usageTimeSource();
