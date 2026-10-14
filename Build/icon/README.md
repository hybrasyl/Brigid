# App icon sources

Brigid follows the house icon standard of the Erisco apps: the round medallion on Windows, the rounded tile on macOS, inset into Apple's grid because macOS draws app icons as tiles, and the same tile full-bleed on Linux.

| File | What it is | Used for |
| --- | --- | --- |
| `Build/icon/Brigid-medallion.webp` | The medallion master, 1024x1024 with alpha (from the house logo set, "Hyb Logo Green - Brigid") | The source of `Brigid/BrigidIcon.ico` and `Brigid/BrigidIcon.png` |
| `Build/icon/Brigid.svg` | The tile master, vector | The source of `Build/icon/Brigid.png` |
| `Build/icon/Brigid.png` | The tile, inset into Apple's grid: 824x824 of art centred on a 1024x1024 transparent canvas | The macOS bundle. `Build/macos/make-app.sh` turns it into `Brigid.icns`. Also the source of `Brigid/BrigidIconMac.png` |
| `Brigid/BrigidIcon.ico` | The medallion at 16, 24, 32, 48, 64, 128 and 256 px | The Windows exe icon (`ApplicationIcon`) |
| `Brigid/BrigidIcon.png` | The medallion at 256 px | The window icon on Windows, set at runtime by `ChaosGame.SetWindowIcon` |
| `Brigid/BrigidIconMac.png` | The inset tile at 512 px | The window icon on macOS. There SDL sets the window icon as the Dock icon, so it must be the same art as `Brigid.icns` |
| `Brigid/BrigidIconLinux.png` | The full-bleed tile at 256 px | The window icon on Linux |
| `Build/icon/linux/NxN.png` | The full-bleed tile at 16, 24, 32, 48, 64, 128, 256 and 512 px | The `.deb` installs each into `hicolor/NxN/apps`; the AppImage uses the 256 |

Commit every generated file. CI does not need ImageMagick.

## Regenerate

Run from the repository root. Always call `magick`, never bare `convert`: on Windows `convert` is the FAT-to-NTFS volume converter.

```sh
# Windows exe icon and the Windows window icon, from the medallion
magick Build/icon/Brigid-medallion.webp -filter Lanczos -define icon:auto-resize=256,128,64,48,32,24,16 Brigid/BrigidIcon.ico
magick Build/icon/Brigid-medallion.webp -filter Lanczos -resize 256x256 -strip -define png:compression-level=9 PNG32:Brigid/BrigidIcon.png

# macOS tile, from the SVG. A full-bleed tile shows about 24% larger than the icons next to it in the Dock, so inset it.
magick -background none -density 384 Build/icon/Brigid.svg -resize 1024x1024 tile-1024.png
magick tile-1024.png -filter Lanczos -resize 824x824 -background none -gravity center -extent 1024x1024 -strip PNG32:Build/icon/Brigid.png

# macOS window icon, from the inset tile
magick Build/icon/Brigid.png -filter Lanczos -resize 512x512 -strip -define png:compression-level=9 PNG32:Brigid/BrigidIconMac.png

# Linux window and package icons, from the full-bleed tile (tile-1024.png above, before the inset)
magick tile-1024.png -filter Lanczos -resize 256x256 -strip -define png:compression-level=9 PNG32:Brigid/BrigidIconLinux.png
for n in 16 24 32 48 64 128 256 512; do
  magick tile-1024.png -filter Lanczos -resize ${n}x${n} -strip -define png:compression-level=9 PNG32:Build/icon/linux/${n}x${n}.png
done
```

Downscale from a large raster with Lanczos. A small vector render comes out too thin.

If `Build/icon/Brigid.png` is absent, the macOS build still succeeds, and the bundle uses the default app icon.
