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
#   sysoptv-app-<version>-portable.zip   SysopTV.exe, SysopTV.exe.config, LICENSE, font licences
#   sysoptv-app-<version>-setup.exe      if Inno Setup is installed
#
# Optional: ISCC=/path/to/ISCC.exe if Inno Setup is somewhere else.

set -euo pipefail
cd "$(dirname "$0")"

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
# Git Bash has no zip, Windows PowerShell does.
STAGE=$(mktemp -d)
cp "$BIN/SysopTV.exe" "$BIN/SysopTV.exe.config" LICENSE "$STAGE/"
cp app/assets/fonts/OFL-Inter.txt app/assets/fonts/OFL-JetBrainsMono.txt "$STAGE/"
powershell.exe -NoProfile -Command \
  "Compress-Archive -Path '$(cygpath -w "$STAGE")\\*' -DestinationPath '$(cygpath -w "$PWD/$ZIP")' -Force"
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
  exit 0
fi

# Git Bash would turn /Q and /D... into paths, so path conversion is off here.
MSYS_NO_PATHCONV=1 "$ISCC" /Q "/DAppVersion=$VERSION" "$(cygpath -w "$PWD/installer/sysoptv.iss")"
SETUP="dist/sysoptv-app-$VERSION-setup.exe"
echo "Setup: $SETUP ($(stat -c %s "$SETUP") Byte)"
