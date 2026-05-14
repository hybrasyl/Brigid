# Legend Mark Icons — Authoring Guide

Practical walkthrough for producing a `.datf` pack of **legend mark icons** — the small glyphs shown beside each entry in a player's Legend tab (self profile → Legend). Companion to [asset-pack-format.md](asset-pack-format.md); that doc defines the pack container, this doc covers the art itself.

## What a legend mark icon is

A legend mark is a server-issued entry in the character's legend — achievements ("Reached level 99"), rites of passage ("Deoch's Mark"), class changes, marriage, specific quest completions, etc. Each entry pairs a colored text line with a **small icon** (~20×20 pixels) that indicates the category or source of the mark.

In the legacy client, these icons come from `legends.epf` inside `national.dat`, rendered with `legend.pal` (palette 3 of that archive). Every icon ID the server sends is a raw byte used directly as a frame index into that sheet. The modern pack swaps individual frames for PNGs.

## What you need before starting

1. **A way to view the legacy icons.** You'll want to match the general silhouette / style of the original sheet if your pack is a partial override. Options:
   - Open `national.dat` with a DALib-aware tool (the Hybrasyl team's internal tools, or a custom EPF viewer) and inspect `legends.epf` frame-by-frame.
   - Run the stock client, accumulate some legend marks in a scratch character, and screenshot the Legend tab.
   - Ask the Hybrasyl team for a reference sheet export — not every artist needs to go fishing in the archives.
2. **A PNG-capable art tool.** Aseprite is ideal for pixel art at these dimensions; Photoshop, Krita, GIMP, or Procreate also work. The icons are small enough that vector tools are overkill.
3. **A text editor** for authoring `_manifest.json` — any editor is fine.
4. **A ZIP tool** — Windows Explorer's built-in ZIP works; 7-Zip, WinRAR, or the `zip` CLI are all fine too.

## Technical spec

| Aspect | Recommendation |
| --- | --- |
| Dimensions | **20×20** or **21×20** pixels for drop-in legacy replacement. Larger sizes work mechanically (the UI row adapts to the PNG's actual pixel dimensions and pushes the label right) but will overflow the row's ~19px height until a future Tier 3 panel-layout pass gives icon cells more room. For v1, author at legacy scale. |
| Pixel format | PNG with alpha channel. 8-bit RGBA is fine; 8-bit indexed with a transparent index also works if your tool writes a real alpha channel. |
| Background | Fully transparent. Do not bake in a row background color — the row's own background shows through. |
| Color palette | No constraint. The renderer treats PNGs as plain RGBA and does not apply `legend.pal` dyeing (that only happens on the legacy EPF fallback path). You have the full 16.7M color range. |
| Filename | `legend{iconId:D4}.png` — lowercase, four-digit zero-padded ID, `.png` extension. Case-insensitive but lowercase is convention. |
| ID range | **0-based**, matching the server wire protocol. `legend0000.png` replaces legacy EPF frame 0 (server icon ID 0). Contrast with `nation_badges` / `ability_icons`, which are 1-based — do not carry that convention across. |

### Why 0-based here when other packs are 1-based?

The legacy EPF sheets for ability icons and nation badges are consumed 1-based by legacy client code (frame 0 is reserved as a null/missing frame, with real content starting at frame 1). `legends.epf` has no such reservation — frame 0 is a real icon. The wire-protocol byte in the server's legend packet is used directly as the EPF frame index, so the pack convention naturally matches: server sends `icon = 0` → pack looks up `legend0000.png`.

## Authoring walkthrough

### 1. Pick which IDs you want to ship

Your pack can cover:

- **Targeted replacement** — ship PNGs only for specific icon IDs, leaving everything else on legacy. Good for modernizing a handful of frequently-seen marks.
- **Full replacement** — ship PNGs for every ID the server uses. Check with the Hybrasyl content team for the current range; legacy `legends.epf` typically has content through the low double digits.
- **Expansion** — ship PNGs for new IDs beyond what legacy has. Hybrasyl can then issue new legend marks with those IDs without a client update.

A pack can mix all three — the replace-vs-additive distinction is emergent from which files you include, not declared in the manifest.

### 2. Draw the icons

At 20×20 or 21×20. The label is drawn to the right of the icon with a 5px gap, and is vertically centered against the icon's height — so an icon taller than the default pushes the label down and the row below it gets visually crowded. Keep it legacy-scale.

Avoid:

- Opaque fills in corners — the row's background shows through alpha, so crisp silhouettes read better than full rectangles.
- Very thin 1px details — these fade out at the small render size and at any future scaled-down display.
- Text inside the icon — the label is the row's text surface; icons that try to be tiny text labels compete with it.

Aim for:

- Strong silhouettes readable at 20×20.
- A small set of dominant colors that differentiate from other icons in the player's likely legend view.
- Consistent visual weight across IDs if you're shipping many — otherwise the legend tab looks jumbled.

### 3. Write the manifest

Create `_manifest.json`:

```json
{
  "schema_version": 1,
  "pack_id": "your-pack-id-here",
  "pack_version": "0.1.0",
  "content_type": "legend_mark_icons",
  "priority": 100,
  "covers": {
    "legend_mark_icons": { }
  }
}
```

- `pack_id` should be lowercase and unique — e.g., `hybrasyl-legend-marks` or `artistname-legendpack`. Used for logging; collision with another installed pack just means the higher-priority one wins, but descriptive IDs make debug output readable.
- `pack_version` is semver, informational. Bump when you ship updates so support can ask "which version?" meaningfully.
- `priority` only matters if multiple legend-mark-icon packs are installed at once; higher wins. Default `100` is fine for a solo pack.
- The `covers` block is a capability declaration, not a coverage list. You do not enumerate which IDs you ship — the client discovers that from which PNG files are in the archive.

### 4. Assemble the pack

Put `_manifest.json` and all your `legend{id:D4}.png` files at the **root** of the ZIP — not inside a subdirectory. Windows ZIP tools sometimes put files inside a folder named after the ZIP; that breaks pack detection.

```
yourpack.datf
├── _manifest.json
├── legend0000.png
├── legend0001.png
├── legend0005.png
└── legend0012.png
```

Verify by opening the `.datf` in your ZIP tool: `_manifest.json` should be immediately visible at the top level, not inside a folder.

Rename the final `.zip` to `.datf`.

### 5. Install and test

1. Copy the `.datf` into the Dark Ages data folder (the directory containing the legacy `*.dat` archives).
2. Start the client.
3. Check stderr / the client log for any `[asset-pack]` warnings — missing manifest, malformed JSON, unknown content_type, or schema mismatch will all be reported there. No warning = pack registered successfully.
4. Log in on a character that has legend marks matching your shipped IDs. Open self profile → Legend tab.
5. Your PNGs should render in place of the legacy icons for those IDs. IDs you didn't ship fall back to legacy.

### 6. Iterate

To test a modified PNG:

1. Close the client (hot-reload isn't supported).
2. Drop the updated `.datf` in place (or rebuild the ZIP).
3. Relaunch.

For fast iteration, keep a loose working directory with your PNGs + `_manifest.json`, and re-zip with each test batch. A tiny PowerShell / bash one-liner can automate the repack.

## QA checklist

Before shipping a pack to players:

- [ ] Every PNG opens cleanly in at least two different image tools (catches format-edge-case corruption).
- [ ] Every PNG has a transparent background (drop the pack on a bright-red mock row to verify no accidental opaque fills).
- [ ] `_manifest.json` validates as JSON (any online JSON validator works).
- [ ] `pack_id` is unique and lowercase.
- [ ] `content_type` is exactly `legend_mark_icons`.
- [ ] Filenames are `legend{id:D4}.png` (four digits, no separators, lowercase).
- [ ] The pack loads with no `[asset-pack]` stderr warnings.
- [ ] Icons appear in the Legend tab at the expected IDs.
- [ ] Removing the pack restores legacy icons (confirms your pack doesn't accidentally corrupt the legacy fallback — the client should behave identically to the unpacked state).
- [ ] A deliberately-corrupted PNG (truncate a file in a test pack) falls back to legacy for that one ID without crashing or visually glitching.

## Troubleshooting

- **Pack doesn't load at all** — check stderr for `[asset-pack]` warning lines. Most common: missing `_manifest.json`, files nested inside a subfolder rather than at ZIP root, or `content_type` typo.
- **Some icons show legacy despite being in the pack** — filename typo. Filenames are case-insensitive but must match `legend{id:D4}.png` exactly. `legend0001.png` works; `legend001.png`, `legend_0001.png`, `legend0001.PNG` all fail except `.PNG` (that extension case is fine).
- **Icon appears too large / pushes text off the row** — your PNG is bigger than the legacy ~20×20 footprint. The UI row sizes to the actual image dimensions. Until the Tier 3 panel-layout pass, stay at legacy scale.
- **Icon appears but text is garbled** — probably unrelated; legend text comes from the server, not the pack. Check with the server team if the text itself is wrong.
- **Two legend-mark-icon packs installed** — higher `priority` wins; the other is logged and ignored. Only one pack of this `content_type` is active at a time.

## Reference

- Format spec: [asset-pack-format.md](asset-pack-format.md)
- Legacy source: `legends.epf` inside `national.dat`, rendered with `legend.pal` (palette 3 of the same archive). See [controlFileList.txt](../controlFileList.txt) for the full legacy archive catalog.
- Pack class: [Chaos.Client.Data/AssetPacks/LegendMarkIconPack.cs](../Chaos.Client.Data/AssetPacks/LegendMarkIconPack.cs)
- Renderer hook: `UiRenderer.GetLegendMarkIcon(byte iconId)` in [Chaos.Client.Rendering/UiRenderer.cs](../Chaos.Client.Rendering/UiRenderer.cs)
- Consumer: [SelfProfileLegendTab.cs](../Chaos.Client/Controls/World/Popups/Profile/SelfProfileLegendTab.cs)
