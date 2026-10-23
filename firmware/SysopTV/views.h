// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Entry points of the designs, one pair per view_<name>.cpp.

#pragma once
#include "usage_data.h"

void saeulenInvalidate();
void saeulenShow(const UsageView& view);

void phosphorInvalidate();
void phosphorShow(const UsageView& view);

void dosInvalidate();
void dosShow(const UsageView& view);

void ansiInvalidate();
void ansiShow(const UsageView& view);

void amberInvalidate();
void amberShow(const UsageView& view);

void klassikInvalidate();
void klassikShow(const UsageView& view);
