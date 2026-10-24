// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Picture mode: decodes a picture in the format "SYF1" (docs/frame-format.md)
// while it is being received and draws it line by line. At most one line of
// 240 pixels is buffered.
//
// Format, all numbers little endian:
//   header  12 bytes: "SYF1", x, y, width, height (uint16 each)
//   pixels  width * height pixels, row by row, as RLE packets:
//           control byte c
//             c & 0x80 set:   run, (c & 0x7F) + 1 pixels (1 to 128) of one
//                             colour, followed by 1 colour (2 bytes RGB565)
//             c & 0x80 clear: literal, c + 1 pixels (1 to 128) follow, each
//                             2 bytes RGB565
//           Packets may span lines. Exactly width * height pixels, no
//           extra bytes after the last pixel.

#pragma once
#include <stddef.h>
#include <stdint.h>

// Starts decoding a new picture.
void frameStart();

// Feeds received bytes. Returns false on an error, see frameError().
bool frameFeed(const uint8_t* data, size_t len);

// Call after the last byte. Returns true if the picture was complete.
bool frameFinish();

// Short error text after frameFeed() or frameFinish() returned false.
const char* frameError();

// True while picture mode is on: a picture arrived within
// SYSOPTV_FRAME_TIMEOUT_MS. While on, the designs must not draw, even if
// usage data arrives as well. Pictures have priority.
bool frameModeActive();

// Remembers where the last complete picture came from, from the header
// X-SysopTV-Source. The value may be percent encoded (UTF-8), it is decoded,
// control characters are dropped and it is cut to SYSOPTV_FRAME_SOURCE_MAX
// bytes without splitting a character. Empty if the header is missing.
void frameSetSource(const char* value);

// Source of the last picture, empty if none was named.
const char* frameSource();

// True if a picture arrived since the start.
bool frameSeen();

// Seconds since the last complete picture, 0 if none arrived yet.
uint32_t frameAgeSeconds();

// Error text when a part of the screen arrives while the screen is not a
// complete picture from the PC. The web server answers 409 with it.
extern const char FRAME_FULL_REQUIRED[];

// True while the whole screen shows what the PC drew. Only then parts of
// the screen may be sent; otherwise only a full picture (0, 0, 240, 240) is
// accepted. A full picture makes it valid again.
bool frameValid();

// Called whenever the firmware draws by itself (design, clock, NO CARRIER,
// update, system screens). After a restart the screen is never valid.
void frameInvalidate();

// The upload broke off. Lines already drawn make the screen invalid.
void frameAbort();
