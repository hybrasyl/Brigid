#!/usr/bin/env bash
#
# Assemble a macOS .app bundle from a self-contained `dotnet publish` directory.
#
# This is the .NET/MonoGame equivalent of what electron-builder does for the
# sibling Hybrasyl apps (creidhne/taliesin/epona): it takes a published payload
# and wraps it in a Contents/{MacOS,Resources} bundle with an Info.plist and
# icon. Signing + notarization are NOT done here — the release workflow handles
# those (they need CI-only Developer ID secrets). Run this locally to validate
# the bundle layout and that the app launches past SDL init.
#
# Usage:
#   make-app.sh --publish <dir> --version <x.y.z> --output <Brigid.app> \
#               [--icon <icon.png>] [--rid osx-arm64]
#
set -euo pipefail

RID="osx-arm64"
ICON_PNG=""
PUBLISH_DIR=""
VERSION=""
OUTPUT=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --publish) PUBLISH_DIR="$2"; shift 2 ;;
        --version) VERSION="$2"; shift 2 ;;
        --output)  OUTPUT="$2"; shift 2 ;;
        --icon)    ICON_PNG="$2"; shift 2 ;;
        --rid)     RID="$2"; shift 2 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

[[ -n "$PUBLISH_DIR" ]] || { echo "--publish is required" >&2; exit 2; }
[[ -n "$VERSION" ]]     || { echo "--version is required" >&2; exit 2; }
[[ -n "$OUTPUT" ]]      || { echo "--output is required" >&2; exit 2; }
[[ -d "$PUBLISH_DIR" ]] || { echo "publish dir not found: $PUBLISH_DIR" >&2; exit 1; }

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EXECUTABLE="Brigid"

echo "==> Assembling $OUTPUT (rid=$RID, version=$VERSION)"

rm -rf "$OUTPUT"
CONTENTS="$OUTPUT/Contents"
MACOS_DIR="$CONTENTS/MacOS"
RES_DIR="$CONTENTS/Resources"
mkdir -p "$MACOS_DIR" "$RES_DIR"

# Copy the entire published payload into Contents/MacOS so that
# AppContext.BaseDirectory (which the custom DllResolver probes) resolves to it.
cp -R "$PUBLISH_DIR"/. "$MACOS_DIR"/

# --- Native-library fixup -------------------------------------------------
# Restore the runtimes/<rid>/native/ layout the DllResolver expects (the
# self-contained publish flattens SDL2 to the payload root). Then drop the other
# RIDs' native payloads — they would otherwise be foreign-arch Mach-O blobs that
# codesign/notarization must account for.
bash "$SCRIPT_DIR/../fixup-sdl-runtimes.sh" "$MACOS_DIR" "$RID"
if [[ -d "$MACOS_DIR/runtimes" ]]; then
    for d in "$MACOS_DIR"/runtimes/*/; do
        name="$(basename "$d")"
        [[ "$name" == "$RID" ]] && continue
        rm -rf "$d"
    done
fi

# --- Info.plist -----------------------------------------------------------
sed "s/@VERSION@/$VERSION/g" "$SCRIPT_DIR/Info.plist.in" > "$CONTENTS/Info.plist"

# --- Icon -----------------------------------------------------------------
# Generate Brigid.icns from a high-res PNG source if one was provided.
# Absent an icon, the bundle ships without one (default app icon) — the
# workflow still produces a valid, signable, notarizable bundle.
if [[ -n "$ICON_PNG" && -f "$ICON_PNG" ]]; then
    echo "==> Generating .icns from $ICON_PNG"
    ICONSET="$(mktemp -d)/Brigid.iconset"
    mkdir -p "$ICONSET"
    for size in 16 32 64 128 256 512 1024; do
        sips -z "$size" "$size" "$ICON_PNG" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
    done
    # Retina (@2x) variants reuse the next size up.
    cp "$ICONSET/icon_32x32.png"     "$ICONSET/icon_16x16@2x.png"
    cp "$ICONSET/icon_64x64.png"     "$ICONSET/icon_32x32@2x.png"
    cp "$ICONSET/icon_256x256.png"   "$ICONSET/icon_128x128@2x.png"
    cp "$ICONSET/icon_512x512.png"   "$ICONSET/icon_256x256@2x.png"
    cp "$ICONSET/icon_1024x1024.png" "$ICONSET/icon_512x512@2x.png"
    rm -f "$ICONSET/icon_64x64.png" "$ICONSET/icon_1024x1024.png"
    iconutil -c icns "$ICONSET" -o "$RES_DIR/Brigid.icns"
else
    echo "==> No icon provided; bundle will use the default app icon"
fi

chmod +x "$MACOS_DIR/$EXECUTABLE"

echo "==> Bundle assembled: $OUTPUT"
