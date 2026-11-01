#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
swift build -c release
BIN_DIR="$(swift build -c release --show-bin-path)"
OUTPUT="$PWD/dist"
APP="$OUTPUT/Delta Browser.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/DeltaMac" "$APP/Contents/MacOS/DeltaMac"
cp Packaging/Info.plist "$APP/Contents/Info.plist"
# Cover both SwiftPM bundleURL and Cocoa resourceURL resource lookup layouts.
find "$BIN_DIR" -maxdepth 1 -name '*.bundle' -type d -exec cp -R {} "$APP/" \;
find "$BIN_DIR" -maxdepth 1 -name '*.bundle' -type d -exec cp -R {} "$APP/Contents/Resources/" \;
plutil -lint "$APP/Contents/Info.plist"
SIGNING_IDENTITY="${DELTA_SIGNING_IDENTITY:--}"
codesign --force --deep --sign "$SIGNING_IDENTITY" "$APP"
codesign --verify --deep --strict "$APP"
ARCH="$(uname -m)"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$OUTPUT/Delta-Mac-preview-0.1.0-$ARCH.zip"
echo "Built $OUTPUT/Delta-Mac-preview-0.1.0-$ARCH.zip"
echo "Ad-hoc builds are development-only; public distribution requires Developer ID signing and notarization."
