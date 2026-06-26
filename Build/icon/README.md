# App icon source

Drop a square **1024x1024 PNG** here named exactly:

    Brigid.png

The macOS release job (`Build/macos/make-app.sh`) downsamples it into a full
`.iconset` and runs `iconutil` to produce `Brigid.icns` inside the app
bundle. PNG with alpha/transparency is fine.

If this file is absent, the build still succeeds, the bundle just uses the
default app icon.
