# App icon source

Drop a square **1024×1024 PNG** here named exactly:

    Chaos.Client.png

The macOS release job (`Build/macos/make-app.sh`) downsamples it into a full
`.iconset` and runs `iconutil` to produce `Chaos.Client.icns` inside the app
bundle. PNG with alpha/transparency is fine.

If this file is absent, the build still succeeds — the bundle just uses the
default app icon.

(The existing `Chaos.Client/ChaosClientIcon.ico` is only 32×32, too small for a
macOS app icon, which is why a dedicated high-res source lives here.)
