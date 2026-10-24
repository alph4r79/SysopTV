// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// Display driver, backlight and the system screens (boot, WLAN, setup
// hotspot, update). The built-in TFT_eSPI fonts have no
// umlauts, texts that need them use the VGA font from view_common.

#include "screen.h"
#include "config.h"
#include "frame.h"
#include "view_common.h"
#include <TFT_eSPI.h>

static TFT_eSPI tft;

static uint16_t cBack;
static uint16_t cAmber;
static uint16_t cDim;
static uint16_t cDark;
static uint16_t cRed;

static unsigned long lastProgressDraw = 0;
static int currentBrightness = -1;

static void header() {
  tft.fillScreen(cBack);
  tft.setTextColor(cDim, cBack);
  tft.setTextDatum(TL_DATUM);
  tft.drawString("SysopTV", 12, 10, 2);
  tft.setTextDatum(TR_DATUM);
  tft.drawString("v" SYSOPTV_VERSION, 228, 10, 2);
  tft.drawFastHLine(12, 32, 216, cDark);
}

static void center(const String& text, int y, int font, uint16_t color) {
  tft.setTextColor(color, cBack);
  tft.setTextDatum(MC_DATUM);
  tft.drawString(text, 120, y, font);
}

static String shorten(const String& text, unsigned int maxLen) {
  if (text.length() <= maxLen) {
    return text;
  }
  return text.substring(0, maxLen - 2) + "..";
}

TFT_eSPI& screenTft() {
  return tft;
}

void screenBegin() {
  frameInvalidate();
  tft.init();
  tft.setRotation(0);
  cBack = TFT_BLACK;
  // Amber with little green, otherwise this panel shows it as yellow.
  cAmber = AMBER_MAIN;
  cDim = AMBER_DIM;
  cDark = AMBER_FRAME;
  cRed = AMBER_WARN;
  tft.fillScreen(cBack);
  analogWriteRange(255);
  screenBrightness(100);
}

void screenBrightness(uint8_t percent) {
  if (percent > 100) {
    percent = 100;
  }
  if (currentBrightness == percent) {
    return;
  }
  currentBrightness = percent;
  // The backlight is active low: 0 is fully on, 255 is off.
  int duty = 255 - (percent * 255 + 50) / 100;
  if (duty <= 0) {
    analogWrite(TFT_BL, 0);
    digitalWrite(TFT_BL, LOW);
  } else if (duty >= 255) {
    analogWrite(TFT_BL, 0);
    digitalWrite(TFT_BL, HIGH);
  } else {
    analogWrite(TFT_BL, duty);
  }
}

void screenResetDone() {
  frameInvalidate();
  tft.fillScreen(cBack);
  vgaText("Passwort", 120, 84, ALIGN_CENTER, 2, cAmber, cBack);
  // UTF-8 for u umlaut, written as escapes to keep the source ASCII.
  vgaText("zur\xc3\xbc" "ckgesetzt", 120, 120, ALIGN_CENTER, 2, cAmber, cBack);
  vgaText("WLAN bleibt erhalten", 120, 172, ALIGN_CENTER, 1, cDim, cBack);
}

void screenBoot(bool rescue) {
  frameInvalidate();
  tft.fillScreen(cBack);
  center("SysopTV", 104, 4, cAmber);
  center("v" SYSOPTV_VERSION, 136, 2, cDim);
  if (rescue) {
    center("RETTUNGSMODUS", 180, 2, cRed);
  }
}

void screenConnecting(const String& ssid) {
  frameInvalidate();
  header();
  center("Verbinde mit WLAN", 100, 2, cDim);
  center(shorten(ssid, 16), 130, 4, cAmber);
}

void screenReady(const String& ip, const String& host, bool rescue) {
  frameInvalidate();
  header();
  if (rescue) {
    center("RETTUNGSMODUS", 52, 2, cRed);
  } else {
    center("Bereit", 52, 2, cDim);
  }
  center("IP-Adresse", 84, 2, cDim);
  center(ip, 112, 4, cAmber);
  center(host, 140, 2, cDim);
  tft.drawFastHLine(12, 164, 216, cDark);
  center("Update im Browser:", 186, 2, cDim);
  center(ip + "/update", 208, 2, cAmber);
}

void screenLost() {
  frameInvalidate();
  header();
  center("WLAN getrennt", 100, 4, cAmber);
  center("verbinde neu ...", 134, 2, cDim);
}

void screenSetup(const char* apName) {
  frameInvalidate();
  header();
  center("WLAN einrichten", 58, 4, cAmber);
  center("1. Mit Hotspot verbinden:", 96, 2, cDim);
  center(apName, 122, 4, cAmber);
  center("2. Im Browser aufrufen:", 162, 2, cDim);
  center("192.168.4.1", 188, 4, cAmber);
}

void screenUpdateStart() {
  frameInvalidate();
  header();
  center("Update", 80, 4, cAmber);
  center("Nicht ausschalten!", 112, 2, cRed);
  lastProgressDraw = 0;
}

void screenUpdateProgress(size_t bytes) {
  frameInvalidate();
  // Redrawing on every chunk slows the upload down, so only a few times per second.
  unsigned long now = millis();
  if (now - lastProgressDraw < 300) {
    return;
  }
  lastProgressDraw = now;
  tft.fillRect(0, 140, 240, 40, cBack);
  center(String(bytes / 1024) + " KB", 160, 4, cAmber);
}

void screenUpdateResult(bool ok, const String& message) {
  frameInvalidate();
  header();
  if (ok) {
    center("Update fertig", 100, 4, cAmber);
    center("Neustart ...", 134, 2, cDim);
  } else {
    center("Update fehlgeschlagen", 90, 2, cRed);
    center(shorten(message, 28), 118, 2, cDim);
    center("Alte Firmware bleibt", 150, 2, cAmber);
    center("Neustart ...", 178, 2, cDim);
  }
}
