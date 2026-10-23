// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.

#include "design.h"
#include "views.h"
#include "settings.h"
#include <Arduino.h>

static const Design designs[DESIGN_COUNT] = {
  {"S\xc3\xa4" "ulen", "S&auml;ulen", saeulenInvalidate, saeulenShow},
  {"Phosphor", "Phosphor", phosphorInvalidate, phosphorShow},
  {"DOS", "DOS", dosInvalidate, dosShow},
  {"ANSI", "ANSI", ansiInvalidate, ansiShow},
  {"Bernstein", "Bernstein", amberInvalidate, amberShow},
  {"Klassik", "Klassik", klassikInvalidate, klassikShow},
};

static uint8_t activeDesign = 0xFF;
static uint8_t chosenDesign = 0;
static uint8_t chosenCycle = 0;
static unsigned long designSince = 0;

const Design& designGet(uint8_t id) {
  if (id >= DESIGN_COUNT) {
    id = 0;
  }
  return designs[id];
}

uint8_t designUpdate() {
  const Settings& s = settingsGet();
  if (activeDesign == 0xFF || s.design != chosenDesign || s.cycleSeconds != chosenCycle) {
    chosenDesign = s.design;
    chosenCycle = s.cycleSeconds;
    activeDesign = s.design < DESIGN_COUNT ? s.design : 0;
    designSince = millis();
  } else if (s.cycleSeconds > 0 && millis() - designSince >= s.cycleSeconds * 1000UL) {
    activeDesign = (activeDesign + 1) % DESIGN_COUNT;
    designSince = millis();
  }
  return activeDesign;
}

uint8_t designActive() {
  return activeDesign == 0xFF ? settingsGet().design : activeDesign;
}
