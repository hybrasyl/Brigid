#!/usr/bin/env bash
#
# Restore the runtimes/<rid>/native/ layout for the versioned SDL2 library
# names on macOS.
#
# A self-contained `dotnet publish` flattens MonoGame's SDL2 native to the
# payload root. DllResolver (Brigid/DllResolver.cs) probes the app root first
# on macOS/Linux, but the runtimes/ copy preserves a fallback for the
# versioned SDL2 filenames that a name-only NativeLibrary.TryLoad can't match.
# SDL2_mixer is already shipped there as Content.
#
# Windows and Linux are deliberately excluded: SDL2 must exist at exactly ONE
# path in those payloads. MonoGame's FuncLoader probes runtimes/<rid>/native/
# BEFORE the app root on both, and both loaders treat same-named libraries at
# different paths/inodes as independent modules (Windows dedupes by full path,
# glibc by inode — and `cp` creates a new inode). A second copy therefore
# splits the process into two SDL2 instances: MonoGame's owns the window,
# Brigid's never sees an event, and all input is dead in packaged builds. The
# flattened app-root SDL2 is the single canonical copy; both loaders fall
# through to it. macOS is safe with the copy because MonoGame probes the app
# root first there, matching DllResolver.
#
# Usage: fixup-sdl-runtimes.sh <payload-dir> <rid>
#   <payload-dir>  directory holding the published binaries (where the apphost
#                  and the runtimes/ tree live)
#   <rid>          osx-arm64 | osx-x64 | win-x64 | linux-x64
#
set -euo pipefail

DIR="${1:?payload dir required}"
RID="${2:?rid required}"

case "$RID" in
    osx-*)   SDL_NAMES=(libSDL2-2.0.0.dylib libSDL2.dylib) ;;
    win-*|linux-*)
        echo "fixup-sdl-runtimes: $RID — SDL2 stays app-root-only; scrubbing any stray runtimes copy"
        rm -f "$DIR/runtimes/$RID/native/SDL2.dll" \
              "$DIR/runtimes/$RID/native/libSDL2-2.0.so.0" \
              "$DIR/runtimes/$RID/native/libSDL2.so.0" \
              "$DIR/runtimes/$RID/native/libSDL2.so"
        exit 0 ;;
    *) echo "fixup-sdl-runtimes: unknown rid '$RID'" >&2; exit 2 ;;
esac

NATIVE_DIR="$DIR/runtimes/$RID/native"
mkdir -p "$NATIVE_DIR"

for name in "${SDL_NAMES[@]}"; do
    if [[ -f "$DIR/$name" && ! -f "$NATIVE_DIR/$name" ]]; then
        cp "$DIR/$name" "$NATIVE_DIR/$name"
        echo "fixup-sdl-runtimes: copied $name -> runtimes/$RID/native/"
    fi
done
