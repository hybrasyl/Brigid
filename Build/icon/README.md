# App icon source

`Brigid.svg` is the vector master. All raster icons are generated from it:
this directory's `Brigid.png` (1024x1024), plus `Brigid/BrigidIcon.png`
(256x256 window icon) and `Brigid/BrigidIcon.ico` (16-256px Windows exe
icon). To regenerate, rasterize the SVG at 1024x1024 and Lanczos-downscale
for the smaller sizes (direct small-size vector renders come out too thin).

The macOS pipeline consumes the square **1024x1024 PNG** named exactly:

    Brigid.png

The macOS release job (`Build/macos/make-app.sh`) downsamples it into a full
`.iconset` and runs `iconutil` to produce `Brigid.icns` inside the app
bundle. PNG with alpha/transparency is fine.

If this file is absent, the build still succeeds, the bundle just uses the
default app icon.
