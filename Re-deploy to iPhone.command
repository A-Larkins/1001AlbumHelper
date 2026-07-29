#!/bin/bash
#
# Double-click this in Finder to reinstall the app on your iPhone and renew the
# 7-day trial. Plug the phone in and unlock it first.
#
cd "$(dirname "$0")"
echo "=== Re-deploying 1001 Albums Helper to your iPhone ==="
echo
if bash ./1001AlbumHelper.iOS/deploy-to-device.sh; then
  echo
  echo "🎉 All set — the app is refreshed on your phone."
else
  echo
  echo "⚠️  Something went wrong above. Make sure the iPhone is plugged in and unlocked."
fi
echo
read -n 1 -s -r -p "Press any key to close this window…"
echo
