#!/usr/bin/env bash
#
# Builds "Kavosh Diagnostics.app" into ./dist and zips it (dist/Kavosh-Diagnostics-macOS.zip).
#
#   Scripts/build-app.sh               release build for this Mac's architecture
#   Scripts/build-app.sh --universal   Apple silicon + Intel in one app (needs full Xcode)
#   Scripts/build-app.sh --debug       debug build
#   Scripts/build-app.sh --open        open the app when done
#
set -euo pipefail

APP_NAME="Kavosh Diagnostics"
EXECUTABLE="KavoshDiagnostics"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

CONFIG="release"
UNIVERSAL=0
OPEN_APP=0
for arg in "$@"; do
  case "$arg" in
    --universal) UNIVERSAL=1 ;;
    --debug) CONFIG="debug" ;;
    --open) OPEN_APP=1 ;;
    -h|--help)
      sed -n '2,10p' "$0"
      exit 0
      ;;
    *)
      echo "Unknown option: $arg" >&2
      exit 1
      ;;
  esac
done

if [[ "$(uname)" != "Darwin" ]]; then
  echo "This script builds a macOS app and must run on a Mac." >&2
  exit 1
fi

BUILD_ARGS=(-c "$CONFIG")
LABEL="$CONFIG"
if [[ "$UNIVERSAL" == "1" ]]; then
  BUILD_ARGS+=(--arch arm64 --arch x86_64)
  LABEL="$CONFIG, universal"
fi

echo "==> Building $APP_NAME ($LABEL)"
swift build "${BUILD_ARGS[@]}"
BIN_DIR="$(swift build "${BUILD_ARGS[@]}" --show-bin-path)"

APP="$ROOT/dist/$APP_NAME.app"
echo "==> Assembling $APP"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/Fonts"
cp "$BIN_DIR/$EXECUTABLE" "$APP/Contents/MacOS/$EXECUTABLE"
cp "Resources/Info.plist" "$APP/Contents/Info.plist"
cp "Resources/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
cp Resources/Fonts/*.ttf Resources/Fonts/OFL.txt "$APP/Contents/Resources/Fonts/"

# Copy any SwiftPM resource bundles that the build produced.
shopt -s nullglob
for bundle in "$BIN_DIR"/*.bundle; do
  cp -R "$bundle" "$APP/Contents/Resources/"
done
shopt -u nullglob

BUILD_NUMBER="$(date +%Y%m%d%H%M)"
plutil -replace CFBundleVersion -string "$BUILD_NUMBER" "$APP/Contents/Info.plist"

echo "==> Signing (ad-hoc)"
codesign --force --deep --sign - "$APP"

ZIP_NAME="Kavosh-Diagnostics-macOS.zip"
echo "==> Zipping"
rm -f "$ROOT/dist/$ZIP_NAME"
(cd "$ROOT/dist" && ditto -c -k --keepParent "$APP_NAME.app" "$ZIP_NAME")

echo ""
echo "Done: $APP"
echo "      $ROOT/dist/$ZIP_NAME"

if [[ "$OPEN_APP" == "1" ]]; then
  open "$APP"
fi
