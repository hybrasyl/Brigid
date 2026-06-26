#!/usr/bin/env bash
#
# Restore the runtimes/<rid>/native/ layout that Chaos.Client's custom
# DllResolver expects.
#
# A self-contained `dotnet publish` flattens MonoGame's SDL2 native to the
# payload root, but DllResolver (Chaos.Client/DllResolver.cs) probes only
# AppContext.BaseDirectory/runtimes/<rid>/native/ (and .../runtimes/<platform>/
# native/). Its last-ditch NativeLibrary.TryLoad(name) fallback can't match the
# versioned SDL2 filenames on macOS/Linux. So copy the flattened SDL2 back into
# the runtimes layout. SDL2_mixer is already shipped there as Content.
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
    linux-*) SDL_NAMES=(libSDL2-2.0.so.0 libSDL2.so.0 libSDL2.so) ;;
    win-*)   SDL_NAMES=(SDL2.dll) ;;
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
