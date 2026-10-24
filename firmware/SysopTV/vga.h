// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Text in the embedded VGA font (Px437 IBM VGA 8x16, UTF-8, with umlauts).

#pragma once
#include <stdint.h>

enum TextAlign : uint8_t { ALIGN_LEFT, ALIGN_CENTER, ALIGN_RIGHT };

// Width in pixels of s at an integer scale.
int vgaWidth(const char* s, uint8_t scale);

// Draws s scaled by an integer factor. y is the top edge. clearWidth > 0
// clears that width (aligned like the text) with bg first, without flicker.
void vgaText(const char* s, int x, int y, TextAlign align, uint8_t scale, uint16_t fg, uint16_t bg,
             int clearWidth = 0);
