# `.datf` Asset Packs — Content Type Catalog

Code-derived reference for every `.datf` asset-pack `content_type` Brigid understands: entry naming,
ID conventions, manifest fields actually consumed, the legacy asset each type overrides, and the
constraints enforced in code.

Counterpart to `controlFileList.txt` (which catalogs the *legacy* prefab/EPF/SPF world). Same
altitude: reference, not tutorial. Artist-facing narrative — authoring workflow, dye rationale,
roadmap — lives in the document repo's `plans/brigid/asset-pack-format.md` and its
`*-asset-pack-scoping.md` siblings; see [Further reading](#further-reading).

Ground truth is `Brigid.Data/AssetPacks/`. When this file and any other doc disagree, the code wins
and this file should be corrected.

---

## What a pack is

A `.datf` is a **ZIP archive with a different extension** — no header, no framing, no encryption.
Rename to `.zip` and any ZIP tool opens it. The extension exists so casual data-folder poking
doesn't surface "a zip full of PNGs"; it is not a security boundary.

A pack ships modern assets the client prefers over the legacy `.dat`/`.epf`/`.spf` sheets.
Resolution is **per asset**, not per pack:

1. The lookup asks each registered pack of this content type, the highest `priority` first, and at the same priority in registration order (file-name order).
2. Hit → modern asset used.
3. Miss, or decode failure → the next pack is asked; after the last pack, the legacy asset for that ID alone. Three types are exceptions: the first pack that covers a creature owns it, and a bad entry shows the placeholder; the first pack that has an ambient id owns that bed, and a bad entry gives no bed; and one `ui_theme` pack is active, so a part that it does not have takes the flat look (see "Several packs of one type, and conflicts").

Decode failure is deliberately indistinguishable from absence (`AssetPack.TryGetImage` swallows read
and decode errors), so a corrupt entry degrades to legacy instead of breaking a surface.

---

## Load pipeline

`Brigid.Data/AssetPacks/AssetPackRegistry.cs`, entered from `DataContext.Initialize()`
(`Brigid.Data/DataContext.cs:61`) before any repository is constructed.

### Discovery location

`AppPaths.AssetsDir` (`Brigid.Data/AppPaths.cs:49`) — **not** the Dark Ages data folder:

| Platform    | Path                                  |
| ----------- | ------------------------------------- |
| Windows     | `%LOCALAPPDATA%\Erisco\Brigid\assets` |
| macOS/Linux | `~/.config/Erisco/Brigid/assets`      |

The Unix split is deliberate: .NET maps `LocalApplicationData` to `~/.local/share`, but the house
standard targets `~/.config`, which is `SpecialFolder.ApplicationData` there. If `GetFolderPath`
returns `""` (stripped env, `HOME` unset), the base falls back to `AppContext.BaseDirectory` so
packs never scatter into the CWD.

Scan is `AssetPackRegistry.EnumeratePackFiles(packDir)` — extension `.datf` only, in any case (`Wood.DATF` registers on Linux too), top level only, no recursion — sorted by file name (`InRegistrationOrder`, ordinal, case ignored), which is the registration order. The legacy migration below uses the same scan. A folder that exists but cannot be listed (no permission, a network drive that is gone) logs `cannot list the asset-pack folder '...', so the client starts with no packs: ...`, and the client starts with no packs; the main menu shows a "No Packs" alert that names the folder, once for each run of the client (BRIG-129).

### Validation ladder

Per file, in order; any failure skips **that pack only** and logs to `notice-debug.log` (through
`DataDiagnostics.Log`) with the `[asset-pack]` prefix. Startup is never aborted by a bad pack.

| Check                                     | Message on failure                                                       |
| ----------------------------------------- | ------------------------------------------------------------------------ |
| `ZipFile.OpenRead`                        | `failed to open pack {name}: {ExceptionType}: {message}`                 |
| `_manifest.json` entry exists at ZIP root | `pack {name} is missing _manifest.json; skipping`                        |
| JSON deserializes non-null                | `pack {name} has empty or invalid manifest; skipping`                    |
| `schema_version` present                  | `pack {name} has no schema_version; skipping`                            |
| `schema_version >= 1`                     | `pack {name} declares schema_version=N which is not a valid version; skipping` |
| `schema_version <= 1`                     | `pack {name} declares schema_version=N which is newer than supported (1); skipping` |
| `pack_id` present and not blank           | `pack {name} has no pack_id; skipping`                                   |
| `pack_id` is not `none` (reserved for `UiTheme: none`) | `pack {name} has pack_id 'none', which is reserved (...); skipping` |
| a `ui_theme` `pack_id` has only ASCII letters, digits, `-` and `_` (so `brigid.cfg` keeps it the same) | `pack {name} has pack_id '...', which a ui_theme cannot have (...); skipping` |
| `pack_id` not already registered (any type, case ignored) | `pack {name} has pack_id '...', which the registered {type} pack '...' already has; skipping` |
| `content_type` matches a factory key      | `pack {name} has unknown content_type='...'; skipping`                   |

`AssetPackRegistry.GetManifestRejection` makes the manifest checks. It accepts `schema_version` from 1 through `SUPPORTED_SCHEMA_VERSION` (currently `1`). It refuses an absent, zero or negative `schema_version`, and an absent or blank `pack_id`. Creidhne and Taliesin also refuse an absent or zero `schema_version` and an absent `pack_id`, but they accept a blank `pack_id`.

No checksum, no signature, no entry-name validation, no image-format validation at load. All decode
is lazy, at first lookup.

### Several packs of one type, and conflicts

A content type can have several packs (BRIG-15). The registry keeps them in order of the manifest **`priority`**, the highest first. Packs with the same priority stay in **registration order**, and the registry registers the `*.datf` files in **file-name order** (ordinal, case ignored), so the order is the same on every OS. A lookup asks for a key and tries each pack of the type in that order. So when two packs cover a key, **the pack with the higher priority wins**, and at the same priority **the pack that registered first wins**.

What a pack does with a bad entry depends on the type:

| Types | Key | Bad entry (does not decode) |
| --- | --- | --- |
| `ability_icons`, `nation_badges`, `npc_portraits`, `static_tiles`, `legend_mark_icons`, `world_maps`, `town_maps` | one image (for `npc_portraits`, the portrait key) | the next pack is asked, then legacy art |
| `ui_sprite_overrides` | one frame of one file (`{file.ext}/{frame}`), so one file can take frames from two packs | the next pack is asked, then legacy art |
| `music`, `sound_effects` | the id | the next pack is asked, then the legacy sound |
| `item_icons` | the sprite id; the pack that gives the image also decides the dye | the next pack is asked, then the legacy EPF |
| `creature_sprites` | the sprite id; the first pack that covers it owns the whole creature (frames, pair count, bounding box) | the placeholder |
| `ambient_sounds` | the ambient id; the first pack that has it owns the bed (the sound and the loop flag) | no bed, and one line in `notice-debug.log` (there is no legacy fallback) |
| `ui_theme` | none: one theme is active (the selected `pack_id`, else the first), and themes share no keys | the flat look; never another theme's part |

At registration the registry compares the new pack's keys with the packs already registered for its type, and logs one warning for each pair that shares keys: both `pack_id`s, the count, the first five keys, which pack a lookup asks first and why, and that an entry of that pack that does not decode passes the lookup to the other pack. A key is a `.png` entry name for the image types (other files, such as the manifest or a README, are not keys), the id for `music` and `sound_effects` (`sfx_1.wav` and `sfx_0001.mp3` are the same key), the sprite id for `creature_sprites`, and the portrait key for `npc_portraits`. The registry writes its messages through `DataDiagnostics.Log`, so they are in `notice-debug.log`.

To make one pack win, give it a higher `priority` (the default is 100). A temporary pack, for example a holiday pack, uses this: at priority 200 it replaces the art of the packs below it while it is installed, and it stops when it is removed. At the same priority the file name decides. The sort compares characters, not numbers: `items_9001.datf` sorts after `items_20001.datf`. Zero-pad the numbers in file names (`items_09001`) when the order matters. A convention for chunked content, for example `items_20001-21000.datf` and `items_21001-22000.datf`, is to cover separate keys so that no pack overlaps another.

### Legacy migration

One-time, best-effort copy of `{DataContext.DataPath}/hybrasyl-data/*.datf` into `AssetsDir`, smoothing pre-`AppPaths` manual installs. Gated by a `.legacy-migrated` marker file written into `AssetsDir` whether or not anything was found — the gate is "has it ever run", not "is the dir empty", so clearing the assets dir to force a launcher re-fetch never resurrects stale packs. Copies to `{dst}.tmp` then `File.Move` so an interrupted copy can't leave a truncated archive. Per-file `File.Exists(dst)` skip means launcher-placed content is never clobbered. A failure never stops the start: a failed copy skips that pack, and a failure of the scan or the copy loop logs `cannot migrate the legacy asset packs from '...': ...`. A marker that cannot be written is not logged.

### No hot reload

`Initialize()` latches on a static `Initialized` bool. A pack dropped in mid-session is not
discovered, and even after re-registration an already-cached texture keeps serving until the owning
renderer's cache is cleared. Restart the client.

### Adding a content type

Three edits: one line in `AssetPackRegistry.Factories`; the accessors, which are an ordered `…Packs` sequence and a key lookup (and a `CoverageKeys` override when the key is not the `.png` entry name, so that the overlap warning names the right keys); and the pack class deriving from `AssetPack` (or `AudioPack` for byte-serving types).

---

## Manifest schema

`_manifest.json`, exact name, at the ZIP root. Deserialized into `AssetPackManifest`
(`Brigid.Data/AssetPacks/AssetPackManifest.cs`) via System.Text.Json.

```json
{
  "schema_version": 1,
  "pack_id": "hybrasyl-nation-badges",
  "pack_version": "0.1.0",
  "content_type": "nation_badges",
  "priority": 100,
  "covers": { "nation_badges": {} }
}
```

| Field            | Type                                     | Default | Read by code                                                       |
| ---------------- | ---------------------------------------- | ------- | ------------------------------------------------------------------ |
| `schema_version` | int (required)                           | none    | Yes — must be 1 through the supported version                      |
| `pack_id`        | string (required)                        | none    | Must not be blank, and unique among the installed packs of every type (case ignored); the pack registered first keeps it. A `ui_theme` id has only ASCII letters, digits, `-` and `_`. Log messages, and the `ui_theme` selection (`UiTheme:` in `brigid.cfg`) |
| `name`           | string                                   | none    | Display name (`DisplayName`; the `pack_id` when absent). Shown in the theme drop-downs of Options and the launcher |
| `pack_version`   | string                                   | `""`    | **Nothing reads this.** Informational                              |
| `content_type`   | string                                   | `""`    | Yes — factory discriminator                                        |
| `priority`       | int                                      | `100`   | Yes — order of the packs of a type: highest first, then file name  |
| `covers`         | `Dictionary<string, CoverageEntry>`      | `{}`    | Only the sub-fields below                                          |

`covers` is a **capability** declaration, not a coverage range — actual coverage is emergent from
which entries the archive ships. You never enumerate sprite IDs in the manifest.

### `covers.<category>` sub-fields

| Field        | Type                         | Consumed by                                          |
| ------------ | ---------------------------- | ---------------------------------------------------- |
| `dimensions` | `int[]` `[w, h]`             | **`NpcPortraitPack` only.** See note below           |
| `dyeable`    | `int[]` (1-based item IDs)   | `ItemPack` only — `covers.item_icons.dyeable`; when present, the exact dyeable set |
| `no_dye`     | `int[]` (1-based item IDs)   | `ItemPack` only — `covers.item_icons.no_dye`; the exceptions to ramp detection (Taliesin writes it) |
| `portraits`  | `Dictionary<string, string>` | `NpcPortraitPack` only — **required**, it *is* the naming convention |
| `"<id>"`     | object, one for each id      | `AmbientPack` only — `covers.ambient_sounds["<id>"].loop`. See `ambient_sounds` |

**`dimensions` caveat.** Widely documented as `[32, 32]` for `ability_icons`; no code reads it there.
The 31→32 icon offset is applied unconditionally by `IconTexture.Modern` regardless of what the
manifest says. `npc_portraits` is the sole consumer, and it enforces the value hard.

For every content type not listed above, `covers` may be `{}` — or omitted entirely — with no
behavioral difference.

---

## Content types at a glance

| `content_type`        | Entry pattern                                        | ID base            | Lookup                      | Legacy fallback                                   |
| --------------------- | ---------------------------------------------------- | ------------------ | --------------------------- | ------------------------------------------------- |
| `ability_icons`       | `{skill\|spell}{id:D4}.png`                          | 1                  | `TryGetIconImage`           | `skill001.epf` / `spell001.epf`                   |
| `item_icons`          | `item{id:D5}.png`                                    | 1, to 32767        | `ItemIconSource.Load`       | item EPF sheets in `legend.dat`                   |
| `nation_badges`       | `nation{id:D4}.png`                                  | 1                  | `TryGetBadgeImage`          | `_nui_nat.spf` frame `id - 1`                     |
| `npc_portraits`       | manifest-declared filenames                          | n/a (keyed)        | `TryGetNpcIllustration`     | `npcbase.dat` SPF frame 0                         |
| `static_tiles`        | `floor{id:D5}.png` / `wall{id:D5}.png`               | raw map tile id    | `TryGetFloor/WallImage`     | tileset / `stc{id:D5}.hpf`                        |
| `legend_mark_icons`   | `legend{id:D4}.png`                                  | **0**              | `TryGetLegendMarkImage`     | `legends.epf` frame `id`                          |
| `ui_sprite_overrides` | `{file.ext}/{frame:D4}.png`                          | frame 0            | `TryGetUiSprite`            | any `setoa.dat` / `cious.dat` EPF/SPF frame       |
| `creature_sprites`    | `creature_sprites/creature_{id:D5}/stand/{n\|e}_001.png` | 1              | `GetCreaturePackFor`        | creature MPF                                      |
| `music`               | `music_{id}.{ext}`                                   | raw server id      | `MusicPacks`                | `{DataPath}/music/{id}.mus`                       |
| `sound_effects`       | `sfx_{id}.{ext}`                                     | raw server id      | `SfxPacks`                  | `legend.dat` `{id}.mp3`                           |
| `ambient_sounds`      | `amb_{id}.{ext}`                                     | ambient id, 1–255  | `GetAmbientPackFor`         | none — no pack entry means no bed                 |
| `world_maps`          | `{fieldName}.png`                                    | named, not numeric | `TryGetFieldImage`          | `{field}.epf` + `{field}.pal` in `setoa.dat`      |
| `town_maps`           | `town_{mapId:D5}.png`                                | raw map id         | `TryGetTownMapImage`        | `national.dat` `_t*` five-layer composite         |
| `ui_theme`            | `theme.json` + the files it names                    | role, not numeric  | `GetUiThemePack()` (active) | the flat `DialogPalette` look of the primitive    |

All lookups are case-insensitive. Unless a section says otherwise, entries live at the ZIP root and
subfolders are ignored.

---

## `ability_icons`

`Brigid.Data/AssetPacks/IconPack.cs` — skill and spell icons.

```csharp
return TryGetImage($"{prefix}{spriteId:D4}.png", out image);   // IconPack.cs:34
```

- Regex: `^(skill|spell)\d{4}\.png$`. Prefixes supplied by `UiRenderer.GetSkillIcon` / `GetSpellIcon`.
- **1-based**, matching legacy slot numbering. Guard: `spriteId <= 0` → miss.
- Overrides `DataContext.PanelSprites.GetSkillIcon` / `GetSpellIcon`.
- Manifest fields read: **none**.
- IDs past the legacy populated range are new content, not replacements — the client shows them
  whenever the server references that sprite ID.

**The 31 vs 32 offset.** Legacy icons are 31×31; modern are 32×32. The pack does not enforce this —
the offset lives downstream in `Brigid.Rendering/IconTexture.cs`, where `Modern(t)` yields
`(texture, -1, -1)` and `Legacy(t)` yields `(texture, 0, 0)`. The extra pixel overruns the slot's
outer border padding rather than bleeding into neighbours. `UiRenderer.GetHalfSizeSpellIcon`
rescales to 15×15, so no offset applies on that path.

**Learnable/locked states are not shipped.** Legacy had three sheets per family (`skill001` known,
`skill002` learnable, `skill003` locked) that were the same art tinted. Modern packs ship **one** PNG
per ID; the client tints at render time (`CornflowerBlue` learnable, `DimGray` locked).

---

## `item_icons`

`Brigid.Data/AssetPacks/ItemPack.cs` — every item surface: ground items, the dialog item portrait, the inventory, bank, shop, item menu, exchange, and both equipment tabs.

```csharp
return HasEntry($"item{spriteId:D5}.png");
return TryGetImage($"item{spriteId:D5}.png", out image);
```

- Regex: `^item\d{5}\.png$`. **5 digits** — legacy IDs reach 15,965+, and modern icons use 20001–32767 (BRIG-77).
- **1-based**. Guard: `spriteId <= 0` → miss.
- **The ceiling is 32767.** The server sends an item sprite as `sprite + 0x8000` in a `u16`, so the client removes the flag with `& 0x7FFF`. A sprite above 32767 wraps on the wire.
- **One resolver for every surface:** `Brigid.Rendering/ItemIconSource.cs`. It removes the flag, asks the `item_icons` packs in their order (priority, then file name; a bad entry passes to the next pack), then reads the legacy EPF + palette. `ItemRenderer` (ground items, dialog portrait) and `UiRenderer.GetItemIcon` (the other surfaces) both call it.
- **Icon offset.** A pack icon draws at `floor((31 − size) / 2)` on each axis in the 31×31 slot (`IconTexture.FromPack`): a 32×32 icon at `-1,-1`, as a modern ability icon does, and a smaller icon centred. The client does not clamp a larger icon: a 48×48 icon draws at `-9,-9` and covers the next slots. Taliesin accepts 16 to 32 px, so only a pack made by hand can have one. A legacy icon draws at `0,0`. On the ground, a pack PNG is centred as a tightly cropped image.
- **Legacy dye** goes through `AislingDrawDataRepository.ApplyDye`, which leaves a palette without the canonical dye ramp unchanged.

**Which items take the dye (BRIG-41).** `ItemPack.IsDyeable(spriteId, image)` decides, for the pack that gives the image:

1. An id of 20001 or more (`ItemSpriteIds.MODERN_FIRST`) is **never** dyed by this pass. Modern icons are painted art, and a painted color can match the ramp by chance; mask dye for them is BRIG-112.
2. When the manifest has `covers.item_icons.dyeable`, that list is the exact set. Only the old scaffold writes it.
3. Otherwise an id in `covers.item_icons.no_dye` is not dyeable, and any other id is dyeable when its PNG contains at least one visible pixel of the canonical purple ramp (legacy palette indices 98–103: `#B393C7 #9B7BB7 #8F5BA3 #7F3B93 #47235F #37005B`, `ItemDyeRamp`). This is the legacy rule: the sprite uses the dye slots. The client detects it on the first load of the id and keeps the answer. **Ramp pixels must be fully opaque**, as palette art is: the PNG decode rounds the color of a semi-transparent pixel, so it is not found and not dyed (a soft purple edge stays purple). Taliesin writes `no_dye` for the exceptions and never writes `dyeable`.

For a dyeable item with a non-zero server color byte, the client replaces the ramp pixels with the six colors from the active `color0.tbl` entry, preserving alpha. Other items ignore the color byte. Both item caches key on the color byte that the server sent, so a cache hit asks no pack. At startup, when an item pack is installed, the client compares the ramp with `color0.tbl` entry 0 and logs any difference.

`Scripts/item-dye-compare/` checks the rule and the dye pass against legacy on real art, in both directions. The result for the Unity rip is on BRIG-41: the pack dye matches the legacy dye for every comparable dyeable icon except 15 whose palette has no dye ramp (so the legacy client does not dye them, and a pack lists them in `no_dye`: 5595, 12769–12772 and 12774–12783), and 2 icons that the legacy client dyes but whose rip art has no ramp pixel (14099 and 14104; the art must be repainted with the ramp).

---

## `nation_badges`

`Brigid.Data/AssetPacks/NationBadgePack.cs` — nation flag shown in the profile equipment tab.

```csharp
return TryGetImage($"nation{nationId:D4}.png", out image);     // NationBadgePack.cs:30
```

- Regex: `^nation\d{4}\.png$`. `byte nationId`, **1-based**. Guard: `nationId == 0` → miss.
- Overrides `UiRenderer.GetNationBadge` → frame `nationId - 1` of `_nui_nat.spf`.
- Manifest fields read: **none**. No dimension constraint — `UIImage` scales to placed bounds, so
  higher-resolution art displays fine (point-filtered, preserving the pixel-art look).

---

## `npc_portraits`

`Brigid.Data/AssetPacks/NpcPortraitPack.cs` — NPC dialog illustrations. **The one manifest-driven
naming scheme**, and the only type with hard image-size validation.

Entry names are not derived from an ID — they come from `covers.npc_portraits.portraits`, a flat
`portrait key → PNG filename` map:

```json
"covers": {
  "npc_portraits": {
    "dimensions": [200, 200],
    "portraits": { "inn.spf": "innkeeper.png", "Gobalt": "gobalt.png" }
  }
}
```

- The **key** is the literal `Portrait` attribute value as the server publishes it via the NPCIllust
  metafile — verbatim, no extension stripping or normalization. Both `"inn.spf"` and `"Gobalt"` are
  valid keys because both forms appear in real Hybrasyl XML.
- The map is built with `StringComparer.OrdinalIgnoreCase`; keys and values are `.Trim()`-ed, and
  empty pairs are dropped. Absent or empty `portraits` = the pack covers nothing.
- The **value** is a PNG entry name at the ZIP root.
- Overrides `WorldScreen.ServerHandlers.TryLoadNpcIllustration` → `DatArchives.Npcbase` SPF frame 0.

**Dimension enforcement** (both `dimensions` and per-entry size):

- Construction, fail-closed: `dims is null || dims.Length != 2 || dims[0] != dims[1] || dims[0] <= 0`
  → logs `npc_portraits pack '{id}' has missing or non-square dimensions; pack will serve no
  illustrations`, sets `Dimensions = (0,0)`. The pack registers but never serves.
- Decode time: any entry whose actual size differs from the declared size logs
  `npc_portraits: '{entry}' is {W}x{H}; expected {W}x{H}; ignoring`, disposes, and reports a miss.

Portraits must be **square**.

---

## `static_tiles`

`Brigid.Data/AssetPacks/StaticTilePack.cs` — in-viewport iso floor and wall tiles.

```csharp
public bool TryGetFloorImage(int tileId, out SKImage? image) => TryGetImage($"floor{tileId:D5}.png", out image);
public bool TryGetWallImage(int tileId, out SKImage? image)  => TryGetImage($"wall{tileId:D5}.png", out image);
```

- Regex: `^(floor|wall)\d{5}\.png$`. Two independent ID namespaces at the ZIP root.
- IDs are the raw `MapTile.Background` (floor) and `LeftForeground`/`RightForeground` (wall) values
  **with no offset applied**. No `tileId <= 0` guard.
- Overrides `DataContext.Tiles.GetBackgroundTile` (floors) and `stc{tileId:D5}.hpf` (walls). A miss
  on both paths yields the checkerboard placeholder.

**Eligibility quirk — palette-cycled tiles are skipped.** `Brigid.Rendering/MapRenderer.cs` refuses
the pack lookup for any legacy tile that participates in palette cycling (water, lava, shimmer),
because the cycling animation is driven by palette rotation a static PNG cannot express. Applied
independently to floors (line 651) and walls (line 665):

```csharp
if (bgTileData.ContainsKey(tileId)
    && bgLookup.Table.GetCyclingEntries(bgLookup.Table.GetPaletteNumber(tileId + 1)) is not null)
    continue;
```

Note the `tileId + 1` — that offset applies to the **palette-table probe only**, not to the entry
name. Pack-only IDs (no legacy tile at that index) bypass the cycling check entirely and are always
added. Pack foreground heights fold into `maxFgHeight`, driving `ForegroundExtraMargin`.

Manifest fields read: **none**. Art guidance: `docs/static-tiles-authoring-guide.md`.

---

## `legend_mark_icons`

`Brigid.Data/AssetPacks/LegendMarkIconPack.cs` — small icons beside legend entries in the self profile.

```csharp
=> TryGetImage($"legend{iconId:D4}.png", out image);           // LegendMarkIconPack.cs:26
```

- Regex: `^legend\d{4}\.png$`. `byte iconId`, **0-based** — a deliberate deviation from the 1-based
  convention. The server sends the icon as a raw byte used directly as a frame index, so
  `legend0000.png` is valid and replaces frame 0. No guard.
- Overrides `UiRenderer.GetLegendMarkIcon` → `GetEpfTexture("legends.epf", iconId)` (palette 3 of
  `national.dat`).
- Manifest fields read: **none**. Legacy icons are roughly 21×20; a modern PNG draws at its own
  pixel size into the row cell, so oversized art overflows until pack-controlled panel layouts exist.

---

## `ui_sprite_overrides`

`Brigid.Data/AssetPacks/UiSpriteOverridePack.cs` — generic per-frame override for any EPF or SPF the
client loads from `setoa.dat` (and `cious.dat` on the prefab path). The catch-all for UI reskinning:
one pack can cover the entire interface.

```csharp
return TryGetImage($"{fileName}/{frameIndex:D4}.png", out image);   // UiSpriteOverridePack.cs:34
```

- Regex: `^[^/]+\.(epf|spf)/\d{4}\.png$` by convention — the folder name is whatever the caller
  passes, so the extension is not validated in code.
- The folder name is the **full legacy filename including extension**, lowercased: `butt001.epf/`,
  `dlgback2.spf/`, `_nui_tb1.spf/`. The extension matters because EPF and SPF can share a stem.
- The PNG name is the zero-padded frame index: `0000.png`, `0015.png`.
- Frames need not be contiguous — ship `0015.png` and `0016.png` alone and frames 0–14 stay legacy.
  A frame index beyond the legacy file's frame count is silently ignored.
- Guards: empty `fileName` or `frameIndex < 0` → miss.
- Manifest fields read: **none**.

**Three consuming call sites**, all in `Brigid.Data/Repositories/UiComponentRepository.cs`:

| ~Line | Method                             | Behavior                                                                   |
| ----- | ---------------------------------- | -------------------------------------------------------------------------- |
| 105   | `GetEpfImages(fileName)`           | Legacy frames render first, then each index is probed and substituted       |
| 296   | `GetSpfImage(fileName, frameIndex)` | Pack probed first — the legacy SPF view is never loaded on a hit           |
| 379   | `RenderFrame(imageName, frameIndex)` (private, prefab) | Probes `{stem}.epf/` then `{stem}.spf/` before the archive |

The key is identical on all three paths; the breakdown matters only when debugging a missed override.

**Typed packs win.** If both `legend_mark_icons` and `ui_sprite_overrides` cover `legends.epf` frame
0, the typed pack's `legend0000.png` is used — typed lookups happen at a higher renderer layer than
this repository-level substitution.

**Finding prefab-resolved stems.** Most HUD art is resolved through prefab `.txt` control files, so
the legacy filename isn't guessable. Set `CHAOS_PREFAB_DUMP=1` or run
`Scripts/dump-prefab-images.{sh,ps1}`, then grep `output/prefab-dump.log`:

```text
[prefab-dump] _nbk_l/InventoryBackgroundExpanded -> _ninv5.spf[0]
```

→ the override key is `_ninv5.spf/0000.png`. `controlFileList.txt` has the full asset catalog.

---

## `creature_sprites`

`Brigid.Data/AssetPacks/CreaturePack.cs` — creature/NPC sprites. **Phase 1 is deliberately narrow.**

Unlike every other image pack, entries are **pre-scanned at construction** rather than probed by
format string:

```text
creature_sprites/creature_{spriteId:D5}/stand/n_001.png
creature_sprites/creature_{spriteId:D5}/stand/e_001.png
```

Parser `TryParsePhase1Entry`:

```csharp
const string PREFIX = "creature_sprites/creature_";
const string STAND_INFIX = "/stand/";
```

- Prefix match is `OrdinalIgnoreCase`. The ID segment is anything `int.TryParse`-able and `> 0`, so
  **zero-padding is tolerated, not required**.
- The filename must be exactly `n_001.png` or `e_001.png` (case-insensitive). Every other stance
  directory the layout reserves — `walk`, `attack`, `attack2`, `attack3`, `idle`, `cast`, `hurt`,
  `death` — and every other frame index is parsed as nothing and **ignored at runtime in phase 1**.
- `n` = master facing North, `e` = master facing East. The engine mirrors: `flip = direction is Down
  or Left`, pairing N↔W and E↔S.
- `PairCount` is 1 or 2. `GetEntryName(0)` returns `NEntry ?? EEntry`; `GetEntryName(1)` returns
  `EEntry` only when `NEntry` is non-null.
- API: `Covers(id)`, `GetPairCount(id)`, `TryGetFrame(id, pairIndex, out SKImage?)`.
- Overrides `DataContext.CreatureSprites.GetCreatureSprite` (legacy MPF).
- Manifest fields read: **none**.

**Offsets are computed, never declared.** `CreatureRenderer.LoadPackFrame` derives the anchor from
`ComputePackUnionBbox(pack, spriteId, pairCount)`, cached in `PackCreatureUnionBboxCache`.
`GetAnimInfo` synthesizes static animation info via `SynthesizeStaticAnimInfo(pairCount)`, and
`GetAverageTopOffset` for pack creatures is simply the decoded image height (bottom-center anchor).

**Unique fallback shape:** a creature the pack *claims* but whose frame fails to decode renders the
checkerboard placeholder, **not** the legacy MPF. Claiming a creature is a commitment.

---

## `music`

`Brigid.Data/AssetPacks/MusicPack.cs`, base `AudioPack.cs` — background music, **streamed**.

```text
music_{id}.{ext}
```

- Regex: `^music_\d+\.(ogg|mp3|wav|flac|mus)$`. Root only — entries containing `/` are ignored.
- `{id}` is the integer music ID the server references (`SoundArgs.Sound` when `IsMusic` is true).
  Parsed with `NumberStyles.None` + invariant culture: **plain digits only**. Zero-padding is
  tolerated (`music_5.ogg` and `music_00005.ogg` both resolve to 5), `0` is a legal ID, a leading
  `+`/`-` or surrounding whitespace makes the entry unrecognized. Last writer wins if two extensions
  share an ID.
- **Extension is enforced**: `AllowedExtensions = { ogg, mp3, wav, flac, mus }` (case-insensitive);
  anything else is silently not indexed. Each bundled SDL2_mixer build decodes `mp3`, `ogg`, `wav` and `flac` (Windows and macOS: built-in minimp3, stb_vorbis and dr_flac; Linux: `ogg` and `flac` through the system libvorbisfile and libFLAC that the package declares). An entry that does not decode falls back to legacy.
- API `TryGetMusicBytes(int musicId, out byte[]?)` — **raw bytes, no decode in the data layer.**
- Overrides `SoundSystem.StartMusic` (`Brigid/Systems/SoundSystem.cs:433`) → legacy loose file
  `{DataPath}/music/{id}.mus`. The pack path pins the `byte[]` (`GCHandle.Alloc(..., Pinned)`) and
  uses `SDL_RWFromConstMem` + `Mix_LoadMUS_RW(rw, 1)`; on failure the pin is freed and it falls
  through to legacy. `musicId == 0` returns early.
- Manifest fields read: **none**. `covers.music` may carry informational per-track metadata
  (`title`, `codec`, `loop`) that the client ignores entirely.

---

## `sound_effects`

`Brigid.Data/AssetPacks/SfxPack.cs`, base `AudioPack.cs` — short SFX, **fully decoded and cached**.

```text
sfx_{id}.{ext}
```

- Regex: `^sfx_\d+\.(wav|ogg|mp3|flac)$`. Same root-only + plain-digit ID parsing as `music`.
- **Extension list differs from `music`: no `mus`.** `AllowedExtensions = { wav, ogg, mp3, flac }`.
  All four formats decode on every platform (see `music`).
- API `TryGetSfxBytes(int soundId, out byte[]?)`.
- Overrides `SoundSystem.LoadChunk` (`Brigid/Systems/SoundSystem.cs:371`) → legacy `legend.dat`
  entry `{soundId}.mp3`. A present-but-undecodable entry falls through to legacy.
- Manifest fields read: **none**.

---

## `ambient_sounds`

`Brigid.Data/AssetPacks/AmbientPack.cs`, base `AudioPack.cs` — ambient beds (for example rain), **fully decoded and cached** like SFX. A bed plays on one mixer channel beside the single music stream, at the **Ambience** volume.

```text
amb_{id}.{ext}
```

- Regex: `^amb_\d+\.(wav|ogg|mp3|flac)$`. Same root-only + plain-digit ID parsing as `music`. Taliesin writes `amb_{id:D4}`, for example `amb_0001.wav`.
- `{id}` is an **ambient id**, a separate number space from the SFX id. `0` means no ambient, so ids are 1–255 (a byte, to agree with the planned per-map server field).
- API `TryGetAmbientBytes(int ambientId, out byte[]?)` and `Loops(int ambientId)`.
- Consumed by `SoundSystem.StartAmbient`. **No legacy fallback:** an id with no entry is a silent no-op, and an entry that is empty or does not decode is silent too (the client writes one line to `notice-debug.log`). An entry outside 1–255 is ignored.
- **Size:** the client decodes a whole entry to PCM in memory, with no size limit, as for `sound_effects`. At the mixer format (22050 Hz, 16-bit, stereo) one second is about 86 KB, so a 10 s bed is under 1 MB and a 5 min bed is about 26 MB. Make a bed a short loop, not a long recording.
- Manifest field read: `covers.ambient_sounds`, keyed by ambient id. Only `{ "loop": false }` changes the default.

| `covers.ambient_sounds["<id>"]`          | Client behaviour                  |
| ---------------------------------------- | --------------------------------- |
| absent                                   | loop                              |
| `{ "loop": true }`                       | loop                              |
| `{ "loop": false }`                      | play one time, then free the channel |
| any other value (for example `"no"`)     | loop                              |

A one-shot bed does not play again while the same id stays the bed, also across a map change. A different id, or a stop, resets it. The interval fields (`mode`, `play`, `silence`) are legal to write, and the client ignores them.

**Ids that the client uses.**

| ID | Use  | Notes |
| -- | ---- | ----- |
| 1  | Rain | Plays while the player is on a rain-flagged map (low nibble of the map flags is 2). Make it loop end-to-start and keep it **short**, because it decodes fully to PCM on the game thread. Use **`wav`**: MP3 encoder padding puts a silent gap into the loop seam. |

### Audio notes (all audio types)

- The mixer runs at **22050 Hz / stereo / signed 16-bit**. Other rates work (the client resamples),
  but matching saves a resample.
- Keep SFX and ambient beds short — they decode into a small (64-entry) LRU cache. A long track belongs in `music`, which streams.
- Malformed entries (corrupt file, non-integer/negative ID, wrong extension, file in a subfolder) are "not present". `music` and `sound_effects` then fall back to legacy; `ambient_sounds` has no legacy source, so the bed is silent. A bad file never breaks playback.

---

## `world_maps`

`Brigid.Data/AssetPacks/WorldMapPack.cs` — full-screen overworld field backgrounds behind the
clickable destination nodes shown when the server sends opcode `0x2E` (`WorldMapPacket`).

```csharp
public bool TryGetFieldImage(string fieldName, out SKImage? image) => TryGetImage($"{fieldName}.png", out image);
```

- Regex: `^.+\.png$`. **Named, not numeric** — `{fieldName}` is the server-sent `ClientMap` string
  verbatim (e.g. `field001`), so there is no zero-padding rule. Case-insensitive, root only.
- Overrides `UiRenderer.GetFieldImage` (`Brigid.Rendering/UiRenderer.cs:201`) → legacy
  `{field}.epf` + co-located `{field}.pal` from **`setoa.dat`** (frame 0 only, via
  `UiComponentRepository.GetFieldImage`). Note the palette is the field's own `.pal`, not the GUI
  palette. A miss on both paths yields `MissingTexture`.
- Shares the legacy path's `Convert` + `Cache["field:{name}"]` flow, so the cached texture's
  ownership contract is identical (`Convert` returns a `CachedTexture2D` whose `Dispose` is a no-op,
  which is why `WorldMap.ClearBackground` disposing it is harmless).
- Manifest fields read: **none**.

**Sizing.** `WorldMap` (`Brigid/Controls/World/ViewPort/WorldMap.cs`) is a hard-coded 640×480 panel
at (0,0), and the background is blitted into a literal `Rectangle(0, 0, 640, 480)` — the source
texture's real dimensions are ignored, so a pack PNG of any size is force-scaled (non-uniformly if
the aspect differs). Node coordinates are the raw server `u16` X/Y interpreted directly in that same
640×480 space with **no scale factor**, so higher-resolution art does not move the nodes. Author the
PNG at 640×480, or at an exact 4:3 multiple reproducing the legacy field's composition, until an
explicit node-coordinate scale exists.

Nodes are drawn by the client from server data and must **never** be baked into the PNG.
`WorldMapPacket.ImageIndex` is parsed by DALib but currently unread by Brigid.

---

## `town_maps`

`Brigid.Data/AssetPacks/TownMapPack.cs` — the **T-key** town map: a popup thumbnail of the *current*
map keyed by numeric map ID. Distinct from `world_maps` (the server-driven overworld field).

```csharp
return TryGetImage($"town_{mapId:D5}.png", out image);         // TownMapPack.cs:38
```

- Regex: `^town_\d{5}\.png$`. `short mapId` = the client's `CurrentMapId`, 5-digit zero-padded
  (map 500 → `town_00500.png`). Guard: `mapId <= 0` → miss.
- Overrides `UiRenderer.GetTownMapImage` (`Brigid.Rendering/UiRenderer.cs:240`), replacing the whole
  legacy `national.dat` five-layer composite (`_t_back.spf` frame + `_t_icon.spf` bar +
  `_t{mapId}.spf` art + `_t{mapId}n.spf` name + `tmuser.epf` marker, positioned via `_tcoord.txt`).
- Manifest fields read: **none**.

**Unique fallback shape.** `GetTownMapImage` returns `Texture2D?` and yields **null** on absence —
not `MissingTexture` — precisely so `TownMapControl` can fall through and assemble the legacy
composite itself.

**Shipped scope (v1): full-panel image, no player marker.** The PNG is blitted as-is; the runtime
does not add the `_t_back` frame, a name label, or the animated player-position marker. Frame
chrome + map art + title must all be baked into the one image.

**Author at exactly 568×406** (the town-map panel size). The runtime draws centered at native size —
a correctly-sized image fills the panel exactly; an off-size image degrades gracefully (centered,
clipped if larger, gapped if smaller) rather than stretching. Taller POI-panel variants (568×515)
are **not** supported in v1 and would clip.

---

## `ui_theme`

`Brigid.Data/AssetPacks/UiThemePack.cs` and `UiTheme.cs` — art and colours for the asset-free
primitives (`TextButton`, `SelectableTab`, `CenteredModalPanel`). It does not replace legacy art: a
primitive with no theme part keeps its flat `DialogPalette` look. A control uses the theme only when
`UIElement.UseTheme` is true on it or on the nearest parent that sets it.

**One theme is active.** Several themes can be installed (`UiThemePacks`, in priority order, then by file name), but themes are alternatives, not layers. `GetUiThemePack()` returns the theme whose `pack_id` the player selected (`UiTheme:` in `brigid.cfg`, through `SelectUiTheme` at startup; case ignored), else the first theme ("Automatic"). `UiTheme: none` (`NO_UI_THEME`) makes no theme active, so the flat look shows with themes installed. A selected id that is not installed logs a warning and uses the first theme. A part, colour or font that the active theme does not have takes the flat look, never another theme's. `CoverageKeys` is empty, so two themes give no shared-key warning. The player chooses in Options → Settings → Theme (`ThemePicker`), or in the launcher's Theme row: Automatic, each installed theme by its manifest `name` (else its `pack_id`), and None. A choice in Options, or in the launcher opened from the main menu, applies the next time Brigid starts; a live swap is BRIG-133. A choice in the launcher at startup applies on Connect, because the packs load after it. Before the registry initializes, the launcher lists the themes with `ScanUiThemes`, which reads each manifest with the checks of registration and opens no pack for use.

`theme.json` at the ZIP root names each part, colour and font by **role**, not by panel or control,
so every themed button gets the same `button.normal` art. Roles ignore case.

```json
{
  "density": 4,
  "parts": {
    "frame.panel":   { "file": "frame_panel.png", "slice": [96, 96, 96, 96] },
    "button.normal": { "file": "button_normal.png", "slice": [48, 40, 48, 40], "tile": false },
    "background.mainmenu": { "file": "mainmenu_bg.webp", "fit": "cover" }
  },
  "colors": { "text": "#E8E4D8", "text.disabled": "#7A7A80" },
  "fonts": { "title": "title.ttf" }
}
```

- `density` — art pixels for each logical (640×480) pixel, from `0.25` to `16`. Default `1`.
- `filter` — `linear` (default) or `nearest`: how a part is sampled where it is enlarged. Use
  `nearest` for pixel art at 1× density. A part can set its own `filter`.
- `parts.*.file` — the image entry (PNG or WebP). `slice` — nine-slice insets in art pixels,
  `[left, top, right, bottom]`, each from 0 to 16384; the corners keep their shape and the edges
  and centre stretch, or repeat when `tile` is `true`. `fit` — `stretch` (default), `cover` or
  `contain`, for a part with no `slice`.
- **Layout uses at most 32 logical px of a slice** (`ThemeLayout.MAX_SLICE_BORDER`). A frame keeps its content clear of the widest inset, divided by `density`, after the insets are clamped to the image as the draw clamps them. A wider slice still draws, but content can then sit on the outer part of the art. The test themes use 6 to 11.
- A part whose image entry is absent or does not decode is not usable: it draws the flat look, gives no frame border, and a control that asks whether the part exists (`HasPart`) gets false.
- `colors.*` — exactly `#RRGGBB` or `#AARRGGBB` (alpha first, unlike CSS `#RRGGBBAA`).
- `fonts.*` — a TTF or OTF entry.
- A part, colour or font that is not valid is skipped and logged through `DataDiagnostics.Log`;
  the rest of the theme still loads. An unknown `fit` or `filter` keeps the part with the default.
  Unknown keys are ignored. A pack with no `theme.json` registers with an empty theme.

Roles read by the primitives (`Brigid.Rendering/ThemeRoles.cs`): parts `frame.panel`, `button.normal`, `button.hover`, `button.pressed`, `button.disabled`, `tab.normal`, `tab.selected`, `dropdown.list`; colours `text`, `text.disabled`, `text.selected`, `text.title`, `text.error`, `divider`. The `DropDown` primitive draws its box with the button parts and its open list with `dropdown.list` (a list of 18 px rows, so a thin border; with no part it has the flat look). The character creator (`CharacterCreatorControl`) draws an error in its status line in `text.error`. The main menu (`MainMenuControl`) also reads parts `background.mainmenu` (normally `"fit": "cover"`; it fills the whole window, cropped to the window's aspect ratio) and `title.mainmenu` (art in place of the title text), and the font `title` (the display face for the title text, in place of Uncial Antiqua). A `title` font with no TrueType or OpenType signature, or one the parser refuses, is logged and the shipped face stays. The Story view (`StoryView`) reads each slide's `image` from `story.json` as a part name (`story.01` and so on; cover fit), and `background.story` behind its text and cutscene pages (else `background.mainmenu`). A slide with no part shows a placeholder frame with the part name. A themed vertical scroll bar (`ScrollBarControl`) reads `scrollbar.track` (normally sliced), `scrollbar.thumb` (drawn 2 px in from the track's long edges), `scrollbar.thumb.hover`, `scrollbar.thumb.pressed`, `scrollbar.up`, `scrollbar.up.pressed`, `scrollbar.down` and `scrollbar.down.pressed`. A themed horizontal bar (BRIG-126) reads its own parts, because the vertical art is upright: `scrollbar.htrack` (normally sliced), `scrollbar.hthumb` (drawn 2 px in from the track's top and bottom), `scrollbar.hthumb.hover`, `scrollbar.hthumb.pressed`, `scrollbar.left`, `scrollbar.left.pressed`, `scrollbar.right` and `scrollbar.right.pressed`. A piece the theme does not have keeps its legacy look. A button, tab, thumb or arrow state that is missing or does not decode uses the normal state.

World UI roles (BRIG-120). Parts: `input.slot` (the box behind a text field in an `InputSlotPane`; normally sliced; with no part the flat box, its border in `divider`), `input.slot.focus` (the box behind the focused field; else `input.slot`), `checkbox.normal` and `checkbox.hover` (a `CheckBox` box, in place of the flat fill and border; a missing or undecodable state uses normal, and with neither the flat box shows) and `checkbox.mark` (drawn over the whole box when checked, so the art holds its own margin; with no part a flat square in `text.selected`). Colours: `border` (the outline of a flat control: `TextButton`, `DropDown`, `FramePanel` and `CheckBox`; a disabled button or drop-down box uses `divider`), `row.hover` and `row.selected` (the fills of list rows, for example in a `DropDown` list), `input.text` (the text of a text box that sets that role) and `input.focus` (the fill of a focused text box; with no `input.focus`, a box on `input.slot` art draws no fill, so the art shows). `text.selected` is the selection accent: selected text, the flat check mark, the selected swatch ring, the border of a flat selected tab (an idle flat tab uses `border`) and the flat fill of a slider. `text.disabled` also colours less prominent text, for example the idle counts of the Aisling list. The Aisling list and the bank/shop browser draw `frame.panel` and the `tab.*` parts. Popup role (BRIG-125): `frame.popup`, read by `PopupMenu` (the world's context menus), `TooltipFrame` (the item tooltip and the bank/shop tooltips) and the group box over a recruiting aisling (BRIG-126; the group title in `text` in a 97 by 13 area that the frame goes around, which `row.hover` fills while the mouse is on the box): a small floating frame with 14 px rows, so give it a thin border (about 3 logical px); with no part, the flat frame (not `frame.panel`). Status icon roles (BRIG-122): `icon.status.awake` to `icon.status.need_help`, one square image each, drawn on every host (also an unthemed one) in the status book, the other-profile view, the Aisling list and over the HUD emote button; with no part, Brigid's built-in icon, else the legacy frame. Slider roles (BRIG-55), read by `SliderControl` in the pause menu: `slider.track` (normally sliced; drawn across the control's height, inset half a thumb at each end), `slider.fill` (normally sliced; from the start of the track to the centre of the thumb) and `slider.thumb` (one image, 8 by 14 logical px in the pause menu). A piece with no part keeps the flat look: a dark groove outlined in `border`, a fill in `text.selected`, and a flat thumb. Sign role (BRIG-122): `frame.sign`, read by `TextPopupControl` for the server's WoodenBoard message, drawn over the whole popup (360 by 180 logical px, no title band, no Close); with no part, the legacy `woodbk.epf` art. The text sits 16 px in from the left and right, 12 px from the top and 32 px from the bottom, so give the art its margins. `divider` also outlines the flat input slot. A `UILabel` or `UITextBox` takes a colour role through `SetForegroundRole(role, fallback)`; its `ForegroundColor` stays the fallback. A colour that carries meaning stays literal. A themed `MarkdownView` draws `frame.panel` in place of its legacy frame and lays out inside that frame's border; its close and maximize buttons are `TextButton`s, so they take the button parts.

**Drawn at native resolution.** `ThemeArt` composites a part with SkiaSharp at the native pixel
size of its rectangle and draws it with the inverse of the UI pass scale, as `FontEngine` draws
glyphs, so 4× art stays sharp at every window size. A large reduction is filtered by halving the
image first, and each nine-slice piece is drawn from its own sub-image so no seam shows. Textures
are cached per role, native size and pass scale; above 256 entries the least recently used go, and
an evicted texture is disposed a second later, never in the frame that may still draw it.

Manifest fields read: **none** beyond the common ones.

---

## Cross-cutting behavior

- **Entry index.** `AssetPack`'s constructor indexes every `entry.FullName` into a
  `Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase)`. Every lookup in every pack
  is therefore case-insensitive, and forward slash is the path separator regardless of platform.
- **Decode contract.** `TryGetImage` = `TryGetEntryBytes` + `SKImage.FromEncodedData`. Read failures
  and corrupt/non-image data are both reported as "not present". Nothing verifies PNG magic — any
  Skia-decodable format would in fact work despite the `.png` naming convention. Don't rely on that.
- **Disposal.** The caller owns the returned `SKImage`. `AssetPack.Dispose()` disposes the
  `ZipArchive`. The registry keeps every registered pack for the life of the process.
- **Zip root.** Some Windows ZIP tools wrap contents in a folder named after the archive. If
  `_manifest.json` isn't at the top level immediately, the pack is skipped. Rebuild with contents at
  root.

---

## Troubleshooting

| Symptom                                            | Cause                                                                                              |
| -------------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| Pack not loading at all                            | Check `notice-debug.log` for `[asset-pack]` at startup. Missing/malformed `_manifest.json`, `schema_version` absent or not a supported version, `pack_id` absent or blank, unknown `content_type`, or the pack is in the game-data folder instead of `AppPaths.AssetsDir` |
| Some assets legacy despite being in the pack       | Filename doesn't match the exact pattern (case-insensitive but padding matters), corrupt entry, or the file isn't at the ZIP root |
| Two packs cover the same key                       | The pack with the higher `priority` wins that key; at the same priority, the pack whose file name sorts first. Startup logs a warning that names both packs, the shared keys and the pack that a lookup asks first |
| Icon appears 1px off                               | Expected — a pack icon is centred in the legacy 31×31 slot by its size, so a 32×32 icon draws at `-1,-1` (an ability icon always does). Check the PNG size |
| `ui_sprite_overrides` frame ignored                | Folder name must include the legacy extension (`butt001.epf/`, not `butt001/`); PNG name must be 4-digit padded (`0000.png`, not `0.png`); frames ≥ the legacy frame count are silently dropped |
| `npc_portraits` serves nothing                     | `dimensions` missing or non-square → the pack registers but is inert. Check stderr                 |
| A water/lava tile stays legacy                     | Palette-cycled tiles are deliberately excluded from `static_tiles` lookup                          |
| New pack not picked up mid-session                 | No hot reload — restart the client                                                                 |

---

## Future content types

The format is extensible; a new type is one `Factories` entry plus a pack class.

| `content_type` | Covers                                                     | Expected manifest additions                            |
| -------------- | ----------------------------------------------------------- | ------------------------------------------------------ |
| `tiles`        | Animated/palette-cycled map tiles `static_tiles` can't express | Per-tile frame array + timings                      |
| `ui_panels`    | Pack-controlled panel layouts (replaces prefab `.txt`); per-control art above the `ui_theme` roles | `panel_ids` coverage list; schema bump to `2` |
| `effects`      | Spell/combat effects (replaces EFA)                          | Frame timings, blend mode (additive), anchor           |
| `bundle`       | Multi-type pack (one archive, several categories)            | `covers` enumerates multiple categories                |

**Known `ui_panels` blocker.** The design keys overrides on `{panel_id}.{control_name}`, where
`control_name` is `UIElement.Name`. Roughly half the from-scratch (non-prefab) controls never set
`Name`, so they are not addressable under that scheme. Resolving this — either by requiring `Name`
on every addressable control or by keying on something else — is a prerequisite, not a detail.
`Brigid.Tests/UiPrimitivesReportTests.cs` and `Scripts/dump-ui-primitives.{sh,ps1}` inventory the
current state.

---

## Further reading

Document repo, `plans/brigid/`:

- `asset-pack-format.md` — artist-facing container spec and authoring workflow
- `world-map-asset-pack-scoping.md`, `town-map-asset-pack-scoping.md`,
  `item-asset-pack-scoping.md`, `npc-portrait-asset-pack-scoping.md`,
  `creature-asset-pack-scoping.md`, `display-sprite-asset-pack-scoping.md`,
  `effect-asset-pack-scoping.md`, `projectile-asset-pack-scoping.md`,
  `aisling-body-asset-pack-scoping.md`, `tile-collision-asset-pack.md`, `ui-asset-pack-scoping.md`
- `plans/taliesin/audio-pack-authoring.md` — audio pack workflow

In this repo:

- `controlFileList.txt` — legacy control/EPF/SPF catalog (what `ui_sprite_overrides` overrides)
- `docs/static-tiles-authoring-guide.md` — static tile art rules
- `Scripts/sample-packs/items-test-001-002/` — old hand-editable manifest scaffold (an explicit `dyeable` list)
- `Scripts/dump-prefab-images.{sh,ps1}` — resolve prefab control names to EPF/SPF stems
- `Scripts/item-dye-compare/` — compares the pack dye with the legacy dye on real item art (BRIG-41)

Packs are authored with **Taliesin** (`../taliesin/`), the Hybrasyl asset-management tool — the
supported path for producing `.datf` archives.
