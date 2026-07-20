# `.datf` Asset Packs — Content Type Catalog

Code-derived reference for every `.datf` asset-pack `content_type` Brigid understands: entry naming,
ID conventions, manifest fields actually consumed, the legacy asset each type overrides, and the
constraints enforced in code.

Counterpart to `controlFileList.txt` (which catalogs the *legacy* prefab/EPF/SPF world). Same
altitude: reference, not tutorial. Artist-facing narrative — authoring workflow, dye rationale,
roadmap — lives in the document repo's `plans/hybrasyl.client/asset-pack-format.md` and its
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

1. Renderer probes the registered pack for this content type.
2. Hit → modern asset used.
3. Miss, or decode failure → falls through to the legacy asset for that ID alone.

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
| Windows     | `%LOCALAPPDATA%\erisco\Brigid\assets` |
| macOS/Linux | `~/.config/erisco/Brigid/assets`      |

The Unix split is deliberate: .NET maps `LocalApplicationData` to `~/.local/share`, but the house
standard targets `~/.config`, which is `SpecialFolder.ApplicationData` there. If `GetFolderPath`
returns `""` (stripped env, `HOME` unset), the base falls back to `AppContext.BaseDirectory` so
packs never scatter into the CWD.

Scan is `Directory.EnumerateFiles(packDir, "*.datf", SearchOption.TopDirectoryOnly)` — extension
`.datf` only, top level only, no recursion.

### Validation ladder

Per file, in order; any failure skips **that pack only** and logs to stderr with the `[asset-pack]`
prefix. Startup is never aborted by a bad pack.

| Check                                     | Message on failure                                                       |
| ----------------------------------------- | ------------------------------------------------------------------------ |
| `ZipFile.OpenRead`                        | `failed to open pack {name}: {ExceptionType}: {message}`                 |
| `_manifest.json` entry exists at ZIP root | `pack {name} is missing _manifest.json; skipping`                        |
| JSON deserializes non-null                | `pack {name} has empty or invalid manifest; skipping`                    |
| `schema_version <= 1`                     | `pack {name} declares schema_version=N which is newer than supported (1)` |
| `content_type` matches a factory key      | `pack {name} has unknown content_type='...'; skipping`                   |

Schema validation is **max-only** (`if (manifest.SchemaVersion > SUPPORTED_SCHEMA_VERSION)`), so a
pack declaring `schema_version: 0` — or omitting the field entirely — is accepted. `SUPPORTED_SCHEMA_VERSION`
is currently `1`.

No checksum, no signature, no entry-name validation, no image-format validation at load. All decode
is lazy, at first lookup.

### Conflict resolution

One registered pack per `content_type`. **Strictly greater `priority` wins; ties go to
first-registered** (`AssetPackRegistry.cs:316`):

```csharp
if (Packs.TryGetValue(manifest.ContentType, out var existing) && (manifest.Priority <= existing.Manifest.Priority))
{ /* log, dispose the loser's archive, keep existing */ }
existing?.Dispose();
Packs[manifest.ContentType] = factory(archive, manifest);
```

A superseded pack is disposed by the registry. Multi-pack-per-type (per-ID dispatch across several
archives) is not implemented.

### Legacy migration

One-time, best-effort copy of `{DataContext.DataPath}/hybrasyl-data/*.datf` into `AssetsDir`,
smoothing pre-`AppPaths` manual installs. Gated by a `.legacy-migrated` marker file written into
`AssetsDir` whether or not anything was found — the gate is "has it ever run", not "is the dir
empty", so clearing the assets dir to force a launcher re-fetch never resurrects stale packs. Copies
to `{dst}.tmp` then `File.Move` so an interrupted copy can't leave a truncated archive. Per-file
`File.Exists(dst)` skip means launcher-placed content is never clobbered. Every failure path is
swallowed.

### No hot reload

`Initialize()` latches on a static `Initialized` bool. A pack dropped in mid-session is not
discovered, and even after re-registration an already-cached texture keeps serving until the owning
renderer's cache is cleared. Restart the client.

### Adding a content type

Three edits: one line in `AssetPackRegistry.Factories`, one typed accessor, and the pack class
deriving from `AssetPack` (or `AudioPack` for byte-serving types).

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
| `schema_version` | int                                      | `0`     | Yes — max-only gate                                                |
| `pack_id`        | string                                   | `""`    | Logging and conflict messages only                                 |
| `pack_version`   | string                                   | `""`    | **Nothing reads this.** Informational                              |
| `content_type`   | string                                   | `""`    | Yes — factory discriminator                                        |
| `priority`       | int                                      | `100`   | Yes — conflict resolution                                          |
| `covers`         | `Dictionary<string, CoverageEntry>`      | `{}`    | Only three sub-fields, below                                       |

`covers` is a **capability** declaration, not a coverage range — actual coverage is emergent from
which entries the archive ships. You never enumerate sprite IDs in the manifest.

### `covers.<category>` sub-fields

| Field        | Type                         | Consumed by                                          |
| ------------ | ---------------------------- | ---------------------------------------------------- |
| `dimensions` | `int[]` `[w, h]`             | **`NpcPortraitPack` only.** See note below           |
| `dyeable`    | `int[]` (1-based item IDs)   | `ItemPack` only — `covers.item_icons.dyeable`        |
| `portraits`  | `Dictionary<string, string>` | `NpcPortraitPack` only — **required**, it *is* the naming convention |

**`dimensions` caveat.** Widely documented as `[32, 32]` for `ability_icons`; no code reads it there.
The 31→32 icon offset is applied unconditionally by `IconTexture.Modern` regardless of what the
manifest says. `npc_portraits` is the sole consumer, and it enforces the value hard.

For every content type not listed above, `covers` may be `{}` — or omitted entirely — with no
behavioral difference.

---

## Content types at a glance

| `content_type`        | Entry pattern                                        | ID base            | Accessor                    | Legacy fallback                                   |
| --------------------- | ---------------------------------------------------- | ------------------ | --------------------------- | ------------------------------------------------- |
| `ability_icons`       | `{skill\|spell}{id:D4}.png`                          | 1                  | `GetIconPack()`             | `skill001.epf` / `spell001.epf`                   |
| `item_icons`          | `item{id:D5}.png`                                    | 1                  | `GetItemPack()`             | item EPF sheets in `legend.dat`                   |
| `nation_badges`       | `nation{id:D4}.png`                                  | 1                  | `GetNationBadgePack()`      | `_nui_nat.spf` frame `id - 1`                     |
| `npc_portraits`       | manifest-declared filenames                          | n/a (keyed)        | `GetNpcPortraitPack()`      | `npcbase.dat` SPF frame 0                         |
| `static_tiles`        | `floor{id:D5}.png` / `wall{id:D5}.png`               | raw map tile id    | `GetStaticTilePack()`       | tileset / `stc{id:D5}.hpf`                        |
| `legend_mark_icons`   | `legend{id:D4}.png`                                  | **0**              | `GetLegendMarkIconPack()`   | `legends.epf` frame `id`                          |
| `ui_sprite_overrides` | `{file.ext}/{frame:D4}.png`                          | frame 0            | `GetUiSpriteOverridePack()` | any `setoa.dat` / `cious.dat` EPF/SPF frame       |
| `creature_sprites`    | `creature_sprites/creature_{id:D5}/stand/{n\|e}_001.png` | 1              | `GetCreaturePack()`         | creature MPF                                      |
| `music`               | `music_{id}.{ext}`                                   | raw server id      | `GetMusicPack()`            | `{DataPath}/music/{id}.mus`                       |
| `sound_effects`       | `sfx_{id}.{ext}`                                     | raw server id      | `GetSfxPack()`              | `legend.dat` `{id}.mp3`                           |
| `world_maps`          | `{fieldName}.png`                                    | named, not numeric | `GetWorldMapPack()`         | `{field}.epf` + `{field}.pal` in `setoa.dat`      |
| `town_maps`           | `town_{mapId:D5}.png`                                | raw map id         | `GetTownMapPack()`          | `national.dat` `_t*` five-layer composite         |

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

`Brigid.Data/AssetPacks/ItemPack.cs` — inventory, ground, vendor, and bank item sprites.

```csharp
return HasEntry($"item{spriteId:D5}.png");                     // ItemPack.cs:37
return TryGetImage($"item{spriteId:D5}.png", out image);       // ItemPack.cs:59
```

- Regex: `^item\d{5}\.png$`. **5 digits** — legacy IDs reach 15,965+.
- **1-based**. Guard: `spriteId <= 0` → miss.
- Overrides `ItemRenderer.LoadSpriteFromLegacy` (legacy EPF + palette pipeline).

**Manifest field read: `covers.item_icons.dyeable`** — a flat array of item IDs opting into the
runtime dye pass, loaded fail-closed into `DyeableSet` and exposed as `IsDyeable(spriteId)`.

For a dyeable item with a non-zero server color byte, the renderer replaces pixels matching the
canonical purple ramp (legacy palette indices 98–103:
`#B393C7 #9B7BB7 #8F5BA3 #7F3B93 #47235F #37005B`) with the six colors from the active
`color0.tbl` entry, preserving alpha. Items **not**
listed ignore the color byte entirely, and `ItemRenderer.GetSprite` collapses their cache key to
`color = 0` so one decoded texture serves every incoming color.

Opt-in rather than auto-detected because scanning every pixel of every icon would waste CPU on the
~95% of items with no dye-relevant pixels. `Scripts/dump-item-palette/` produces `items-dyeable.csv`
as a starting list; a hand-editable scaffold lives at `Scripts/sample-packs/items-test-001-002/`.

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
  anything else is silently not indexed. **`mp3` is recommended** — guaranteed by the client's
  bundled SDL2_mixer build (statically-linked minimp3). `ogg`/`flac` need codec libraries not
  currently shipped and fall back to legacy if they fail to decode.
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
  `wav` or `mp3` recommended (WAV natively, MP3 via minimp3); `ogg`/`flac` are not shipped.
- API `TryGetSfxBytes(int soundId, out byte[]?)`.
- Overrides `SoundSystem.LoadChunk` (`Brigid/Systems/SoundSystem.cs:371`) → legacy `legend.dat`
  entry `{soundId}.mp3`. A present-but-undecodable entry falls through to legacy.
- Manifest fields read: **none**.

**Reserved ambient IDs.** Some IDs above the legacy range are reserved by the client for looping
ambient beds. They have **no legacy fallback** — shipping the entry is what enables the sound.

| ID    | Use                | Notes                                                                                                                                                       |
| ----- | ------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 10000 | Rain ambience loop | Loops while the player is on a rain-flagged map. Author loopable end-to-start and **short** (decodes fully to PCM on the game thread). Prefer **`wav`** — MP3 encoder padding bakes a silent gap into the loop seam. |

### Audio notes (both types)

- The mixer runs at **22050 Hz / stereo / signed 16-bit**. Other rates work (the client resamples),
  but matching saves a resample.
- Keep SFX short — they decode into a small (64-entry) LRU cache. Anything long or looping belongs
  in `music`, which streams.
- Malformed entries (corrupt file, non-integer/negative ID, wrong extension, file in a subfolder)
  are "not present" and fall back to legacy. A bad file never breaks playback.

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

## Cross-cutting behavior

- **Entry index.** `AssetPack`'s constructor indexes every `entry.FullName` into a
  `Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase)`. Every lookup in every pack
  is therefore case-insensitive, and forward slash is the path separator regardless of platform.
- **Decode contract.** `TryGetImage` = `TryGetEntryBytes` + `SKImage.FromEncodedData`. Read failures
  and corrupt/non-image data are both reported as "not present". Nothing verifies PNG magic — any
  Skia-decodable format would in fact work despite the `.png` naming convention. Don't rely on that.
- **Disposal.** The caller owns the returned `SKImage`. `AssetPack.Dispose()` disposes the
  `ZipArchive`; the registry disposes a superseded pack when a higher-priority one replaces it.
- **Zip root.** Some Windows ZIP tools wrap contents in a folder named after the archive. If
  `_manifest.json` isn't at the top level immediately, the pack is skipped. Rebuild with contents at
  root.

---

## Troubleshooting

| Symptom                                            | Cause                                                                                              |
| -------------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| Pack not loading at all                            | Check stderr for `[asset-pack]` at startup. Missing/malformed `_manifest.json`, `schema_version > 1`, unknown `content_type`, or the pack is in the game-data folder instead of `AppPaths.AssetsDir` |
| Some assets legacy despite being in the pack       | Filename doesn't match the exact pattern (case-insensitive but padding matters), corrupt entry, or the file isn't at the ZIP root |
| Two packs claim the same type                      | Higher `priority` wins; ties go to first-registered. The loser logs a warning and is disposed      |
| Icon appears 1px off                               | Expected — 32×32 modern icons draw at `-1,-1` relative to legacy 31×31. Verify the PNG really is 32×32 |
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
| `ui_panels`    | Pack-controlled panel layouts (replaces prefab `.txt`)       | `panel_ids` coverage list; schema bump to `2`          |
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

Document repo, `plans/hybrasyl.client/`:

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
- `Scripts/sample-packs/items-test-001-002/` — hand-editable manifest scaffold
- `Scripts/dump-prefab-images.{sh,ps1}` — resolve prefab control names to EPF/SPF stems
- `Scripts/dump-item-palette/` — item dyeability scan (`items-dyeable.csv`)

Packs are authored with **Taliesin** (`../taliesin/`), the Hybrasyl asset-management tool — the
supported path for producing `.datf` archives.
