#!/bin/bash
#
# Builds, signs, installs, and launches the iPhone app on the connected device —
# and renews the free-account 7-day signing profile first, so this keeps working
# week after week. Just plug in the phone (unlocked) and run it (or double-click
# "Re-deploy to iPhone.command" in the repo root). See PROJECT.md §4 and §7.
#
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
CSPROJ="$REPO/1001AlbumHelper.iOS/1001AlbumHelper.iOS.csproj"
APP="$REPO/1001AlbumHelper.iOS/bin/Debug/net10.0-ios/ios-arm64/1001AlbumHelper.iOS.app"
SIGNPROJ="$REPO/1001AlbumHelper.iOS/SignHelper/SignHelper.xcodeproj"

UDID="00008130-001468C02630001C"                        # Andrew's iPhone 15 Pro
BUNDLE="com.larkins.albumhelper"
SIGN="Apple Development: alarkins93@yahoo.com (85U29FF3K6)"
DST_PROFILES="$HOME/Library/MobileDevice/Provisioning Profiles"
SRC_PROFILES="$HOME/Library/Developer/Xcode/UserData/Provisioning Profiles"

echo "▶ Renewing the 7-day signing profile…"
# xcodebuild talks to Apple to (re)create a fresh profile + register the device.
# The stub app itself doesn't need to finish building — provisioning happens first — so ignore its exit.
xcodebuild -project "$SIGNPROJ" -scheme SignHelper -destination "id=$UDID" \
  -allowProvisioningUpdates -allowProvisioningDeviceRegistration build >/dev/null 2>&1 || true
mkdir -p "$DST_PROFILES"
cp "$SRC_PROFILES/"*.mobileprovision "$DST_PROFILES/" 2>/dev/null || true

echo "▶ Building + signing (interpreter mode)…"
dotnet build "$CSPROJ" -c Debug -f net10.0-ios -r ios-arm64 \
  -p:MtouchDebug=false -p:ValidateXcodeVersion=false \
  -p:MtouchInterpreter=all -p:UseInterpreter=true -p:MtouchLink=None \
  -p:CodesignKey="$SIGN" --nologo -v minimal

echo "▶ Installing to the iPhone…"
xcrun devicectl device install app --device "$UDID" "$APP"

echo "▶ Launching (unlock the phone first)…"
if ! xcrun devicectl device process launch --device "$UDID" "$BUNDLE" 2>/dev/null; then
  echo "  Launch didn't take (phone locked?) — unlock it and tap the app."
fi

echo "✓ Done — good for another 7 days."
