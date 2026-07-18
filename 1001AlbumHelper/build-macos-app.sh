#!/bin/bash
#
# Builds a double-clickable, self-contained "1001 Albums Helper.app" for macOS.
#
#   ./build-macos-app.sh
#
# The bundle includes its own copy of the .NET runtime, so it runs even without
# .NET installed. It launches the Avalonia UI and reads/writes the input/ and
# output/ folders (and appsettings.json / credentials.json) in this project
# directory.
#
set -euo pipefail

PROJ_DIR="$(cd "$(dirname "$0")" && pwd)"
CSPROJ="$PROJ_DIR/1001AlbumHelper.csproj"
APP_NAME="1001 Albums Helper"
DIST_DIR="$PROJ_DIR/dist"
APP="$DIST_DIR/$APP_NAME.app"
PUBLISH_DIR="$PROJ_DIR/obj/appbundle-publish"

case "$(uname -m)" in
  arm64) RID="osx-arm64" ;;
  *)     RID="osx-x64" ;;
esac

echo "▶ Publishing self-contained ($RID)…"
rm -rf "$PUBLISH_DIR"
dotnet publish "$CSPROJ" -c Release -r "$RID" --self-contained true -o "$PUBLISH_DIR" \
  -p:UseAppHost=true --nologo -v quiet

echo "▶ Assembling $APP_NAME.app…"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/app"

# Publish output → Resources/app
cp -R "$PUBLISH_DIR/." "$APP/Contents/Resources/app/"

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
  <key>CFBundlePackageType</key>       <string>APPL</string>
  <key>LSMinimumSystemVersion</key>    <string>11.0</string>
  <key>NSHighResolutionCapable</key>   <true/>
</dict>
</plist>
PLIST

echo "✓ Built: $APP"
echo "  Double-click it in Finder, or run:  open \"$APP\""
