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

RENEW_LOG="$(mktemp -t albumhelper-renew)"
trap 'rm -f "$RENEW_LOG"' EXIT

# Expiry of a .mobileprovision as epoch seconds, or non-zero if it can't be read.
profile_expiry_epoch() {
  local plist iso
  plist="$(security cms -D -i "$1" 2>/dev/null)" || return 1
  iso="$(printf '%s' "$plist" | plutil -extract ExpirationDate raw - 2>/dev/null)" || return 1
  date -j -f "%Y-%m-%dT%H:%M:%SZ" "$iso" "+%s" 2>/dev/null
}

# Stops with the reason, plus what xcodebuild actually said — the renewal runs quietly, so
# without this its output is lost and the failure surfaces much later as an opaque
# "Could not find any available provisioning profiles" from the dotnet build.
die_no_profile() {
  echo
  echo "✗ Signing profile renewal didn't produce a usable profile."
  echo "  $1"
  echo
  echo "  Tail of the renewal log:"
  tail -20 "$RENEW_LOG" | sed 's/^/    /'
  echo
  echo "  Most often this means Xcode needs signing in again: open Xcode → Settings →"
  echo "  Accounts, confirm the Apple ID is still listed, then re-run this. PROJECT.md §4."
  exit 1
}

echo "▶ Renewing the 7-day signing profile…"
# xcodebuild talks to Apple to (re)create a fresh profile + register the device. Its exit code
# is deliberately ignored: provisioning happens early on and the stub app itself often doesn't
# finish building, which is harmless. What matters is whether a valid profile actually landed —
# so that, not the exit code, is what gets checked below.
xcodebuild -project "$SIGNPROJ" -scheme SignHelper -destination "id=$UDID" \
  -allowProvisioningUpdates -allowProvisioningDeviceRegistration build >"$RENEW_LOG" 2>&1 || true
mkdir -p "$DST_PROFILES"
cp "$SRC_PROFILES/"*.mobileprovision "$DST_PROFILES/" 2>/dev/null || true

# Verify the outcome rather than trusting the two steps above. Xcode deletes profiles once they
# expire, so "the renewal quietly did nothing" looks like an empty folder here — which is exactly
# what happened on 2026-08-05, and cost a while to diagnose from the downstream build error.
newest_expiry=0
for profile in "$DST_PROFILES"/*.mobileprovision; do
  [ -e "$profile" ] || continue
  expiry="$(profile_expiry_epoch "$profile")" || continue
  if [ "$expiry" -gt "$newest_expiry" ]; then newest_expiry="$expiry"; fi
done

if [ "$newest_expiry" -eq 0 ]; then
  die_no_profile "Nothing readable was installed into: $DST_PROFILES"
elif [ "$newest_expiry" -le "$(date +%s)" ]; then
  die_no_profile "The newest profile there expired on $(date -r "$newest_expiry" '+%Y-%m-%d %H:%M')."
fi

echo "  ✓ Profile valid until $(date -r "$newest_expiry" '+%Y-%m-%d %H:%M')."

echo "▶ Building + signing (interpreter mode)…"
dotnet build "$CSPROJ" -c Debug -f net10.0-ios -r ios-arm64 \
  -p:MtouchDebug=false -p:ValidateXcodeVersion=false \
  -p:MtouchInterpreter=all -p:UseInterpreter=true \
  -p:CodesignKey="$SIGN" --nologo -v minimal

echo "▶ Installing to the iPhone…"
xcrun devicectl device install app --device "$UDID" "$APP"

echo "▶ Launching (unlock the phone first)…"
if ! xcrun devicectl device process launch --device "$UDID" "$BUNDLE" 2>/dev/null; then
  echo "  Launch didn't take (phone locked?) — unlock it and tap the app."
fi

echo "✓ Done — good for another 7 days."
