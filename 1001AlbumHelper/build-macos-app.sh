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
# .NET installed. It launches the Avalonia UI and reads/writes the input/ and
# output/ folders (and appsettings.json / credentials.json) in this project
# directory.
#
set -euo pipefail

PROJ_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(dirname "$PROJ_DIR")"
CSPROJ="$PROJ_DIR/1001AlbumHelper.csproj"
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
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/app"

# Publish output → Resources/app
cp -R "$PUBLISH_DIR/." "$APP/Contents/Resources/app/"

# App icon (if present)
if [ -f "$PROJ_DIR/AppIcon.icns" ]; then
  cp "$PROJ_DIR/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
fi

# Launcher: point the app at this project's data folder, then run the apphost.
cat > "$APP/Contents/MacOS/launch" <<LAUNCH
#!/bin/bash
export ALBUMHELPER_DATA_DIR="$PROJ_DIR"
DIR="\$(cd "\$(dirname "\$0")" && pwd)"
exec "\$DIR/../Resources/app/1001AlbumHelper" "\$@"
LAUNCH
chmod +x "$APP/Contents/MacOS/launch"

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
  <key>CFBundleExecutable</key>        <string>launch</string>
  <key>CFBundleIconFile</key>          <string>AppIcon</string>
  <key>CFBundlePackageType</key>       <string>APPL</string>
  <key>LSMinimumSystemVersion</key>    <string>11.0</string>
  <key>NSHighResolutionCapable</key>   <true/>
</dict>
</plist>
PLIST

echo "✓ Built: $APP"

if [ "$INSTALL" -eq 1 ]; then
  DEST="$INSTALL_DIR/$APP_NAME.app"
  echo "▶ Installing to ${DEST}…"
  # Replace rather than merge: leftovers from an older publish would otherwise
  # linger inside the bundle.
  rm -rf "$DEST"
  cp -R "$APP" "$DEST"
  echo "✓ Installed: $DEST"
  echo "  Open it from Launchpad or run:  open -a \"$APP_NAME\""
else
  echo "  Double-click it in Finder, or run:  open \"$APP\""
  echo "  Install it with:                   $0 --install"
fi
