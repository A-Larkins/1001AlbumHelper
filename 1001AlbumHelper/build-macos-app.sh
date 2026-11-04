#!/bin/bash
#
# Builds a double-clickable, self-contained "1001 Albums Helper.app" for macOS.
#
#   ./build-macos-app.sh              build into dist/
#   ./build-macos-app.sh --install    …and install it into /Applications
#   ./build-macos-app.sh --skip-tests build without the test gate (use sparingly)
#
# The unit tests run first and a failure stops the build: a bundle that reached
# /Applications is the copy you'll actually be using, so it shouldn't be the one
# that skipped its checks.
#
# The bundle includes its own copy of the .NET runtime, so it runs even without
# .NET installed, and needs nothing from this checkout once built: the sheet
# settings and service-account key are built into it, its data lives in
# ~/Library/Application Support/1001AlbumHelper, and exports go to
# ~/Documents/1001 Albums Helper.
#
# It's signed with your Apple Development certificate when there is one, so the
# permissions macOS grants it (Accessibility, controlling Music) survive a
# rebuild — an unsigned app gets a new identity every build, and macOS quietly
# forgets them. Override with SIGN_IDENTITY="…"; without any, it's ad-hoc
# signed and the permissions have to be granted again after each build.
#
set -euo pipefail

# PROJ_DIR is the shared project's folder. The thing we actually publish is the Desktop
# head, which produces the "1001AlbumHelper" executable.
PROJ_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(dirname "$PROJ_DIR")"
CSPROJ="$REPO_DIR/1001AlbumHelper.Desktop/1001AlbumHelper.Desktop.csproj"
TEST_CSPROJ="$REPO_DIR/1001AlbumHelper.Tests/1001AlbumHelper.Tests.csproj"
APP_NAME="1001 Albums Helper"
DIST_DIR="$PROJ_DIR/dist"
APP="$DIST_DIR/$APP_NAME.app"
PUBLISH_DIR="$PROJ_DIR/obj/appbundle-publish"
INSTALL_DIR="/Applications"

INSTALL=0
RUN_TESTS=1
for arg in "$@"; do
  case "$arg" in
    --install)    INSTALL=1 ;;
    --skip-tests) RUN_TESTS=0 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

case "$(uname -m)" in
  arm64) RID="osx-arm64" ;;
  *)     RID="osx-x64" ;;
esac

if [ "$RUN_TESTS" -eq 1 ]; then
  echo "▶ Running unit tests…"
  if ! dotnet test "$TEST_CSPROJ" -c Release --nologo -v quiet; then
    echo "✗ Tests failed — not building the app." >&2
    exit 1
  fi
  echo "✓ Tests passed."
else
  echo "⚠️  Skipping tests (--skip-tests)."
fi

echo "▶ Publishing self-contained ($RID)…"
rm -rf "$PUBLISH_DIR"
dotnet publish "$CSPROJ" -c Release -r "$RID" --self-contained true -o "$PUBLISH_DIR" \
  -p:UseAppHost=true --nologo -v quiet

echo "▶ Assembling $APP_NAME.app…"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# The apphost is the bundle's executable, with the rest of the publish output beside it — so
# macOS identifies the running app by the bundle's own signature (a launcher script exec-ing a
# binary elsewhere in the bundle muddles that, and with it the permissions).
cp -R "$PUBLISH_DIR/." "$APP/Contents/MacOS/"

# App icon (if present)
if [ -f "$PROJ_DIR/AppIcon.icns" ]; then
  cp "$PROJ_DIR/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
fi

# Info.plist
cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>              <string>1001 Albums Helper</string>
  <key>CFBundleDisplayName</key>       <string>1001 Albums Helper</string>
  <key>CFBundleIdentifier</key>        <string>com.larkins.albumhelper</string>
  <key>CFBundleVersion</key>           <string>1.0</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleExecutable</key>        <string>1001AlbumHelper</string>
  <key>CFBundleIconFile</key>          <string>AppIcon</string>
  <key>CFBundlePackageType</key>       <string>APPL</string>
  <key>LSMinimumSystemVersion</key>    <string>11.0</string>
  <key>NSHighResolutionCapable</key>   <true/>
  <key>NSAppleEventsUsageDescription</key>
  <string>Adds albums to your playlists in the Music app, and reads them back.</string>
</dict>
</plist>
PLIST

IDENTITY="${SIGN_IDENTITY:-$(security find-identity -v -p codesigning 2>/dev/null \
  | sed -n 's/.*"\(Apple Development: [^"]*\)".*/\1/p' | head -1)}"
if [ -n "$IDENTITY" ]; then
  echo "▶ Signing as ${IDENTITY}…"
  codesign --force --deep --timestamp=none --sign "$IDENTITY" "$APP"
else
  echo "⚠️  No signing certificate — signing ad hoc. macOS will ask for permissions again after every build."
  codesign --force --deep --sign - "$APP"
fi
codesign --verify --deep --strict "$APP"

echo "✓ Built: $APP"

if [ "$INSTALL" -eq 1 ]; then
  DEST="$INSTALL_DIR/$APP_NAME.app"
  echo "▶ Installing to ${DEST}…"
  # Refill the installed bundle rather than deleting and re-copying it: a fresh folder is a new
  # file to macOS, so the Dock stops recognizing its pinned icon as the app and lists the
  # running copy a second time under recent apps. Emptying it first still clears out leftovers
  # from an older publish. ditto, not rsync: the signatures of the bundle's non-Mach-O files
  # live in extended attributes, which macOS's rsync drops.
  mkdir -p "$DEST"
  find "$DEST" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
  ditto "$APP" "$DEST"
  codesign --verify --deep --strict "$DEST"
  /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$DEST" >/dev/null 2>&1 || true
  # Only one copy should exist: a second one in dist/ is what a Dock icon or Spotlight ends up
  # launching, long after it's gone stale.
  rm -rf "$APP"
  echo "✓ Installed: $DEST"
  echo "  Open it from Launchpad or run:  open -a \"$APP_NAME\""
else
  echo "  Double-click it in Finder, or run:  open \"$APP\""
  echo "  Install it with:                   $0 --install"
fi
