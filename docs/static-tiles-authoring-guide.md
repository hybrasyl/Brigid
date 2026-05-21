# Static Tiles — Authoring Guide

Practical walkthrough for producing a `.datf` pack of **static** floor and wall tiles for Hybrasyl. Companion to [asset-pack-format.md](asset-pack-format.md); that doc defines the pack container, this doc covers the art and the eligibility rules you need to know before drawing anything.

## What "static tiles" covers

The current `static_tiles` content type targets the simplest tile case: **single-image-per-ID** floor and wall tiles that don't animate. The renderer hooks the pack lookup into the existing tile preload pass — pack PNGs decode once at map load and feed the same atlas pipeline as legacy tiles, so render-time performance is identical regardless of source.

What it does NOT cover (yet — these are roadmapped for the future full `tiles` content type):

- **Palette-cycled tiles** — water, lava, anything where the palette rotates colors over time. The cycling overlay system runs every render frame and would visually overwrite any pack PNG, so the renderer skips pack lookup for these tile IDs entirely.
- **Frame-animated tiles** — anything driven by `gndani.tbl` (background) or `stcani.tbl` (foreground), where multiple tile IDs alternate over time. A static pack would cover only the base frame, producing a glitchy "modern frame, then legacy frame, then modern frame" cycle. Skipped automatically.

In both cases the skip is silent — the client doesn't log a warning if you ship `floor00524.png` for a cycled or animated ID. **It's your responsibility as the author to know which IDs are eligible.** Read on for guidance.

## What you need before starting

1. **A way to identify tile IDs.** When you see a floor or wall in the game, you need to know its `tileId` value to know what filename to use:
   - **Easiest:** ask the Hybrasyl content team for a tile catalog export. They have tools that dump tile-IDs with thumbnail previews.
   - **Map editor:** if you have a Dark Ages map editor (Hybrasyl team has tools), each placed tile shows its ID.
   - **In-game debug:** the client doesn't currently surface tile IDs at runtime. If you need to figure out IDs without external tools, you'll have to count entries in legacy archives — slow and error-prone, prefer a catalog export.
2. **A way to identify whether a tile is cycled or animated.** Without this, you can't tell which IDs are eligible for static replacement. Options:
   - Cross-reference your tile IDs against `gndani.tbl` (BG animations) and `stcani.tbl` (FG animations) — any ID listed there is animated, skip it.
   - For palette cycling, the data is in `mpt` palette tables (BG) and `stc` palette tables (FG). The Hybrasyl team can provide a list of cycled palette numbers.
   - Pragmatic test: drop a candidate `floor{id}.png` in a test pack, load the map. If it renders, the ID is eligible. If you see the legacy tile cycling/animating instead, it's ineligible — silently skipped.
3. **An art tool with point-filter export.** Aseprite, Photoshop, Krita, GIMP all work. Make sure your export doesn't apply bilinear scaling.
4. **A reference of what the legacy tile looks like** for visual continuity. Get extracts from the Hybrasyl team.
5. **A text editor + ZIP tool** for `_manifest.json` and pack assembly.

## Technical spec

### Floor tiles

| Aspect | Spec |
| --- | --- |
| Dimensions | **56 × 27 pixels** (matches legacy `Tile` size) |
| Pixel format | PNG with alpha. RGBA preferred. |
| Background | Floor tiles in Dark Ages are **fully opaque** — they tile edge-to-edge in the isometric grid. No transparency in floor PNGs unless the legacy tile already has transparent areas (rare). |
| Filename | `floor{id:D5}.png` — five-digit zero-padded ID matching `MapTile.Background`. |
| ID range | 1-based per the map file's storage convention. `floor00001.png` overrides what the map editor calls "tile 1." |

### Wall tiles

| Aspect | Spec |
| --- | --- |
| Dimensions | **28 pixels wide, variable height.** Match the legacy HPF height for the ID you're replacing — too tall pushes the wall up into adjacent tiles; too short leaves a gap. |
| Pixel format | PNG with alpha. Walls almost always have transparent regions (sky behind, tiles to the side). |
| Background | Transparent for the non-wall area. The wall's silhouette is the visible region. |
| Filename | `wall{id:D5}.png` — five-digit zero-padded ID matching `MapTile.LeftForeground` / `RightForeground`. |
| ID range | Foreground IDs above 10012 (per the renderer's `IsRenderedTileIndex` filter — IDs 0–12 and 10000–10012 are sentinel values, not real tiles). |

### Why no offset adjustment?

Some other pack types (`ability_icons`, `nation_badges`) deliberately offset the filename ID from the legacy frame index for various reasons. **Static tiles don't.** The number you see in the map editor / tile catalog is exactly the number you put in the filename: `MapTile.Background == 524` → ship `floor00524.png`.

## Authoring walkthrough

### 1. Identify your target IDs

Pick the tiles you want to modernize. For each, confirm:

- **Eligible?** Not cycled (water/lava/etc.), not in `gndani.tbl` / `stcani.tbl`. If you're unsure, ship a test PNG and check whether it renders.
- **Floor or wall?** This determines the filename prefix.
- **Tile ID?** Get the value from a catalog export or map editor.

Make a list before you start drawing — ideally with thumbnails of the legacy tile next to each ID so the artist has visual context.

### 2. Draw

For floors:

- 56 × 27, fully opaque, edge-to-edge legible. Test that adjacent tiles tile cleanly — Dark Ages maps lay tiles in an isometric grid where each floor neighbor's pixels touch yours; obvious seams are jarring.
- Avoid hard-coded directional shading (e.g., a strong light from the left). Maps rotate context — your tile may be placed near edges where strong directional light looks inconsistent.

For walls:

- 28 × N where N matches the legacy tile's pixel height. Get this from the legacy export.
- Transparent pixels around the wall silhouette. The renderer composites your PNG against tiles below/behind, so any non-transparent pixel paints over them.
- Avoid stretching detail to fill more height than the legacy tile occupied — that pushes the wall above where players expect it.

### 3. Write the manifest

Create `_manifest.json`:

```json
{
  "schema_version": 1,
  "pack_id": "your-pack-id-here",
  "pack_version": "0.1.0",
  "content_type": "static_tiles",
  "priority": 100,
  "covers": {
    "static_tiles": { }
  }
}
```

- `pack_id` lowercase, unique. E.g., `hybrasyl-mileth-tiles` or `studio-grass-set`.
- `pack_version` is informational — bump on updates.
- `priority` only matters if multiple `static_tiles` packs are installed simultaneously; higher wins.
- `covers` is a capability declaration — leave the inner object empty. Coverage is emergent from which PNGs you ship.

### 4. Assemble the pack

Pack contents at the **archive root** (no subfolders):

```
yourtiles.datf
├── _manifest.json
├── floor00524.png
├── floor00525.png
├── floor00526.png
├── wall12345.png
└── wall12346.png
```

Verify by opening the `.datf` in a ZIP tool — `_manifest.json` should be at the top level immediately. Some Windows ZIP tools wrap contents in a folder named after the archive; if you see your files nested, recreate the ZIP with the contents at root.

Rename `.zip` to `.datf`.

### 5. Install and test

1. Drop the `.datf` into the Dark Ages data folder (where the legacy `*.dat` archives live).
2. Start the client.
3. Check stderr for `[asset-pack]` warnings at startup. No warnings = pack registered.
4. Load a map that contains your target tile IDs. Floor PNGs render in place of legacy floor tiles for those IDs; same for walls.
5. **The map atlas is rebuilt only on map load**, so to iterate on tile art, transition between maps to see updated PNGs (not just tweak the file in-place).

### 6. Iterate

To test a modified PNG:

1. Close the client (no hot-reload).
2. Drop the updated `.datf` in place.
3. Relaunch and load a map with your target IDs.

For fast iteration, keep a working directory with PNGs + `_manifest.json` and re-zip with each test batch.

## QA checklist

Before shipping:

- [ ] All target tile IDs verified non-cycled and non-animated.
- [ ] Floor PNGs are exactly 56 × 27.
- [ ] Wall PNGs match the legacy HPF heights for their IDs.
- [ ] Floor PNGs are fully opaque (no accidental transparency in tiled floors).
- [ ] Wall PNGs have transparent backgrounds (so they composite cleanly).
- [ ] Adjacent floor tiles tile without obvious seams (test on an actual map, not in isolation).
- [ ] `_manifest.json` is valid JSON.
- [ ] `pack_id` is unique and lowercase.
- [ ] `content_type` is exactly `static_tiles`.
- [ ] Filenames match `floor{id:D5}.png` / `wall{id:D5}.png`.
- [ ] Pack loads with no `[asset-pack]` stderr warnings.
- [ ] On a map with cycled water (e.g., Mileth river), shipping a `floor{cycledId}.png` is silently skipped — animation continues unaffected.
- [ ] On a map with animated tiles, shipping a `floor{animatedId}.png` is silently skipped — animation continues with all-legacy frames.
- [ ] Removing the pack restores legacy tiles exactly — confirms no state pollution.
- [ ] A deliberately-corrupted PNG falls back to legacy without crashing or visual glitching.

## Troubleshooting

- **Pack doesn't load** — check stderr for `[asset-pack]` warnings. Most common: missing `_manifest.json`, files nested inside a folder, wrong `content_type`, malformed JSON.
- **Floor PNG appears with wrong colors / palette artifacts** — modern packs are RGBA, not palettized. Your art tool may be exporting indexed-color PNGs; switch to RGBA8888.
- **Floor PNG appears at wrong position / overlaps neighbors** — dimensions must be exactly 56 × 27. Larger sizes get downscaled or cropped by the atlas builder.
- **Wall PNG appears too tall / floats above the floor** — height doesn't match legacy. Walls are positioned bottom-aligned to a fixed origin; taller PNGs extend upward. Match the legacy height.
- **Pack PNG doesn't show up; legacy tile renders instead** — the ID is probably cycled or animated. Test against a known non-special ID first to verify your pack loads at all, then check eligibility for the failing ID.
- **Pack PNG shows up briefly then reverts to legacy** — this can happen if the cycling overlay starts mid-frame. Should not happen for non-cycled IDs; if it does, your eligibility check was wrong.
- **Some pack PNGs work, others don't** — almost always means the working IDs are eligible (static) and the broken IDs are cycled or animated. Cross-reference with `gndani.tbl` / `stcani.tbl`.

## Reference

- Format spec: [asset-pack-format.md](asset-pack-format.md)
- Pack class: [Chaos.Client.Data/AssetPacks/StaticTilePack.cs](../Chaos.Client.Data/AssetPacks/StaticTilePack.cs)
- Renderer hook: `Phase 2.5` in `MapRenderer.PreloadMapTiles` ([MapRenderer.cs](../Chaos.Client.Rendering/MapRenderer.cs))
- Legacy sources:
  - Floor tiles: tileset entries from `seo*.dat` archives, palettized via `mpt` palette tables.
  - Wall tiles: `stc{tileId:D5}.hpf` entries in `Ia.dat`, palettized via `stc` palette tables.
- Animation tables: `gndani.tbl` (BG), `stcani.tbl` (FG) — entries here mark animated IDs.
