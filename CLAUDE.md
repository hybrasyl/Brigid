# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Brigid is a Dark Ages MMORPG client built in C# (.NET 10.0) using MonoGame for windowing/graphics and DALib for Dark Ages file format handling. It is the Hybrasyl team's reference client: a clean, modern implementation of the Dark Ages protocol whose first meaningful dev target is the Hybrasyl server (`qa.hybrasyl.com:2610`). It speaks the standard, retail-compatible DA protocol, so it also interoperates with other compatible servers. Licensed under AGPL-3.0-or-later. v0.1.0.

## Build & Run

```bash
dotnet build Brigid.slnx
dotnet run --project Brigid/Brigid.csproj
```

The client requires a Dark Ages game-data directory (the folder containing `*.dat` archives). Point `GlobalSettings.DataPath` at that directory before running; `LobbyHost`/`LobbyPort` and `ClientVersion` also live in `GlobalSettings`.

Release packaging lives in `Build/`: `fixup-sdl-runtimes.sh`, `macos/`, and `linux/package-linux.sh`, which builds the `.deb` (declares the bundled SDL2_mixer's six codec libraries by soname) and the AppImage (bundles them beside the mixer with `$ORIGIN` runpaths, so it needs the ubuntu-24.04 runner's glibc 2.39 or newer). The Linux tarball does neither and relies on the system's codecs. Write local package outputs (`make-app.sh --output`, the `package-linux.sh` `<out-dir>`) to `dist/`, which git ignores. `Build/icon/` holds the icon sources for each platform; see its `README.md`.

Tests live in `Brigid.Tests` (xunit): `dotnet test Brigid.slnx`. Coverage is currently a handful of targeted regression/smoke tests, not a full suite. Tests that change static state (`WorldState`, `DataDiagnostics.Log`, `FontEngine`, `DebugOverlay`, textbox focus) share a named collection with `DisableParallelization = true`; the `AssetPackRegistry` tests have a collection of their own (`RegisterForTest`/`ResetForTest`). The test assembly replaces the SDL backend of `Utilities/Clipboard` with the in-memory `TestClipboard`.

## Solution Structure

```
Brigid.slnx (.NET 10.0, C# 14)
├── Brigid               — MonoGame Game class, screens, UI controls, systems, entry point
├── Brigid.Data           — Asset repositories, DALib integration, archive loading, caching
├── Brigid.Rendering      — Texture conversion, sprite renderers, map rendering, text, camera
├── Brigid.Networking     — TCP client, connection state machine (protocol/crypto via DALib)
└── Brigid.Tests          — xunit regression/smoke tests
```

DALib (Dark Ages file format + networking support) comes from the pinned NuGet package (1.0.0-alpha3) by default. Build with `-p:UseLocalDALib=true` to use the sibling checkout at `../dalib` when co-developing DALib alongside Brigid. DALib models the retail/USDA wire format as ground truth; Hybrasyl protocol divergence lives in `Brigid.Networking/Definitions/HybrasylExtensions.cs`, never in DALib.

**Dependency flow:** Data <- Rendering <- Client, Networking <- Client

## Related Repositories

| Path                | Description                                                            |
|---------------------|-----------------------------------------------------------------------|
| `../server/`        | Hybrasyl server source. Dev target (qa.hybrasyl.com:2610).             |
| `../api/`           | hybrasyl.com web/server interface.                                     |
| `../ceridwen/`      | World test data for the Hybrasyl server.                              |
| `../creidhne/`      | Hybrasyl world data editor.                                            |
| `../taliesin/`      | Hybrasyl asset management — authoring tool for `.datf` packs.          |
| `../epona/`         | Client/server launcher tool.                                          |
| `../dalib/`         | Local checkout of the DALib NuGet package (Hybrasyl-owned).            |
| `../dalib-ts/`      | DALib reimplemented in TypeScript.                                     |
| `../xml/`           | Local checkout of the Hybrasyl XML NuGet package.                      |
| `../lodunity/`      | Decompile of the Unity LOD client.                                    |
| `../Chaos-Server/`  | Chaos-Server source (Sichii). Protocol reference for compat work.     |

The **docs repo** is a private sibling checkout holding the Dark Ages protocol/format reference, the per-project design plans, and the issue tracker. It is referred to by that name throughout this file.

## Issue Tracker

Issues are **not** in Jira. The tracker is git-backed — files are the database — and it lives in the docs repo under `tracker/`:

```text
tracker/config.yaml                        # projects, statuses, issue types, fields, people
tracker/issues/<PREFIX>/<PREFIX>-<n>.md    # one file per issue, sharded by project prefix
```

`BRIG` is this repo's prefix, so `BRIG-64` is `tracker/issues/BRIG/BRIG-64.md`. Each issue is markdown with YAML frontmatter, and grep over that frontmatter is the intended query interface (`grep -rl "^status: in-progress" tracker/issues/BRIG`). An empty result usually means the wrong prefix, not a missing issue. `tracker/config.yaml` is authoritative for the status and type vocabularies — read it rather than trusting any summary.

Design docs for a card live in the docs repo at `docs/plans/brigid/BRIG-<id>-<slug>.md` and are linked from the card's `docs:` frontmatter plus a `HasDesignDoc` label; see that directory's `README.md` before adding one.

**Read-only by default** — read the tracker freely for context, mutate only when asked. Never hand-allocate an issue ID; the tracker has a tool for it, because the obvious `ls | sort | tail` recipe has produced duplicate IDs in practice.

## Key Dependencies

| Package                                    | Purpose                                                                   |
|--------------------------------------------|---------------------------------------------------------------------------|
| DALib                                      | Dark Ages file formats, SkiaSharp rendering, protocol packets + crypto    |
| MonoGame.Framework.DesktopGL 3.8.4.1       | Cross-platform graphics/windowing                                         |
| FontStashSharp.MonoGame 1.5.6              | Runtime font rasterization (`FontEngine`/`TextRenderer`/`UILabel`)        |
| Markdig 1.3.2                              | Markdown parsing (`MarkdownLayoutEngine`; AST only, rendering is ours)    |
| Chaos.Common 1.11.0-preview                | Shared extension methods (NuGet)                                          |
| Chaos.DarkAges 1.11.0-preview              | Dark Ages protocol types (NuGet)                                          |
| Chaos.Geometry 1.11.0-preview              | Geometry types -- rectangles, points (NuGet)                              |
| Chaos.Pathfinding 1.11.0-preview           | A* pathfinding (NuGet)                                                    |
| Microsoft.Extensions.Caching.Memory 10.0.5 | MemoryCache infrastructure                                                |

The `Chaos.*` preview packages (`Chaos.Common`, `Chaos.DarkAges`, `Chaos.Geometry`, `Chaos.Pathfinding`) are inherited from the upstream project and are slated for replacement with Hybrasyl equivalents. `Chaos.Networking` and `Chaos.Cryptography` were already removed in the DALib networking cutover (2026-06/07); the remaining packages supply shared definition enums, extension methods, geometry, and pathfinding only — no protocol serialization.

## Build Configuration

Centralized in `Directory.Build.props`: C# 14, net10.0, nullable enabled, implicit usings, TieredPGO + TieredCompilation (+ QuickJit) enabled, WarningLevel 4, EnforceCodeStyleInBuild. Package versions managed centrally in `Directory.Packages.props`. Versioning is explicit: `<Version>` in `Directory.Build.props` (kept matching the latest release tag) for local builds; release builds stamp `-p:Version` from the git tag in `release.yml`.

## Architecture

### Data Layer (`Brigid.Data`)
- **`DataContext`** -- Static singleton exposing all repositories via `Initialize()`.
- **`DatArchives`** -- Static holder for 22 game data archives loaded at startup via memory-mapped files.
- **`DataPathResolver`** -- Join every path to a game file in the Dark Ages data directory (archives, `npc/`, `music/`) through `Resolve(root, segments)`. Data directories do not agree on filename case (the installer writes `Legend.dat`, an unpacker can write `legend.dat`), and Linux refuses the wrong case. Brigid-owned folders there (`hybrasyl-data/`, the legacy per-character folders) still use `Path.Combine`.
- **`RepositoryBase`** -- Abstract base with MemoryCache (15-min sliding expiration). Uses `GetOrCreate<T>(key, factory)`, which throws on a null load; use `GetOrCreateOrNull<T>` for an asset that can be absent. Neither caches a null.
- **11 repositories** (exposed on `DataContext` by these accessor names): AislingDrawData, CreatureSprites, Effects, Fonts, LightMasks, LocalPlayerSettings, MapsFiles, MetaFiles, PanelSprites, Tiles, UserControls (the UI-component repository).
- **`ControlPrefab`/`ControlPrefabSet`** -- Wraps DALib Control definitions + pre-rendered SKImage arrays. First control (Anchor) defines panel bounds.
- Control file catalog in `controlFileList.txt` at solution root.
- **`AssetPackRegistry`** (`AssetPacks/`) -- Static registry. On `Initialize()`, scans the per-user Erisco assets directory (`AppPaths.AssetsDir`) for `*.datf` files (ZIP archives with modern asset overrides), reads each one's `_manifest.json`, validates `schema_version`, and registers each pack under its `content_type`. A type can have several packs (BRIG-15), in order of the manifest `priority` (highest first; the default is 100), and at the same priority in registration order (file-name order). A lookup asks for a key and tries each pack of the type in that order, so the higher priority wins a shared key (a temporary pack, for example a holiday pack, uses this), and registration warns about shared keys and names the pack that a lookup asks first (through `DataDiagnostics.Log`). For an image key a bad entry passes to the next pack; a creature is owned by the first pack that covers it (`GetCreaturePackFor`), and audio callers walk `SfxPacks`/`MusicPacks` themselves. `assetPackList.md` has the table. On first launch it performs a one-time best-effort migration of legacy `{DataContext.DataPath}/hybrasyl-data/` packs (the `LEGACY_PACK_SUBFOLDER`) into the assets directory. A lookup that no pack answers returns false or null; the renderer/audio layer falls through to legacy. Full per-type catalog (entry naming, ID bases, manifest fields actually read, legacy fallbacks, enforced constraints) is in `assetPackList.md` at solution root; `asset-pack-format.md` in the docs repo is the artist-facing format spec. These content types are supported. Each has an ordered `…Packs` sequence and a key lookup, except `ui_theme`, where one theme is active:
  - `ability_icons` -> `IconPack` -> `TryGetIconImage(prefix, id)`
  - `item_icons` -> `ItemPack` -> `ItemPacks`, through `ItemIconSource.Load(sprite, color)` in `Brigid.Rendering` (every item surface; ids to 32767). Dye (BRIG-41): an id below 20001 is dyeable when its PNG has a canonical-ramp pixel (`ItemDyeRamp`) and it is not in `covers.item_icons.no_dye`; an explicit `dyeable` list is the exact set; an id of 20001 or more is never dyed by the ramp swap (mask dye is BRIG-112)
  - `nation_badges` -> `NationBadgePack` -> `TryGetBadgeImage(id)`
  - `npc_portraits` -> `NpcPortraitPack` -> `TryGetNpcIllustration(key)`
  - `static_tiles` -> `StaticTilePack` -> `TryGetFloorImage(id)` / `TryGetWallImage(id)` (`HasStaticTilePacks` gates the map preload)
  - `legend_mark_icons` -> `LegendMarkIconPack` -> `TryGetLegendMarkImage(id)`
  - `ui_sprite_overrides` -> `UiSpriteOverridePack` -> `TryGetUiSprite(file, frame)`
  - `creature_sprites` -> `CreaturePack` -> `GetCreaturePackFor(spriteId)` (per-creature auto-trim)
  - `music` -> `MusicPack` -> `MusicPacks` (streamed; entries `music_{id}.{ext}`)
  - `sound_effects` -> `SfxPack` -> `SfxPacks` (decoded; entries `sfx_{id}.{ext}`)
  - `ambient_sounds` -> `AmbientPack` -> `GetAmbientPackFor(id)` (the first pack that has the id owns the bed: its sound and its loop flag; decoded; entries `amb_{id}.{ext}`, ambient ids 1–255, no legacy fallback; `covers.ambient_sounds["<id>"].loop: false` makes a one-shot, every other id loops)
  - `world_maps` -> `WorldMapPack` -> `TryGetFieldImage(name)` (overworld field backgrounds; entries `{fieldName}.png`)
  - `town_maps` -> `TownMapPack` -> `TryGetTownMapImage(mapId)` (T-key town maps by map number; full-panel entries `town_{mapId:D5}.png`, drawn via `UiRenderer.GetTownMapImage` in place of the legacy `national.dat` `_t*` composite; no player marker)
  - `ui_theme` -> `UiThemePack` (art, colours and fonts for the asset-free primitives, named by role in `theme.json`; drawn by `ThemeArt`) -> `UiThemePacks`, and `GetUiThemePack()` returns the active theme: the pack whose `pack_id` the player selected (`SelectUiTheme`, from `ClientSettings.UiTheme`), else the first pack; `none` makes no theme active. Themes are alternatives, not layers: a part that the active theme does not have takes the flat look, never another theme's part
- **`AudioPack`** (`AssetPacks/`) -- Shared base for `MusicPack`/`SfxPack`/`AmbientPack`. Entries named `{prefix}_{id}.{ext}` at the archive root; builds an extension-agnostic `id -> entry` map at construction and returns raw bytes via `TryGetAudioBytes` (no decode — the audio layer hands bytes to SDL_mixer). `MusicPack` overrides loose `{DataPath}/music/{id}.mus`; `SfxPack` overrides `legend.dat` `{id}.mp3`. A present id = replace, a new id = add.
- **`IconPack`** (`AssetPacks/`) -- Wraps a ZipArchive of `{prefix}{id:D4}.png` entries. `TryGetIconImage(prefix, spriteId, out SKImage?)` case-insensitive lookup; decode failures treated as "not present" so renderer falls back cleanly to legacy.
- **`DataDiagnostics`** -- Log seam for `Brigid.Data` and `Brigid.Rendering`, which cannot reference `NoticeDebugLog`. `Program.cs` sets `Log` to `NoticeDebugLog.Write`; `Try` runs best-effort file IO and logs a failure.
- **`MetaFileCache`** -- Static helpers for the metafile cache in `AppPaths.MetaFileDir`. `IsSafeName` refuses a server-sent name that is not a bare, cross-platform file name; `GetChecksum`, `TryWrite` and `PruneUnlisted` (deletes the files the server no longer lists) apply it.
- **`AppPaths`** -- Per-user `Erisco/Brigid` directories (`AppRoot`, `AssetsDir`, `MetaFileDir`, `MapsDir` and others). `MigrateLegacyVendorFolder()` runs once in `Program.cs`, before anything reads `AppRoot`, and moves a legacy lowercase `erisco` folder entry by entry.

### Rendering Layer (`Brigid.Rendering`)
- **`TextureConverter`** -- DALib `SKImage` -> MonoGame `Texture2D` (RGBA8888 premul). Entry points: `ConvertImage<T>()`, `ToTexture2D()`.
- **`ImageUtil`** -- Static class of stateless CPU pixel-manipulation primitives: tint/blend variants (`Blend50`, `ApplyHoverTint`, `ApplyGroundTint`, `BuildGroupTinted`, `BuildHitTinted`, `BuildHoverTinted`, `BuildGroundTinted`, `BuildCooldownTintedCached`), checker pattern, vertical alpha gradient, filled border, rectangle fill, projected-quadrants raster, chat-bubble body + tail, 2x2 box downsampler (`SKColor[]`), comb-dissolve kernel. Naming convention: `Build*` returns a new `Texture2D` (or `CachedTexture2D`); `Apply*`/`Fill*`/`Draw*` mutate a `Color[]` in place. No global state -- all device-requiring helpers take an explicit `GraphicsDevice`.
- **`PixelBufferScope`** -- `ref struct` RAII wrapper around `ArrayPool<Color>.Shared.Rent` + `Texture2D.GetData`/`SetData`. Use this instead of hand-rolling rent/get/set/return. Two constructors: `(Texture2D source)` reads the texture into a fresh buffer; `(int width, int height)` rents an **uninitialized** buffer (callers must `Array.Clear(scope.Pixels, 0, scope.Count)` if they depend on zeros). Exposes `Pixels`, `Count`, `Width`, `Height`, `AsSpan()` for bounds-safe iteration; `CommitTo(Texture2D)` uploads pixels back via `SetData`. This is the *only* place outside tests that should call `ArrayPool<Color>.Shared.Rent`.
- **`Camera`** -- Isometric camera: `WorldToScreen`, `ScreenToWorld`, `TileToWorld`, `WorldToTile`, `GetVisibleTileBounds()`.
- **`MapRenderer`** -- Background + foreground tile rendering. `DrawBackground()`, `DrawForegroundTile()`, `PreloadMapTiles()`.
- **`TextRenderer`** -- Higher-level text concerns layered on `FontEngine` (the glyph backend is FontStashSharp/TTF, not SkiaSharp): inline `{=x}` color codes, drop shadows, per-glyph clipping, measurement, wrapping. `DrawText()`, `DrawTextClipped()`, `DrawLines()`, `MeasureWidth()`, `FindLineBreak()`, `WrapLines()`, `WrapText()`. Both wrap methods continue an inline `{=x}` code onto the lines after it. `CHAR_HEIGHT` (12) is the line grid; glyph ink size is independent of it. `FindLineBreak` advances whole Unicode scalars, so a returned index is always a UTF-16 boundary -- breaking a surrogate pair leaves lone halves that sanitize to U+FFFD.
- **`UiRenderer`** -- UI panel rendering utilities.
- **`DarknessRenderer`** -- Light/darkness overlay. Consumes light sources from `LightingSystem`.
- **`TabMapRenderer`** -- Mini-map rendering. Also consumes from `LightingSystem` for fog-of-war.
- **`WeatherRenderer`** -- Snow/rain overlay driven by the low nibble of `MapFlags` (1=Snow, 2=Rain, 3=Darkness handled by `DarknessRenderer`).
- **`SilhouetteRenderer`** -- Silhouette effect for blocked entities.
- **`PaletteCyclingManager`** -- Animated palette shimmer effects.
- **`FontAtlas`** -- Font glyph atlas management.
- **`FontEngine`** -- FontStashSharp-backed text backend: loads real TTFs, rasterizes glyphs on demand into a dynamic atlas (anti-aliased, full Unicode incl. CJK fallback), replacing the legacy `.fnt` bitmap fonts. Multiple selectable faces via `CycleFont`/`SetActiveFont`, persisted by the client. Faces may ship bold/italic variant files selected per call via the `[Flags]` `FontStyle` enum (missing variants fall back to regular; `FontStyle.Mono` redirects to the Noto Sans Mono face for code spans; `FontStyle.Display` redirects to the display face for titles, Uncial Antiqua, which is never a selectable face and which a `ui_theme` pack can replace through `SetDisplayFont`). Styled `DrawLine`/`MeasureWidth`/`GetLineHeight` overloads take an explicit pixel size (used by markdown headers/code); implements `ITextMeasurer` so layout logic is testable without fonts. `Generation` bumps on face change *and* layout-scale change (window resize) so measurement caches invalidate.
- **`MarkdownLayoutEngine`/`MarkdownLayout`** (`Markdown/`) -- Pure markdown layout: parses via Markdig and lays out positioned styled spans + code-background/rule rects at a fixed wrap width through an `ITextMeasurer`. Supports headings, emphasis, lists, thematic breaks, fenced/inline code; everything else degrades to plain text (links render their text, hostile over-nested input falls back to verbatim text instead of throwing). Consumed by `MarkdownView`; colors are the view's concern via `MarkdownSpanKind`.
- **`ThemeArt`/`ThemeLayout`/`ThemeRoles`** -- Draws the parts of the `ui_theme` pack: nine-slice frames and button states, and cover or contain fits. Like glyphs, a part is composited with SkiaSharp at the native pixel size of its rectangle and drawn with the inverse of the UI pass scale, so high-resolution art stays sharp. It reads the pass scale from `FontEngine.NativeScale`, the one place that holds it. `ThemeTextureCache` keys textures on role, native size and pass scale, and disposes an evicted texture only after a delay, because a deferred `SpriteBatch` can still hold it. `ThemeLayout` holds the pure geometry; `ThemeRoles` names the roles the primitives read.
- **`BuiltInArt`** -- Brigid's own default art (BRIG-123), keyed by the `ui_theme` role that a theme part replaces: the embedded `Brigid.Rendering/Resources/BuiltIn/{role}.png`, drawn like a theme part with no slice. A control draws the theme part, then this art, then its legacy art, so that it can run with no dat file. A role with no image draws nothing and returns false. The eight social status icons ship; each image has an SVG source in `Resources/BuiltIn/src/`, and `Scripts/build-builtin-art.py` (needs `resvg-py`) renders the PNGs. `SetEnabledForTests(false)` hides the images, for the tests of the legacy fallback.
- **`IconArt`** -- The new art of an icon role (`icon.*`): the `ui_theme` part, else `BuiltInArt`. An icon is content, so it shows on every host, also one that does not take the theme; do not draw an icon role through `TryDrawThemePart`. `Has` checks that the image decodes, so a host reads its legacy art only when there is no usable new art.
- **`CreatureRenderer`/`AislingRenderer`/`EffectRenderer`/`ItemRenderer`** -- Per-frame texture caches. `Clear()` on map change.
- **`ItemIconSource`** -- The one item-image resolver, for every item surface: `ItemRenderer` (ground items, dialog portrait) and `UiRenderer.GetItemIcon` (inventory, bank, shop, item menu, exchange, equipment). Removes the `0x8000` wire flag (`& 0x7FFF`), asks the `item_icons` packs in order (a bad entry passes to the next pack; the pack that gives the image also decides the dye), then the legacy EPF. Item ids follow `Brigid.Data/AssetPacks/ItemSpriteIds` (`ToSpriteId`, modern from `MODERN_FIRST` = 20001, `MAX` = 32767). `GetItemIcon` returns an `IconTexture`; a pack icon gets `IconTexture.FromPack` = `SlotOffset(size)` = `floor((31 − size) / 2)` on each axis.
- **`LegendColors`** -- Named color constants for UI text. Initialized at startup.
- **`LightSource`** -- Light source model for darkness system.
- **`RenderHelper`** -- Shared rendering utility methods.
- **`CacheExtensions`** -- Extension methods for consistent dictionary cache management across renderers.
- **`TextureAtlas`/`AtlasHelper`/`CachedTexture2D`** -- Grid/Shelf packing for performance optimization.
- **`SpriteAnimation`/`SpriteFrame`** -- Frame array with `GetFrame(index)`, timing, additive blending.
- **`EntityHitBox`** -- Hit testing geometry for clickable entities.
- **`IconTexture`** -- Record struct `(Texture2D Texture, int OffsetX, int OffsetY)` with `Legacy(t)`/`Modern(t)`/`FromPack(t)` factories, an `Offset` vector and a `Draw(sb, pos, tint?)` helper. `FromPack` centres a PNG of any size in the 31×31 footprint (`SlotOffset`); `Modern` is that rule for a 32×32 ability icon. Wraps the icon returns of `UiRenderer.GetSkillIcon`/`GetSpellIcon`/`GetItemIcon` so modern 32×32 icons from `.datf` packs (offset -1/-1) can coexist with legacy 31×31 EPF icons (offset 0/0) in the same pipeline. Offset propagates through `UIImage.TextureOffset` and `UIButton.TextureOffset`; the bank/shop and item-menu lists carry an `IconTexture` per row for the same reason.
- **Asset pipeline:** `DatArchives -> Repository -> Palettized<T> -> DALib Graphics.RenderXxx() -> SKImage -> TextureConverter.ToTexture2D() -> Texture2D -> SpriteBatch.Draw()`

### Networking Layer (`Brigid.Networking`)
- **`GameClient`** -- Owns the TCP socket; hands framing, encryption, and (de)serialization to DALib's `PacketCodec` (stateless, shared across connections; per-connection `CryptoState` replaced wholesale at each handshake). Inbound packets surface as typed `IServerPacket` values via `DrainPackets()`; outbound are sent as typed `IClientPacket` via `Send()`. Auto-responds to byte/tick heartbeats. `TryGetTcpSmoothedRttMs()` exposes the kernel's RTT estimate for `LatencyMonitor`. `ConnectAsync` runs one connect at a time: a new connect or `Disconnect` cancels the connect in progress, and `Disconnect` drops the packets that the game loop did not handle yet. `ConnectionManager` numbers each hop, so a superseded connect changes no state and raises no error.
- **`ConnectionManager`** -- State machine (Disconnected->Connecting->Lobby->Login->World), array-indexed handler dispatch (60+ handlers), 48+ events. Full lobby/login/world-entry flows. Player action methods, communication, NPC/dialog, requests. `ProcessPackets()` (called from the game loop) drains the client queue and dispatches; handler exceptions are logged to `NoticeDebugLog` rather than rethrown, so protocol divergence is visible instead of fatal.
- **`NoticeDebugLog`** -- Static diagnostic logger (stdout + `notice-debug.log` in the app base directory). The client's standing debug channel (`Brigid` and `Brigid.Networking`). `Brigid.Data` and `Brigid.Rendering` write through `DataDiagnostics.Log`, which `Program.cs` sets to it.
- Protocol types come from DALib: packets in `DALib.Networking.Packets.Client`/`.Server`, opcodes + codec in `DALib.Networking.Wire`, crypto in `DALib.Networking.Crypto`. Shared definition enums (stats, directions, etc.) still come from `Chaos.DarkAges.Definitions` / `Chaos.Geometry`. The server-table blob is parsed by DALib's `ServerTableDataPacket`; the `name;description` field is split client-side via `ServerEntryExtensions.SplitNameDescription()`.

### Client Project (`Brigid`) Internal Organization

```
Brigid/
├── ChaosGame.cs              — MonoGame Game class, entry point
├── Program.cs                — Process entry point (Main)
├── GlobalSettings.cs         — Static config (ClientVersion, DataPath, LobbyHost/Port)
├── InputBuffer.cs            — Event-driven input capture and buffering
├── InputDispatcher.cs        — UI event dispatch: hit-test, bubble, drag, click synthesis, control stack
├── Sdl.cs                    — Centralized SDL2 P/Invoke declarations (keyboard, text, mouse button, mouse wheel event constants consumed by InputBuffer; audio subsystem init/quit, SDL_GetError, SDL_RWFromConstMem consumed by SoundSystem; SDL_GetClipboardText/SDL_SetClipboardText/SDL_free/SDL_ClearError consumed by Utilities/Clipboard; SDL_RaiseWindow consumed by ChaosGame.RaiseWindow)
├── SdlMixer.cs               — SDL2_mixer P/Invoke wrapper (Mix_* functions and constants, consumed by SoundSystem)
├── Collections/              — WorldState, CircularBuffer
├── Models/                   — WorldEntity, Animation, EntityRemovalAnimation, WorldFrameState, SlotDragPayload, PathfindingState, etc.
├── ViewModel/                — Authoritative state classes owned by WorldState
├── Systems/                  — AnimationSystem, CastingSystem, SoundSystem, Pathfinder, LightingSystem, LatencyMonitor, ClientSettings, MachineIdentity, AbilityRequirements
│   └── Keybinds/             — CommandId, KeyChord, KeybindCatalog, Keybinds (rebindable key resolution)
├── Screens/                  — IScreen, ScreenManager, LobbyLoginScreen, WorldScreen (7 partial files)
├── Rendering/                — EntityOverlayManager, WorldDebugRenderer, SystemTextPainter
├── Controls/                 — Full UI control hierarchy (see UI Control System below)
│   └── Scrolling/            — ScrollModel, IScrollSource, ScrollView, ScrollBarBinder, VirtualizedListView, SelectableTextView, LabelScrollSource, TextBoxScrollSync
├── Definitions/              — Delegates, Enums, DoorTable, InputEvents, TextColors
├── Extensions/               — DirectionExtensions, RectangleExtensions, UIElementExtensions
└── Utilities/                — Clipboard, DialogFrame, Browser, FolderPicker, VersionInfo
```

### Screen System
- **`IScreen`/`ScreenManager`** -- Stack-based screen management.
- **`LauncherScreen`** -- Zero-config startup screen (server select + theme + asset-folder picker + Connect; the theme list comes from `AssetPackRegistry.ScanUiThemes`, which reads only the pack manifests before the registry initializes, and the choice goes to `FinishAssetInitialization`, which saves it after `ClientSettings.Load`), shown on every launch unless env vars (`DA_HOST`/`DA_ASSET_PATH`) fully specify a valid setup. **The one deliberately non-`UIElement` screen:** it renders before `DataContext.Initialize` (the asset path is what it exists to obtain), so it cannot use prefab panels/atlas fonts -- it draws with a 1x1 white texture + `SystemFontText` (OS typeface) and reads `InputBuffer` directly (`Root => null`, not in the `InputDispatcher` tree). `Connect` persists `LauncherConfig` then calls `ChaosGame.FinishAssetInitialization()` (the deferred asset-load seam) and switches to the lobby. Its drawing primitives (OS-typeface text, rectangles snapped to the native pixel grid, and their caches) are in `SystemTextPainter` (`Brigid/Rendering/`); put new asset-free drawing there, not in the screen.
- **`LobbyLoginScreen`** -- Full login flow: lobby connect, server select, login, character creation, transition to world. It shows the primitive `MainMenuControl`, the server greeting and the three lobby forms; the legacy `_nsvr` server list is the one prefab panel left. A server answer goes to the busy form (`LobbyFormPanel.IsBusy`). With no connection, the menu shows an Offline alert with Reconnect. The screen sets `InputDispatcher.EnforceModals`.
- **`WorldScreen`** -- Main game screen, split into 7 partial class files:
  - `WorldScreen.cs` -- Base class, fields, construction
  - `WorldScreen.Draw.cs` -- Render logic (diagonal stripe entity interleaving, overlays)
  - `WorldScreen.Update.cs` -- Game logic update
  - `WorldScreen.InputHandlers.cs` -- Keyboard/mouse input (movement, hotkeys, pathfinding, click-to-interact)
  - `WorldScreen.ServerHandlers.cs` -- Network packet handler subscriptions
  - `WorldScreen.Wiring.cs` -- Event subscription setup
  - `WorldScreen.Map.cs` -- Map management

### UI Control System

**Component Primitives (`Controls/Components/`):** UIElement, UIPanel, UIButton, UITextBox, UIImage, UILabel, UIProgressBar, TextElement, PrefabPanel (prefab-art panels), DialogPalette (the one colour source; `ForRole` gives each `ui_theme` colour role its one flat value, which `ThemeColor(role)` and `SetForegroundRole(role)` use), InputSlotPane (draws slot chrome behind a form's text fields; `input.slot` art when themed). Theme colours (BRIG-120): a `UILabel` or `UITextBox` whose colour is part of the look calls `SetForegroundRole(role, fallback)` once, and draws the role colour when themed (its `ForegroundColor` stays the fallback); a control whose fill or border is part of the look overrides `DrawnBackgroundColor`/`DrawnBorderColor`. Do not set colours from the theme in `Draw`. A colour that carries meaning (secure, cooldown, a server-sent name colour) stays literal.

**Login Flow (`Controls/LobbyLogin/`):** MainMenuControl (the BRIG-107 primitive main menu: themed, every child named; its About text and `story.json` ship as embedded resources in `Resources/MainMenu/`), AlertsPanel (the menu's non-modal local alerts: tabs, a next arrow and per-alert actions; the alert content is in `MenuAlerts`: no sound, the legacy settings import, offline, a server notice that cannot be read, an assets folder that cannot be listed), StoryView (the modal Story pages from `StoryScript`: slide, text and cutscene placeholder), ServerGreetingControl (the modal primitive login notice: OK, Cancel, Escape blocked), LobbyFormPanel (the base of the three primitive lobby forms: label and field rows, a status line in `text.error` for errors, a busy state that disables submit and makes the text fields read-only (the creator also locks its look controls), Enter submits and Escape cancels), CharacterCreatorControl (the BRIG-116 primitive creator: walking preview, name and passwords, cosmetic species and body type `DropDown`s that list every entry but enable only Human and Type A, gender tabs, hair `Stepper`, `ColorSwatch` grid from `color0.tbl`, errors in its status line; its rules are in `CreatorAppearance`), LoginFormControl and PasswordFormControl (the BRIG-119 primitive login and password-change forms). While one of the three lobby forms waits for the server it stays shown and can be cancelled; a cancel, or a wait of more than 20 s, closes the connection and connects to the lobby again, so that the late answer does not arrive, ServerSelectControl (the legacy `_nsvr` server list).

**Generic Controls (`Controls/Generic/`):** CenteredModalPanel (base for from-scratch modals: DialogPalette frame, title band, per-view bottom action bar via `SetBottomBarActions`, Escape-dismiss, control-stack push; `useTheme: true` themes the modal from its constructor and grows it by the extra width of the themed frame, so `ContentBounds` keeps its flat size and the constructor can lay out from it; construct such a modal after the asset packs load; it swallows every key it does not use, unless it overrides `LetsKeysThrough`; it centers in its viewport, unless it overrides `PositionIn`), OkPopupMessageControl (BRIG-121: a themed `CenteredModalPanel` message popup with OK and an optional Cancel; takes the theme, and its height fits the wrapped message), TextPopupControl (BRIG-122: a themed `CenteredModalPanel` for the server's ScrollWindow, NonScrollWindow and WoodenBoard text; the wooden sign hides the frame chrome and draws the `frame.sign` part, or the flat frame, with the sign's text margins; it reads no legacy art), MarkdownView (near-fullscreen markdown notice for `SystemMessageType.MarkdownNotice` = 0x20, a Hybrasyl/USDA extension: title bar with close/maximize, scrollbar, Escape dismisses, non-modal; debug-test via F11 overlay + Ctrl+M), PrimitiveGallery (BRIG-122 debug panel: Ctrl+G while the F11 overlay is on opens a panel that shows one of each primitive in each state and a chip for each theme colour, each with its `ui_theme` role; with no pack it is the reference sheet for the flat look; while it is open the overlay layer toggles (`DebugOverlay.Layers`) are off, and its `Dispose` puts them back; `TextButton.ShowState` and `CheckBox.ShowState` are its internal hooks), ScrollBarControl (vertical or horizontal; each orientation has its own `scrollbar.*` theme parts, because the art is drawn upright), SliderControl (BRIG-55: a primitive, texture-free slider for 0 to 10; `slider.track`, `slider.fill` and `slider.thumb` parts when themed; its pointer-to-value mapping is the pure `ValueAt`), DebugOverlay (while it is on, `UIPanel.DrawOverChild` draws its box and name over each child, so `Components` does not depend on it). Asset-free primitives shared by the from-scratch modals: DisplayTitle (one line in the display face, or theme art in its place), ThemePicker (a labelled drop-down of theme entries with a "Restart to apply" hint; the Options modal gives the entries from `Systems/UiThemeChoices` (Automatic, each installed `ui_theme` pack and None, the same list as the launcher's Theme row) and saves `ClientSettings.UiTheme`, applied at the next start), FramePanel (a framed box, or the theme's `frame.panel` art, or another part through the `FrameRole` override; `GrowAroundContent(padding)`, called once at the end of a constructor, grows a hand-laid-out panel around a wider themed frame and returns the offset to add to positions it draws itself; `BankShopPanel` and `WorldListControl` use it), MarkdownBlock (laid-out markdown with no frame or scroll bar; MarkdownView, the alerts and the Story use it), TextButton, SelectableTab (the one tab primitive: `TabStrip`, the Aisling list filters and the bank/shop categories use it; `tab.*` parts when themed), TabStrip (row/column of tabs; clicked and selected are separate so a server-authoritative surface can defer selection to the reply), CheckBox, DropDown (a box and an open list below it; box in the button parts, list in `dropdown.list`; text clears a sliced part's border; a disabled entry shows grey and italic and cannot be selected; the host gives it a ZIndex above the rows its list covers; while its list is open it is on the control stack, as `PopupMenu` is, so Escape closes the list before the host gets it), Stepper (previous, value, next; the host owns the value), ColorSwatch (a colour box with a selected border), PopupMenu (BRIG-125: a `FramePanel` that a caller opens at a point, with a title row and entries; draws the `frame.popup` part, not `frame.panel`, else the flat frame; fits its widest text and stays in the viewport; the mouse or Up/Down select a row in `row.hover`, a click or Enter picks it, Escape or a right-click closes; it is on the control stack while open, so a host that hides its parent hides it too), TooltipFrame (BRIG-125: lines of wrapped text in a `frame.popup` frame at the cursor through `TooltipPlacement`; a line takes a theme role or a literal colour that carries meaning; the bank and shop window keeps its tooltips with no parent and draws them over its content, so it does not clip them), StatusIcon (BRIG-122: a social status icon; the new art of its `icon.status.*` role from `IconArt` on any host, themed or not, else the legacy frame that the host gives, read only when there is no new art; the status book, the other-profile view and the Aisling list show it, and `ShowOn` puts it over the HUD emote button).

**World HUD (`Controls/World/Hud/`):** IWorldHud interface, WorldHudControl (classic compact HUD), LargeWorldHudControl (expanded HUD), OrangeBarControl, ChatInputControl, EffectBarControl/EffectSlotControl, MailButton (unread-mail pulse indicator driven by `PlayerAttributes.HasUnreadMail`).

**HUD Panels (`Hud/Panel/`):** PanelBase, ExpandablePanel, InventoryPanel, SkillBookPanel, SpellBookPanel, ToolsPanel, ChatPanel, StatsPanel, ExtendedStatsPanel, SystemMessagePanel, StatButton. Slots: PanelSlot, AbilitySlotBase, SkillSlot, SpellSlot.

**Self Profile (`Popups/Profile/`):** SelfProfileTabControl with Equipment/Legend/AbilityMetadata/Events/Family/Blank tabs, SelfProfileTextEditorControl (BRIG-122: a themed `CenteredModalPanel` profile text editor; its text box keeps the legacy 174 by 152 size, so it takes the same text), AbilityMetadataDetailsControl/AbilityMetadataEntryControl, EventMetadataDetailsControl/EventMetadataEntryControl (BRIG-122: the two detail popups are themed `CenteredModalPanel`s on `StatusBookDetailsPanel`; the height fits the description up to 14 lines, and the wheel scrolls a longer one; a click on or outside the popup, Escape or Enter closes it), LegendMarkControl. **Other Profile:** OtherProfileTabControl (Equipment via _nui_eqa + Legend tabs), OtherProfileEquipmentTab. (Legend tab reuses `SelfProfileLegendTab`.)

**Options (`Popups/Options/`):** OptionsModalControl (tabbed Settings|Macros|Friends|Keybinds modal), KeybindsTabControl, KeybindCaptureControl, PauseMenuControl (BRIG-55: a themed `CenteredModalPanel` with three `SliderControl`s and Settings / Exit Game `TextButton`s; it passes every key but Escape and X on to the world screen, so Q closes it again).

**Popups (`Popups/`):** AislingContextMenu and DoorContextMenu (BRIG-125: `PopupMenu`s; the aisling's name, then info, group request and whisper; "Doors", then up to three doors from the caller), AmountPromptPanel (BRIG-122: a themed `CenteredModalPanel` base for the digits-only amount prompts GoldAmountControl and ItemAmountControl), CastablePopupControl (+CastableDetailsPane), GroupTabControl (BRIG-126: the group window, a themed `CenteredModalPanel` at the top left of the viewport that lets keys through; a `TabStrip` with the Members tab `GroupTab` (13 rows: the name, "Leader", and Kick for the leader) and the Recruit tab `GroupRecruitPanel`), GroupRecruitPanel (the recruit form, with no buttons: the host shows the actions of its `Mode` on the bottom bar and calls `BeginBox`, `ModifyBox`, `RemoveBox` or `RequestJoin`), GroupBoxViewerControl (another player's group box: the same form, read-only, with Join), HotkeyHelpControl, ItemTooltipControl (BRIG-125: a `TooltipFrame`; the name in `text`, the durability in literal blue), NotepadControl, SocialStatusControl, TownMapControl. Subdirectories: `Boards/` (BoardsModalControl + BoardIndexPane/PostListPane/PostReadPane/PostComposePane), `Dialog/` (NpcSessionControl, FramedDialogPanelBase, DialogAlphaGradient, BankShopPanel, DialogTextEntryPanel, DialogProtectedTextEntryPanel, MenuTextEntryPanel, DialogOptionPanel, MenuListPanel), `Exchange/` (ExchangeControl, BRIG-126: a themed `CenteredModalPanel` that is not modal and lets the keys it does not use through (`LetsKeysThrough`; the screen's stack guard still blocks the hotkeys below it); two columns, `my` and `their`, each with the name, four `ExchangeItemControl` rows (a row grows to its name) with a vertical and a horizontal bar, and gold; the gold button raises `OnGoldClicked`, and the exchange's close also closes the gold and item-count prompts), `WorldList/` (WorldListControl, with its nested NameRow).

**Viewport Overlays (`ViewPort/`):** ChatBubble, HealthBar, LoadingBar/MapLoadingBar, WorldMap/WorldMapNode, ChantText, GroupBox (BRIG-126: a `FramePanel` with the `frame.popup` part; it sizes itself around the legacy 97×13 title area each time its text is set, so `EntityOverlayManager` reads its size after `UpdateText`), SystemMessagePaneControl, PersistentMessageControl.

### Game Systems (`Brigid/Systems/`)
- **`AnimationSystem`** -- Pure methods for walk/body/creature animations, frame calculation, walk offset lerp.
- **`CastingSystem`** -- Spell targeting + chant management.
- **`SoundSystem`** -- SDL2_mixer-based audio. SFX decoded to PCM once via `Mix_LoadWAV_RW` and cached as `Mix_Chunk` pointers; playback uses the mixer's channel pool with per-channel volume. Music streams via `Mix_LoadMUS`/`Mix_LoadMUS_RW` with `Mix_FadeOutMusic`/`Mix_FadeInMusic` for map transitions. When the same sound id plays again while an earlier instance is still audible, the earlier instances fade out over 200 ms (`Mix_FadeOutChannel`; per-id voice stealing, as the retail client restarts its one sample per id). Both paths are pack-first: `LoadChunk` prefers `AssetPackRegistry.SfxPacks` over `legend.dat`, and `StartMusic` prefers `MusicPacks` (streamed from a pinned byte[] via `Mix_LoadMUS_RW`) over the loose `{DataPath}/music/{id}.mus`. The pinned music buffer is released in lockstep with the music handle by `FreeCurrentMusic`. Before any `Mix_*` call the constructor probes the mixer via `DllResolver.TryResolve` (the resolver plus the runtime's default search, the order a p/invoke uses; `NativeLibrary.TryLoad` with an assembly skips the resolver). If SDL2_mixer or a codec library it links cannot load -- the Linux build links six system codecs -- `MixerLibraryMissing` is set, every call no-ops, and `LobbyLoginScreen` adds the no-sound alert to the main menu once per process (`MenuAlerts.NoSound`: per-distro install commands on Linux, reinstall advice elsewhere).
- **`SoundSystem` ambient bed** -- `StartAmbient(ambientId)`/`StopAmbient()` play one bed (rain, from `WorldScreen.UpdateWeatherAmbience` on the weather nibble) at the Ambience volume (`SetAmbienceVolume`, applied live). Ambient ids are their own number space: the chunk cache and channel tracking key on `SoundKey` (kind + id), and a bed comes only from `GetAmbientPackFor(id)` (rain = `amb_1`, `WorldScreen.RAIN_AMBIENT_ID`); no pack entry is a silent no-op, and an id of 0 or less stops the bed. The bed is not in the sound-effect channel tracking while it plays, so voice stealing does not touch it; at a stop the fading channel goes into that tracking, so its chunk is not freed during the fade. A start that fails only because every channel is busy retries from `Update()`. The bed fades in and out over 200 ms; at Ambience volume 0 the mixer refuses the fade, so a stop halts the channel. A bed loops unless its pack marks it as a one-shot, which plays one time and does not play again until a different id or a stop.
- **`Pathfinder`** -- A* pathfinding algorithm.
- **`LightingSystem`** -- Owns the per-frame light source buffer. Walks world entities, reads `LanternSize`, and gathers into a span consumed read-only by `DarknessRenderer` and `TabMapRenderer` (neither stores its own copy). Caches Euclidean circle offset arrays (radius 3/5) and exposes `BaselineVisibilityOffsets` for the unconditional player-tile reveal on darkness maps.
- **`LatencyMonitor`** -- Static class. Passive sink for application-layer round-trip-time samples. Producers call `Update(long? rttMs)` (null clears the reading); the HUD subscribes to `LatencyChanged` and reads `LatencyMs`. May fire on any thread — subscribers must not block, and any non-trivial work should be marshalled to the game-loop thread by the consumer. Producer: `ChaosGame` runs a 2s polling task while in World state that calls `GameClient.TryGetTcpSmoothedRttMs` — real kernel-measured smoothed RTT, no protocol changes. Cross-platform via `SIO_TCP_INFO` IOCTL (Windows), `getsockopt(IPPROTO_TCP, TCP_INFO)` (Linux), or `getsockopt(IPPROTO_TCP, TCP_CONNECTION_INFO)` (macOS). Clears via `Update(null)` on World exit.
- **`MachineIdentity`** -- Machine-specific identification for the client.
- **`ClientSettings`** -- Static class. Persistent user settings. Access via `ClientSettings.SoundVolume`, etc.
- **`Keybinds`** (`Systems/Keybinds/`) -- Static rebindable-key registry. A `CommandId` names an action; a `KeyChord` is a key + `ChordMods` (`Meta` is the per-OS primary modifier -- Ctrl on Windows/Linux, Cmd on macOS); `Defaults` maps command to chord plus a `KeybindContext` (`TextEditing`, `ReadView`, `Modal`, `WorldHud`), overlaid per-OS and then by the user's `keybinds.json`. Two entry points: `Resolve(e, context)` returns the `CommandId?` a key event means in that context (used by `UITextBox`/`UILabel`), and `Matches(e, commandId)` tests one command (used by `WorldScreen` hotkeys and `WorldScreen.TabCommands` for the HUD tabs). `KeybindCatalog` drives the Options -> Keybinds rebinding UI. `Modal` is deliberately unpopulated -- modal navigation keys (Escape/arrows/Enter) stay literal in the modal itself.
- **`AbilityRequirements`** -- Formats a castable's learn/use requirements for the castable popup's Details tab.

### World State & Models
- **`WorldState`** (`Collections/`) -- Static class. Entity tracking, sorted rendering, active effects, all ViewModel state. Access via `WorldState.Inventory`, `WorldState.Attributes`, etc.
- **`WorldEntity`** (`Models/`) -- Full entity data bag: position, direction, appearance, animation state, emotes.
- **Other models:** `Animation`, `EntityRemovalAnimation`, `WorldFrameState`, `SlotDragPayload`, `PathfindingState`, `TileClickTracker`, `Projectile`, `MailEntry`, `LegendMarkEntry`, `WorldListEntry`.

### ViewModel (`Brigid/ViewModel/`)
Authoritative state objects exposed as static properties on WorldState, updated by server packets:
- **`PlayerAttributes`** -- Stats, HP/MP, experience.
- **`Inventory`** -- Items and gold.
- **`SkillBook`/`SpellBook`** -- Skills/spells with cooldown timers.
- **`Equipment`** -- Equipped items.
- **`Chat`** -- Chat and orange bar messages.
- **`Exchange`** -- Trade state.
- **`Board`** -- Bulletin board / mail state.
- **`GroupState`/`GroupInvite`** -- Party/group membership.
- **`NpcInteraction`** -- Dialog/menu state.
- **`UserOptions`** -- Server-sent user option flags.
- **`WorldList`** -- Online players list.

### Entry Point
- **`ChaosGame : Game`** -- 640x480 virtual resolution MonoGame window. Owns ConnectionManager, shared renderers (Aisling/Creature/Effect/Item), SoundSystem, InputDispatcher, ScreenManager. Global entity event wiring at construction. WorldState, ClientSettings, and InputBuffer are static classes (not owned by ChaosGame).
- **`InputBuffer`** (static) -- Process-global input buffer driven by a single `SDL_AddEventWatch` callback. Unified event stream for keyboard, text, mouse button, and mouse wheel events in true OS post order (chronological `Events` buffer), with live cursor position refreshed each frame from `SDL_GetMouseState`. Query API: `WasKeyPressed()`, `IsKeyHeld()`, `TextInput`, `MouseX`/`MouseY`, plus the chronological `Events` stream (mouse buttons are event-only — no polled held flags). Lifecycle: `Initialize()` / `Update(isActive)` / `Shutdown()`.

### Input Dispatch (`InputDispatcher`)
Per-frame processor that reads `InputBuffer` state and produces UI events. Key concepts:
- **Hit-testing:** deepest-child-first, highest-ZIndex-first, respects `IsPassThrough`/`IsHitTestVisible`.
- **Capture:** mouse-down captures the target; mouse-up routes `MouseUp` to the captured element and synthesizes `Click` only if the cursor is still inside it and no drag occurred.
- **Click vs MouseDown:** `OnMouseDown` fires on press (used by `WorldScreen` for right-click pathfinding — instant response); `OnClick` fires on release. `DoubleClick` is synthesized on the second release within 300ms on the same element.
- **Control stack:** popups push themselves via `InputDispatcher.Instance.PushControl(this)` — the topmost entry receives keyboard events in Phase 2 of dispatch. Explicit focus (textboxes) intercepts Phase 1. A push takes the explicit focus and gives it back on removal; `PushControl(panel, keepTextFocus: true)` leaves a focused text box its focus, for a small entry that only needs the keys a text box lets through (an open `DropDown` list, which takes Escape).
- **Modals:** `TopModal` is the topmost visible `IsModal` panel on the control stack. With `EnforceModals` (the lobby sets it; `Clear` resets it, so world popups do not block the mouse), a mouse event outside the top modal and the visible stack panels above it is consumed. Each button and wheel event is checked at its own position. A blocked release still ends a capture: the captured element gets its mouse-up, a drag ends with `OnDragCancel` on its source, and no click occurs.
- **Drag:** initiated when the mouse moves ≥4px from the mousedown position while an element is captured. `OnDragStart` lets the source populate a payload; `DragMove`/`DragDrop` bubble to the element under the cursor.

## Conventions

### Concurrency
- Use `Lock` with `EnterScope()` instead of the `lock` keyword -- e.g. `using var scope = SendLock.EnterScope();`. This is the new .NET 9+ lock primitive with better usage semantics and performance.

### Packet Dispatch
- Use array-indexed handler dispatch (not switch-case) for opcode routing, mirroring the server's opcode-routing convention
- Delegate arrays sized `byte.MaxValue + 1`, indexed by opcode byte, registered via `IndexHandlers()`
- **Adding a handler:** write `private void HandleXxx(IServerPacket p)` in `ConnectionManager` (cast to the concrete `Server.XxxPacket` from `DALib.Networking.Packets.Server`), register it in `IndexHandlers()` as `PacketHandlers[(byte)ServerOpcode.Xxx] = HandleXxx`, then raise an event on the manager for `WorldScreen` to subscribe to.

### UI Patterns
- UI panels derive from `PrefabPanel` (legacy prefab-art layouts), `CenteredModalPanel` (from-scratch/asset-free modals -- the direction new panels go), or `UIPanel` (manual layouts)
- Theme art: `TextButton`, `SelectableTab` and `CenteredModalPanel` draw the `ui_theme` pack's parts when `UIElement.IsThemed` is true, and keep their flat `DialogPalette` look for a role the pack does not supply. `UseTheme` is a `bool?`: null inherits from the parent, so one switch on a panel themes everything in it. A panel draws its own fill, border and `Background` in `UIPanel.DrawChrome`; a themed control overrides it and calls the base only when `UIElement.TryDrawThemePart` returns false. Draw theme art only through `TryDrawThemePart` and colours through `ThemeColor`, so the later per-control `ui_panels` layer has one place to go.
- `PrefabPanel` provides `CreateButton`/`CreateImage`/`CreateLabel`/`CreateTextBox`/`CreateProgressBar` to selectively create controls from prefab data. Panels explicitly create only the controls they need (no auto-populate).
- Popup panels use `Show()`/`Hide()` for visibility and are children of the WorldScreen Root panel
- HUD has two implementations behind `IWorldHud`: `WorldHudControl` (classic compact) and `LargeWorldHudControl` (expanded)
- HUD tab panels share the center-bottom area via `ShowTab(HudTab)` -- only one visible at a time
- World controls organized into subdirectories: `Hud/`, `Hud/Panel/`, `Hud/Panel/Slots/`, `Popups/`, `Popups/Boards/`, `Popups/Dialog/`, `Popups/Exchange/`, `Popups/Options/`, `Popups/Profile/`, `Popups/WorldList/`, `ViewPort/`
- Hotkeys (all *defaults* -- every one resolves through `Keybinds` in `KeybindContext.WorldHud` and is rebindable in Options -> Keybinds; see `Keybinds.Defaults` for the authoritative table):
  - HUD tabs: A=Inventory, S=Skills, D=Spells, F=Chat, G=Stats, H=Tools; the Shift variant selects the alternate panel (Shift+A expands Inventory, Shift+F=MessageHistory, Shift+G=ExtendedStats). Base and alt are independent bindings.
  - Toggles: Q=PauseMenu, W=Boards, E=WorldList, R=SocialStatus, T=TownMap, Y=Group, B=PickupItem, J=GroupHighlight, Tab=TabMap (PageUp/PageDown zoom), `/`=SwapHudLayout, backtick=UnequipWeapon/Shield
  - Function keys: F1=Help/Merchant (also while the NPC window is the top popup), F3=Macros, F4=Settings, F5=Refresh, F7=BoardList, F8=KeybindMenu, F9=Ignore, F10=Friends
  - Other: Space=Assail, Enter=ChatFocus, Alt+Enter=CycleWindowSize, Shift+1=Shout, Shift+'=Whisper
- Textbox editing keys: Ctrl+A=select all, Ctrl+C/X/V=copy/cut/paste, Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y=undo/redo, Home/End (or Alt+Left/Right)=line start/end; Enter inserts a newline in multiline boxes. These resolve through the keybind registry (`Keybinds`, `KeybindContext.TextEditing`) so they honour rebindings and the per-OS primary modifier (Cmd on macOS); the editor family is rebindable in Options → Keybinds. Pure caret nav (arrows/Home/End/word nav) and text mutation stay literal in `UITextBox.OnKeyDown`. Chat input: Up/Down cycles sent-message history (draft preserved).
- Grid panels use `PanelBase` -> `PanelSlot` with slot number overlays and cooldown rendering
- Server-driven UI: many panels (exchange, dialog, equipment, profile) are populated by server packets, not client state
- Emote hotkeys: Ctrl+1-0/- (BodyAnimation 9-19), Ctrl+Alt+1-0/- (23-33), Alt+1-0/- (34-44)
- Slot hotkeys: 1-9, 0, -, = -> UseItem/UseSkill/UseSpell depending on active panel

### Architecture Patterns
- **Screens own controls:** WorldScreen creates and manages all world UI controls as children of its Root UIPanel
- **WorldScreen partial classes:** Split by concern (Draw, Update, Input, ServerHandlers, Wiring, Map) for maintainability
- **Events bridge network to UI:** ConnectionManager fires events -> WorldScreen subscribes -> creates/updates/shows controls
- **ViewModel state:** WorldState is a static class exposing ViewModel objects (Inventory, SkillBook, etc.) updated by server packets. Controls access state directly via `WorldState.Xxx` -- no constructor injection needed.
- **Cache-on-demand:** All renderers cache textures lazily and clear on map change
- **Data bag entities:** WorldEntity holds all state; AnimationSystem provides pure functions
- **Separation:** Rendering layer has no dependency on Networking; coordination happens in Client project
- **Global entity wiring:** ChaosGame wires entity tracking events at construction (before WorldScreen exists)
- **Pathfinding:** Right-click A* to a ground tile (a single right-click on a creature or aisling does not move the player); double right-click on a creature or aisling follows it with auto-assail; arrow/spacebar cancels
- **Casting flow:** CastingSystem coordinates targeting -> UseSpellOnTarget and chant progress

### Text Sizing & Passes
Durable rules; violating one produces a silent visual defect rather than a failure.
- **`FontEngine.RENDER_SIZE` (15) is a ceiling, not the default.** The size ordinary UI text draws at is `FontEngine.UiSize` -- the largest size at or below `RENDER_SIZE` whose glyph *ink* fits the 12px `TextRenderer.CHAR_HEIGHT` cell every Dark Ages prefab text rect is built on. It is **per-face** (at 15px Noto and Iosevka ink 12px, Anonymous Pro inks 13, Comic Shanns 16) and is a pure property of the face, cached on it. Passing `null`/omitting a size means `UiSize`; only pass an explicit size when the caller genuinely owns its own line grid (markdown headers/code).
- **Measure at the size and spacing you draw at.** Measuring at the default while drawing at an explicit size puts carets, selection rects and hit-tests out of step with the glyphs. Controls holding a `TextElement` measure through `TextElement.Measure`, and hit-test a click through `TextElement.IndexAtX`, rather than calling `TextRenderer` directly. Never add up single-glyph widths across a line: a lone glyph carries no tracking to the next glyph, and a rounded glyph width loses up to 0.5 px off 1x (`FindLineBreak` adds unrounded `MeasureWidthExact` widths plus the tracking). Each control keeps its `size:`/`characterSpacing:` pair in exactly one private `DrawRun` helper -- the pair being repeated per call site is what caused an entire family of these bugs.
- **Negative tracking vs natural advances.** `FontEngine.DEFAULT_TRACKING` is -1px globally. The shipped faces carry no sidebearing slack, so that default pulls glyph ink into its neighbour; text that autofits to fill a width cancels it (`ChatTextStyle.Spacing = -DEFAULT_TRACKING`) and sizes against the faces' natural advances instead.
- **All text draws in the native pass.** Text rasterized into the 640x480 virtual render target is point-upscaled with the world and renders blurry beside crisp UI text. Draw from `IScreen.DrawNative` (or `ChaosGame`'s native layer). A `Debug.Assert` in `FontEngine.DrawLineCore` catches violations; its predicate is the pure, unit-tested `IsUpscaledTextDraw`.
- **`FontEngine.Generation` bumps on face change *and* layout-scale change.** Because `MeasureWidth` is layout-scale dependent (it measures at native pixel size and divides back into virtual space), a window resize can move measured widths and wrap points without the nominal size changing. Anything caching a measurement, a wrap, or a derived layout must key on `Generation`, not just on the resolved size. A control that fits or wraps its text again while it is visible keeps a `FontGenerationStamp` (`Controls/Components`): it checks `Changed` in `Update` and calls `Mark` when it lays the text out.
- **The chat display owns chat text sizing.** `ChatPanel` owns the width (it comes from game data) and therefore the derived size; `ChatInputControl` asks it via `EnsureTextSize()` so the input cannot render at a different size from the lines above it. `ChatTextStyle` is deliberately stateless -- both HUD implementations are alive at once, so shared mutable statics there mean last-writer-wins.

### Other
- Case-insensitive string operations: `StartsWithI`, `ContainsI`, `EqualsI`, `ReplaceI`
- Thread-safe cache access via `RepositoryBase.GetOrCreate<T>` (per-instance Lock)
- `RepositoryBase`-cached `Get` methods return null for an absent asset through `GetOrCreateOrNull`; a catch is only for a corrupt asset
- UI controls use `NeverRemove` cache priority; other assets use sliding expiration
- Disposable cached objects are disposed via post-eviction callbacks

## Review Policy

Notable refactors or changes must have at minimum:
1. **Bug/regression review** -- A team member reviews the changes for correctness, edge cases, and regressions.
2. **Architecture/design review** -- A separate team member reviews for consistency with the current architecture, adherence to established patterns, and reasonable optimizations.

### Plan Workflow

When writing any implementation plan, each plan must include:
- **Phase-level review gates** -- After each phase/milestone, include a review step that performs both bug/regression review and architecture/design review of the changes made in that phase before proceeding to the next.
- **Final review** -- After full implementation is complete, include a comprehensive review step covering the entire changeset for correctness, regressions, architectural consistency, and adherence to established patterns.
- **Execution** -- Once a plan is approved, work through its phases in order, completing both reviews at each gate before advancing.

## Guardrails

- Do not introduce interactive prompts in scripts or commands
- Do not add commentary inside code solely to explain actions
- Avoid exception swallowing -- use guard checks (`TryGetValue`, bounds checks, null checks) instead of try-catch for control flow. Prefer `archive.TryGetValue` + `FromEntry` over `FromArchive` wrapped in try-catch, `lookup.Palettes.TryGetValue` over `lookup.GetPaletteForId` in try-catch, etc.
- Every implementation plan must include review gates after each phase and a final review after full implementation. Do not proceed to the next phase without completing both bug/regression and architecture/design review of the current phase.

## Control File Reference

Control files (`.txt` + `.spf`/`.epf`) define UI panel layouts. Loaded via `DataContext.UserControls.Get("_name")` -> `ControlPrefabSet`. Full catalog of all control files, their image references, consuming classes, format specification, and ControlType enum is in `controlFileList.txt` at solution root.

### Pattern: Building a Panel from a ControlPrefabSet
```csharp
// Extend PrefabPanel -- constructor handles anchor dimensions, centering, and background
public sealed class MyPanel : PrefabPanel
{
    public UIButton? OkButton { get; }
    public UIButton? CancelButton { get; }

    public MyPanel(GraphicsDevice device) : base(device, "_name")
    {
        // Create controls by name from the prefab definition
        OkButton = CreateButton("OK");
        CancelButton = CreateButton("Cancel");
        var title = CreateLabel("Title", TextAlignment.Center);
        var icon = CreateImage("Icon");
        var inputRect = GetRect("InputArea");  // rect-only lookup, no child created
    }
}
```

## Isometric Rendering Reference

Tile dimensions: 56x27 pixels, half-tile: 28x14 (from `DALib.Definitions.CONSTANTS`).

```
Tile -> Pixel (from Graphics.RenderMap):
  initialDrawX = (mapHeight - 1) * 28
  For each tile (x, y):
    pixelX = initialDrawX + x * 28    (initialDrawX decrements by 28 each y row)
    pixelY = initialDrawY + x * 14    (initialDrawY increments by 14 each y row)

Foreground tile positioning:
  lfgDrawX = same as bgDrawX
  lfgDrawY = bgDrawY + (x+1) * 14 - image.Height + 14  (bottom-aligned)
  Only render if tileIndex.IsRenderedTileIndex() -> (index > 10012) || ((index % 10000) > 12)

Draw order (painter's algorithm -- diagonal stripe, see WorldScreen.Draw.cs):
  1. Background tiles (floor) -- y-major, x-minor order
  2. Tile cursor highlight
  3. Foreground tiles + Entities + Effects -- diagonal stripe (depth = x+y ascending), X ascending within stripe; ground effects in stripe, entity effects after entity
  4. Silhouettes -- blocked-entity outlines behind foreground
  5. DarknessRenderer -- light/darkness overlay (if MapFlags has Darkness)
  6. WeatherRenderer -- snow/rain overlay (low nibble 1/2 of MapFlags)
  7. Viewport overlays (health bars, chat bubbles, chant text, etc.)
  8. Debug renderer (draw counts, gridlines, toggled via debug flags)
  9. Tab map overlay -- on top of world, under HUD (Tab key toggle)
  10. UI overlay (Root panel) -- popups, HUD; separate SpriteBatch pass, no camera transform
  11. Drag icon -- always topmost
```

## DALib Key Types

- **`MapFile`** -- `Tiles[x,y]` returns `MapTile` with `.Background`, `.LeftForeground`, `.RightForeground`
- **`Tileset`** -- `Collection<Tile>`, indexed by background tile ID
- **`Tile`** -- 56x27 palettized pixel data
- **`HpfFile`** -- Foreground tile, 28px wide, variable height
- **`Palette`** -- 256 SKColors. `Dye(colorTableEntry)` returns new palette with dye colors at index 98+.
- **`PaletteLookup`** -- Maps tile IDs to palettes via PaletteTable. Khan archives use `KhanPalOverrideType.Male`/`.Female`.
- **`ColorTable`** -- Dye color table from `.tbl` files. `ColorTableEntry` has `Colors[]` (6 SKColors for palette dye slots).
- **`EpfFile/MpfFile/EfaFile`** -- Sprite/animation formats with frame collections
- **`Palettized<T>`** -- Generic wrapper: `.Entity` + `.Palette`, implements IDisposable
- **`DataArchive`** -- DAT file container. `archive["filename"]` or `archive.TryGetValue(name, out entry)`
