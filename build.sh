#!/usr/bin/env bash
# SysopTV, Copyright (C) 2026 Christoph Scheel
# Licensed under the GNU General Public License v3.0, see LICENSE.
#
# Builds the SysopTV firmware with arduino-cli.
#
# Needs: arduino-cli, the esp8266 core 3.1.2 and the libraries TFT_eSPI 2.5.43
# and ArduinoJson 7.4.3.
#   arduino-cli core install esp8266:esp8266@3.1.2 \
#     --additional-urls https://arduino.esp8266.com/stable/package_esp8266com_index.json
#   arduino-cli lib install "TFT_eSPI@2.5.43"
#   arduino-cli lib install "ArduinoJson@7.4.3"
#
# Optional: FQBN_CORE=esp8266com:esp8266 for a manually installed core,
# EXTRA_PROPS for additional --build-property arguments.

set -euo pipefail
cd "$(dirname "$0")"

VERSION=$(sed -n 's/^#define SYSOPTV_VERSION "\([^"]*\)".*/\1/p' firmware/SysopTV/config.h)
if [ -z "$VERSION" ]; then
  echo "FEHLER: SYSOPTV_VERSION nicht in config.h gefunden." >&2
  exit 1
fi
CORE=${FQBN_CORE:-esp8266:esp8266}
# Flash layout 4M3M like the original GeekMagic firmware: sketch and OTA
# buffer below 0x100000, so an update never touches the filesystem area
# 0x100000 to 0x3FA000 with the web interface of the original firmware.
FQBN="$CORE:generic:eesz=4M3M,FlashMode=dio,ResetMethod=nodemcu,CrystalFreq=26"

# Display of the GeekMagic SmallTV Ultra: ST7789 240x240, no CS line, SPI mode 3,
# backlight on GPIO5 (active low).
TFT="-DUSER_SETUP_LOADED=1 -DST7789_DRIVER=1 -DTFT_WIDTH=240 -DTFT_HEIGHT=240 \
-DTFT_INVERSION_ON=1 -DTFT_MISO=-1 -DTFT_MOSI=13 -DTFT_SCLK=14 -DTFT_CS=-1 \
-DTFT_DC=0 -DTFT_RST=2 -DTFT_BL=5 -DTFT_BACKLIGHT_ON=0 -DSPI_FREQUENCY=27000000 \
-DTFT_SPI_MODE=SPI_MODE3 \
-DLOAD_GLCD=1 -DLOAD_FONT2=1 -DLOAD_FONT4=1 -DLOAD_FONT8=1 -DDISABLE_ALL_LIBRARY_WARNINGS=1"

# Only these library versions are tested.
for LIB in "TFT_eSPI 2.5.43" "ArduinoJson 7.4.3"; do
  NAME=${LIB% *}
  WANT=${LIB#* }
  if ! arduino-cli lib list "$NAME" 2>/dev/null | grep -Eq "^$NAME +$WANT( |$)"; then
    echo "FEHLER: Bibliothek $NAME $WANT fehlt oder hat eine andere Version." >&2
    echo "  arduino-cli lib install \"$NAME@$WANT\"" >&2
    exit 1
  fi
done

mkdir -p dist build
# shellcheck disable=SC2086
arduino-cli compile -b "$FQBN" \
  --build-property "compiler.cpp.extra_flags=$TFT" \
  --build-property "compiler.c.extra_flags=$TFT" \
  ${EXTRA_PROPS:-} \
  --output-dir build firmware/SysopTV

BIN="dist/sysoptv-$VERSION.bin"
cp build/SysopTV.ino.bin "$BIN"

# The image must stay small enough that the original GeekMagic firmware
# (about 505 KB) still fits next to it for an update back.
SIZE=$(stat -c %s "$BIN")
LIMIT=500000
echo "Firmware: $BIN ($SIZE Byte, Grenze $LIMIT)"
if [ "$SIZE" -gt "$LIMIT" ]; then
  echo "FEHLER: Firmware ist zu groß." >&2
  exit 1
fi
