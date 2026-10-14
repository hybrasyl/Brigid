#!/usr/bin/env bash
#
# Build the Linux .deb and AppImage from a published linux-x64 payload.
#
# The bundled libSDL2_mixer.so links six codec libraries the system must
# supply (libopusfile, libmpg123, libvorbisfile, libFLAC, libfluidsynth,
# libmodplug). Each package handles them the way its format allows:
#
#   .deb      declares them, by the exact sonames the bundled mixer links
#             (libflac12t64 | libflac12 and so on). A release that ships a
#             different soname refuses the install instead of installing and
#             running without sound: Ubuntu 22.04 (libFLAC.so.8) does, and so
#             will any release that has moved past libFLAC.so.12.
#   AppImage  cannot declare anything, so it bundles them. linuxdeploy runs on
#             a throwaway AppDir holding only the mixer, collects the codec
#             closure and gives every library a $ORIGIN runpath; the set then
#             replaces the payload's runtimes/linux-x64/native/libSDL2_mixer.so.
#             Desktop-base libraries (SDL2, X11, Wayland, PulseAudio, GLib,
#             D-Bus, systemd) stay on the host: SDL2 must exist at exactly one
#             path (see fixup-sdl-runtimes.sh), and the rest have to match the
#             host's sound and display servers.
#
# The codec libraries come from the build host, so the AppImage needs a host
# glibc at least as new as the builder's (ubuntu-24.04: 2.39). A host without
# them, or without libjack/libpipewire, which fluidsynth links and the
# AppImage excludelist keeps on the host, runs without sound and shows the
# in-game notice.
#
# Usage: package-linux.sh <payload-dir> <version> <out-dir>
#   <payload-dir>  the published linux-x64 payload, after fixup-sdl-runtimes.sh
#   <version>      release version, e.g. 0.6.0
#   <out-dir>      where Brigid-<version>-linux-x64.deb and
#                  Brigid-<version>-x86_64.AppImage are written
#
set -euo pipefail
# package modes come from the files as staged; a group-writable umask would ship drwxrwxr-x directories
umask 022

PAYLOAD="$(realpath "${1:?payload dir required}")"
VERSION="${2:?version required}"
OUT="$(realpath -m "${3:?out dir required}")"
HERE="$(dirname "$(realpath "$0")")"
ICONS="$HERE/../icon/linux"
MIXER_REL="runtimes/linux-x64/native/libSDL2_mixer.so"

MAINTAINER="Hybrasyl <sabrael@hybrasyl.com>"

LINUXDEPLOY_URL="https://github.com/linuxdeploy/linuxdeploy/releases/download/1-alpha-20251107-1/linuxdeploy-x86_64.AppImage"
LINUXDEPLOY_SHA256="c20cd71e3a4e3b80c3483cef793cda3f4e990aca14014d23c544ca3ce1270b4d"
APPIMAGETOOL_URL="https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage"
APPIMAGETOOL_SHA256="ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0"
RUNTIME_URL="https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-x86_64"
RUNTIME_SHA256="2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d"

# Libraries the AppImage leaves to the host, on top of linuxdeploy's own excludelist.
HOST_LIBRARIES=(
    'libSDL2-2.0*'
    'libglib-2.0*' 'libgobject-2.0*' 'libgmodule-2.0*' 'libgio-2.0*'
    'libpulse*' 'libdbus-1*' 'libsystemd*'
    'libX*' 'libxcb*' 'libwayland*' 'libxkbcommon*' 'libdecor*'
    'libapparmor*' 'libcap*' 'libgcrypt*' 'libgpg-error*'
    'liblz4*' 'liblzma*' 'libzstd*' 'libffi*' 'libpcre2*' 'libbsd*' 'libmd.so*' 'libtinfo*'
    'libasyncns*' 'libdb-5*'
)

[ -x "$PAYLOAD/Brigid" ] || { echo "package-linux: $PAYLOAD/Brigid is missing or not executable" >&2; exit 1; }
[ -f "$PAYLOAD/$MIXER_REL" ] || { echo "package-linux: $PAYLOAD/$MIXER_REL is missing" >&2; exit 1; }

# Debian sorts "-" as a revision separator, so 0.6.0-dev would rank above 0.6.0; "~" ranks below it.
DEB_VERSION="${VERSION/-/\~}"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$OUT" "$WORK/tools"
# APPIMAGE_EXTRACT_AND_RUN unpacks the tools under $TMPDIR; keep that inside $WORK so the trap removes it
export TMPDIR="$WORK"

# set -e does not reach inside $(...), so every step returns explicitly and only the path goes to stdout.
fetch() {
    local url="$1" sha="$2" dest="$WORK/tools/$(basename "$1")"
    curl -fsSL --retry 3 -o "$dest" "$url" || return 1
    echo "$sha  $dest" | sha256sum --check --quiet >&2 || return 1
    chmod +x "$dest" || return 1
    echo "$dest"
}

# ── .deb ──────────────────────────────────────────────────────────────────────
DEB="$WORK/deb"
mkdir -p "$DEB/DEBIAN" "$DEB/opt/brigid" "$DEB/usr/bin" "$DEB/usr/share/applications"
cp -a "$PAYLOAD/." "$DEB/opt/brigid/"
ln -s /opt/brigid/Brigid "$DEB/usr/bin/brigid"
install -m 644 "$HERE/brigid.desktop" "$DEB/usr/share/applications/brigid.desktop"
for icon in "$ICONS"/*.png; do
    size="$(basename "$icon" .png)"
    install -D -m 644 "$icon" "$DEB/usr/share/icons/hicolor/$size/apps/brigid.png"
done
chmod -R u+rwX,go+rX,go-w "$DEB/opt/brigid"

# The first six are the bundled mixer's DT_NEEDED codecs. The rest are the .NET
# runtime's dependencies: libicu (globalization), libssl and ca-certificates (TLS;
# without the CA store, TLS to a server with a public certificate fails), tzdata
# (time zones) and libgssapi-krb5-2 (Negotiate authentication). Package names carry
# the soname, and Ubuntu 24.04 renamed some for the 64-bit time_t transition, hence
# the pairs.
cat > "$DEB/DEBIAN/control" <<EOF
Package: brigid
Version: $DEB_VERSION
Architecture: amd64
Maintainer: $MAINTAINER
Installed-Size: $(du -sk --exclude=DEBIAN "$DEB" | cut -f1)
Depends: libflac12t64 | libflac12, libmodplug1, libfluidsynth3, libmpg123-0t64 | libmpg123-0, libopusfile0, libvorbisfile3, libc6 (>= 2.34), libgcc-s1, libstdc++6, libssl3t64 | libssl3, ca-certificates, tzdata, libgssapi-krb5-2, libicu78 | libicu77 | libicu76 | libicu74 | libicu72 | libicu70
Section: games
Priority: optional
Homepage: https://github.com/hybrasyl/Brigid
Description: Dark Ages client by the Hybrasyl project
 Brigid is a modern client for the Dark Ages protocol. It needs a retail
 Dark Ages data folder, which the launcher asks for on first run.
EOF

dpkg-deb --root-owner-group -Zxz --build "$DEB" "$OUT/Brigid-$VERSION-linux-x64.deb"

# ── AppImage ──────────────────────────────────────────────────────────────────
LINUXDEPLOY="$(fetch "$LINUXDEPLOY_URL" "$LINUXDEPLOY_SHA256")"
APPIMAGETOOL="$(fetch "$APPIMAGETOOL_URL" "$APPIMAGETOOL_SHA256")"
RUNTIME="$(fetch "$RUNTIME_URL" "$RUNTIME_SHA256")"
export APPIMAGE_EXTRACT_AND_RUN=1

CODECS="$WORK/codecs"
excludes=()
for lib in "${HOST_LIBRARIES[@]}"; do excludes+=(--exclude-library "$lib"); done
(cd "$WORK" && "$LINUXDEPLOY" --appdir "$CODECS" --library "$PAYLOAD/$MIXER_REL" "${excludes[@]}" >/dev/null)

APPDIR="$WORK/Brigid.AppDir"
mkdir -p "$APPDIR/usr/bin"
cp -a "$PAYLOAD/." "$APPDIR/usr/bin/"
cp -a "$CODECS/usr/lib/." "$APPDIR/usr/bin/$(dirname "$MIXER_REL")/"
install -m 755 "$HERE/AppRun" "$APPDIR/AppRun"
install -m 644 "$HERE/brigid.desktop" "$APPDIR/brigid.desktop"
install -m 644 "$ICONS/256x256.png" "$APPDIR/brigid.png"
ln -s brigid.png "$APPDIR/.DirIcon"

echo "package-linux: AppImage bundles $(ls "$CODECS/usr/lib" | tr '\n' ' ')"
ARCH=x86_64 "$APPIMAGETOOL" --no-appstream --runtime-file "$RUNTIME" \
    "$APPDIR" "$OUT/Brigid-$VERSION-x86_64.AppImage"
