#!/usr/bin/env bash
# SysopTV, Copyright (C) 2026 Christoph Scheel
# Licensed under the GNU General Public License v3.0, see LICENSE.
#
# Builds the Windows program (app/SysopTV.Tray) and its setup.
#
# Needs: .NET SDK 6 or newer (builds for .NET Framework 4.8), and for the
# setup Inno Setup 6. Without Inno Setup only the portable ZIP is made.
#   winget install --id Microsoft.DotNet.SDK.8 -e
#   winget install --id JRSoftware.InnoSetup -e
#
# Result in dist/:
#   sysoptv-app-<version>-portable.zip   SysopTV.exe, SysopTV.exe.config, LICENSE, font licences,
#                                        firmware/sysoptv-<firmware version>.bin
#   sysoptv-app-<version>-setup.exe      if Inno Setup is installed
#   SHA256SUMS.txt                       checksums of the current release files
#
# The firmware for "Firmware aktualisieren" comes from dist/sysoptv-<version>.bin
# with the version in firmware/SysopTV/config.h, so build.sh runs first.
#
# Optional: ISCC=/path/to/ISCC.exe if Inno Setup is somewhere else.

set -euo pipefail
cd "$(dirname "$0")"

# SHA256SUMS.txt over the release files of the current versions in dist/
# (firmware, setup, portable ZIP, plugin), in the format of sha256sum. The
# program checks every download from GitHub against it, so every release
# carries this file.
write_sums() {
  local fw app plugin files=()
  fw=$(sed -n 's/^#define SYSOPTV_VERSION "\([^"]*\)".*/\1/p' firmware/SysopTV/config.h)
  app=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' app/SysopTV.Tray/SysopTV.Tray.csproj | head -n 1)
  plugin=$(sed -n 's/^ \* Version: *\([0-9.]*\).*/\1/p' wordpress/sysoptv-woocommerce/sysoptv-woocommerce.php | head -n 1)
  for f in "sysoptv-$fw.bin" "sysoptv-app-$app-setup.exe" "sysoptv-app-$app-portable.zip" "sysoptv-woocommerce-$plugin.zip"; do
    if [ -f "dist/$f" ]; then
      files+=("$f")
    fi
  done
  if [ ${#files[@]} -gt 0 ]; then
    (cd dist && sha256sum "${files[@]}" | sed 's/ \*/  /' > SHA256SUMS.txt)
    echo "Prüfsummen: dist/SHA256SUMS.txt (${#files[@]} Dateien)"
  fi
}

# No usage data from the .NET tools.
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

PROJECT=app/SysopTV.Tray/SysopTV.Tray.csproj
BIN=app/SysopTV.Tray/bin/Release/net48

VERSION=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$PROJECT" | head -n 1)
if [ -z "$VERSION" ]; then
  echo "FEHLER: <Version> nicht in $PROJECT gefunden." >&2
  exit 1
fi

FW_VERSION=$(sed -n 's/^#define SYSOPTV_VERSION "\([^"]*\)".*/\1/p' firmware/SysopTV/config.h)
FIRMWARE="dist/sysoptv-$FW_VERSION.bin"
if [ -z "$FW_VERSION" ] || [ ! -f "$FIRMWARE" ]; then
  echo "FEHLER: $FIRMWARE fehlt, bitte zuerst build.sh ausführen." >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "FEHLER: dotnet nicht gefunden. .NET SDK installieren:" >&2
  echo "  winget install --id Microsoft.DotNet.SDK.8 -e" >&2
  exit 1
fi

echo "Baue SysopTV $VERSION ..."
dotnet build "$PROJECT" -c Release -nologo -v minimal

mkdir -p dist
ZIP="dist/sysoptv-app-$VERSION-portable.zip"
rm -f "$ZIP"
# Git Bash has no zip. Compress-Archive of Windows PowerShell 5.1 writes
# backslashes into folder names inside the ZIP, so the entries are added one
# by one with forward slashes.
STAGE=$(mktemp -d)
mkdir -p "$STAGE/firmware"
cp "$BIN/SysopTV.exe" "$BIN/SysopTV.exe.config" LICENSE "$STAGE/"
cp app/assets/fonts/OFL-Inter.txt app/assets/fonts/OFL-JetBrainsMono.txt "$STAGE/"
cp "$FIRMWARE" "$STAGE/firmware/"
powershell.exe -NoProfile -Command "
  Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
  \$root = '$(cygpath -w "$STAGE")'
  \$zip = [IO.Compression.ZipFile]::Open('$(cygpath -w "$PWD/$ZIP")', 'Create')
  try {
    Get-ChildItem -LiteralPath \$root -Recurse -File | ForEach-Object {
      \$name = \$_.FullName.Substring(\$root.Length + 1).Replace('\\', '/')
      [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(\$zip, \$_.FullName, \$name, 'Optimal')
    }
  } finally { \$zip.Dispose() }
"
rm -rf "$STAGE"
echo "Portable: $ZIP ($(stat -c %s "$ZIP") Byte)"

if [ -z "${ISCC:-}" ]; then
  for candidate in \
    "$LOCALAPPDATA/Programs/Inno Setup 6/ISCC.exe" \
    "/c/Program Files (x86)/Inno Setup 6/ISCC.exe" \
    "/c/Program Files/Inno Setup 6/ISCC.exe"; do
    if [ -f "$candidate" ]; then
      ISCC="$candidate"
      break
    fi
  done
fi
if [ -z "${ISCC:-}" ] || [ ! -f "$ISCC" ]; then
  echo "Inno Setup nicht gefunden, kein Setup gebaut. Installieren mit:"
  echo "  winget install --id JRSoftware.InnoSetup -e"
  write_sums
  exit 0
fi

# Git Bash would turn /Q and /D... into paths, so path conversion is off here.
MSYS_NO_PATHCONV=1 "$ISCC" /Q "/DAppVersion=$VERSION" "/DFirmwareVersion=$FW_VERSION" "$(cygpath -w "$PWD/installer/sysoptv.iss")"
SETUP="dist/sysoptv-app-$VERSION-setup.exe"
echo "Setup: $SETUP ($(stat -c %s "$SETUP") Byte)"
write_sums
