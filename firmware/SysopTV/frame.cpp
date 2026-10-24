// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.

#include "frame.h"
#include "config.h"
#include "screen.h"
#include <Arduino.h>
#include <TFT_eSPI.h>
#include <string.h>

static const int SIZE = 240;
static const uint8_t MAGIC[4] = {'S', 'Y', 'F', '1'};

enum Phase : uint8_t {
  PHASE_HEADER,
  PHASE_CONTROL,
  PHASE_RUN_COLOUR,
  PHASE_LITERAL,
  PHASE_DONE,
  PHASE_ERROR
};

static Phase phase = PHASE_ERROR;
static uint8_t header[12];
static uint8_t headerLen = 0;
static uint16_t fx, fy, fw, fh;
static uint16_t row = 0;          // current line inside the picture
static uint16_t col = 0;          // current pixel inside the line
static uint16_t packetLeft = 0;   // pixels left in the current packet
static uint8_t colourLow = 0;
static bool haveLow = false;      // first byte of a colour received
static uint16_t line[SIZE];
static const char* error = "";

static bool modeOn = false;
static bool seen = false;
static bool valid = false;        // the whole screen shows a picture from the PC

const char FRAME_FULL_REQUIRED[] = "full_frame_required";
static unsigned long lastFrame = 0;
static char source[SYSOPTV_FRAME_SOURCE_MAX + 1] = "";

static bool fail(const char* text) {
  // Lines that were already drawn leave a mixed screen behind.
  if (row > 0) {
    valid = false;
  }
  phase = PHASE_ERROR;
  error = text;
  return false;
}

static bool fullScreen() {
  return fx == 0 && fy == 0 && fw == SIZE && fh == SIZE;
}

void frameStart() {
  phase = PHASE_HEADER;
  headerLen = 0;
  row = col = packetLeft = 0;
  haveLow = false;
  error = "";
}

static uint16_t headerWord(int offset) {
  return (uint16_t)(header[offset] | (header[offset + 1] << 8));
}

static bool parseHeader() {
  if (memcmp(header, MAGIC, 4) != 0) {
    return fail("kein SYF1");
  }
  fx = headerWord(4);
  fy = headerWord(6);
  fw = headerWord(8);
  fh = headerWord(10);
  if (fw == 0 || fh == 0 || fx >= SIZE || fy >= SIZE || fw > SIZE - fx || fh > SIZE - fy) {
    return fail("Ausschnitt ausserhalb von 240x240");
  }
  // A part would land on top of something the firmware drew itself.
  if (!valid && !fullScreen()) {
    return fail(FRAME_FULL_REQUIRED);
  }
  phase = PHASE_CONTROL;
  return true;
}

// One decoded pixel into the line buffer; a full line goes to the display.
static bool putPixel(uint16_t colour) {
  if (row >= fh) {
    return fail("zu viele Pixel");
  }
  line[col++] = colour;
  if (col == fw) {
    TFT_eSPI& tft = screenTft();
    tft.startWrite();
    tft.setAddrWindow(fx, fy + row, fw, 1);
    tft.pushColors(line, fw, true);
    tft.endWrite();
    col = 0;
    row++;
    if (row == fh) {
      phase = PHASE_DONE;
    }
  }
  return true;
}

bool frameFeed(const uint8_t* data, size_t len) {
  for (size_t i = 0; i < len; i++) {
    uint8_t b = data[i];
    switch (phase) {
      case PHASE_HEADER:
        header[headerLen++] = b;
        if (headerLen == sizeof(header) && !parseHeader()) {
          return false;
        }
        break;
      case PHASE_CONTROL:
        packetLeft = (uint16_t)((b & 0x7F) + 1);
        phase = (b & 0x80) ? PHASE_RUN_COLOUR : PHASE_LITERAL;
        haveLow = false;
        break;
      case PHASE_RUN_COLOUR:
        if (!haveLow) {
          colourLow = b;
          haveLow = true;
          break;
        }
        {
          uint16_t colour = (uint16_t)(colourLow | (b << 8));
          while (packetLeft > 0) {
            packetLeft--;
            if (!putPixel(colour)) {
              return false;
            }
            if (phase == PHASE_DONE && packetLeft > 0) {
              return fail("zu viele Pixel");
            }
          }
          if (phase != PHASE_DONE) {
            phase = PHASE_CONTROL;
          }
        }
        break;
      case PHASE_LITERAL:
        if (!haveLow) {
          colourLow = b;
          haveLow = true;
          break;
        }
        haveLow = false;
        if (!putPixel((uint16_t)(colourLow | (b << 8)))) {
          return false;
        }
        packetLeft--;
        if (phase == PHASE_DONE) {
          if (packetLeft > 0) {
            return fail("zu viele Pixel");
          }
        } else if (packetLeft == 0) {
          phase = PHASE_CONTROL;
        }
        break;
      case PHASE_DONE:
        return fail("Daten nach dem letzten Pixel");
      case PHASE_ERROR:
        return false;
    }
    if ((i & 1023) == 1023) {
      yield();
    }
  }
  return true;
}

bool frameFinish() {
  if (phase == PHASE_ERROR) {
    return false;
  }
  if (phase != PHASE_DONE) {
    return fail(phase == PHASE_HEADER ? "Kopf unvollstaendig" : "zu wenige Pixel");
  }
  modeOn = true;
  seen = true;
  lastFrame = millis();
  if (fullScreen()) {
    valid = true;
  }
  return true;
}

bool frameValid() {
  return valid;
}

void frameInvalidate() {
  valid = false;
}

void frameAbort() {
  fail("Upload abgebrochen");
}

const char* frameError() {
  return error;
}

bool frameModeActive() {
  if (modeOn && millis() - lastFrame >= SYSOPTV_FRAME_TIMEOUT_MS) {
    modeOn = false;
  }
  return modeOn;
}

static int hexValue(char c) {
  if (c >= '0' && c <= '9') return c - '0';
  if (c >= 'a' && c <= 'f') return c - 'a' + 10;
  if (c >= 'A' && c <= 'F') return c - 'A' + 10;
  return -1;
}

void frameSetSource(const char* value) {
  size_t n = 0;
  for (const char* p = value; *p && n < SYSOPTV_FRAME_SOURCE_MAX; p++) {
    uint8_t c = (uint8_t)*p;
    if (c == '%' && hexValue(p[1]) >= 0 && hexValue(p[2]) >= 0) {
      c = (uint8_t)(hexValue(p[1]) * 16 + hexValue(p[2]));
      p += 2;
    }
    if (c < 0x20 || c == 0x7F) {
      continue;
    }
    source[n++] = (char)c;
  }
  // Do not end inside a UTF-8 character: drop an incomplete last sequence.
  size_t start = n;
  while (start > 0 && ((uint8_t)source[start - 1] & 0xC0) == 0x80) {
    start--;
  }
  if (start > 0 && ((uint8_t)source[start - 1] & 0x80)) {
    uint8_t lead = (uint8_t)source[start - 1];
    size_t need = (lead & 0xE0) == 0xC0 ? 2 : (lead & 0xF0) == 0xE0 ? 3 : (lead & 0xF8) == 0xF0 ? 4 : 0;
    if (need == 0 || n - (start - 1) < need) {
      n = start - 1;
    }
  }
  source[n] = '\0';
}

const char* frameSource() {
  return source;
}

bool frameSeen() {
  return seen;
}

uint32_t frameAgeSeconds() {
  return seen ? (millis() - lastFrame) / 1000 : 0;
}
