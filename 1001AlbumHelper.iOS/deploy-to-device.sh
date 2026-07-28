#!/bin/bash
#
# Builds, signs, installs, and launches the iPhone app on the connected device.
# See PROJECT.md §4 (setup) and §7 (why these flags). Free-account profile expires weekly —
# just re-run this to re-deploy.
#
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
CSPROJ="$REPO/1001AlbumHelper.iOS/1001AlbumHelper.iOS.csproj"
APP="$REPO/1001AlbumHelper.iOS/bin/Debug/net10.0-ios/ios-arm64/1001AlbumHelper.iOS.app"

UDID="00008130-001468C02630001C"                        # Andrew's iPhone 15 Pro
BUNDLE="com.larkins.albumhelper"
SIGN="Apple Development: alarkins93@yahoo.com (85U29FF3K6)"

echo "▶ Building + signing (interpreter mode)…"
dotnet build "$CSPROJ" -c Debug -f net10.0-ios -r ios-arm64 \
  -p:MtouchDebug=false -p:ValidateXcodeVersion=false \
  -p:MtouchInterpreter=all -p:UseInterpreter=true \
  -p:CodesignKey="$SIGN" --nologo -v minimal

echo "▶ Installing to the iPhone…"
xcrun devicectl device install app --device "$UDID" "$APP"

echo "▶ Launching (unlock the phone first)…"
if ! xcrun devicectl device process launch --device "$UDID" "$BUNDLE" 2>/dev/null; then
  echo "  Launch didn't take (phone locked?) — unlock it and tap the app, or re-run."
fi

echo "✓ Done."
