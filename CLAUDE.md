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

No test projects exist currently.

## Solution Structure

```
Brigid.slnx (.NET 10.0, C# 14)
├── Brigid               — MonoGame Game class, screens, UI controls, systems, entry point
├── Brigid.Data           — Asset repositories, DALib integration, archive loading, caching
├── Brigid.Rendering      — Texture conversion, sprite renderers, map rendering, text, camera
└── Brigid.Networking     — TCP client, crypto, packet framing, connection state machine
```

DALib (Dark Ages file format support) is consumed as an external NuGet package, not a solution project.

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

## Key Dependencies

| Package                                    | Purpose                                                                   |
|--------------------------------------------|---------------------------------------------------------------------------|
| DALib                                      | Dark Ages file format support, SkiaSharp rendering                        |
| MonoGame.Framework.DesktopGL 3.8.4.1       | Cross-platform graphics/windowing                                         |
| Chaos.Networking 1.11.0-preview            | Complete protocol library: packet converters, crypto, opcodes, args types |
| Chaos.Common 1.11.0-preview                | Shared extension methods (NuGet)                                          |
| Chaos.DarkAges 1.11.0-preview              | Dark Ages protocol types (NuGet)                                          |
| Chaos.Geometry 1.11.0-preview              | Geometry types -- rectangles, points (NuGet)                              |
| Chaos.Pathfinding 1.11.0-preview           | A* pathfinding (NuGet)                                                    |
| Microsoft.Extensions.Caching.Memory 10.0.5 | MemoryCache infrastructure                                                |
| TextCopy 6.2.1                             | Cross-platform clipboard access (used by `Utilities/Clipboard`)           |

The `Chaos.*` preview packages (`Chaos.Networking`, `Chaos.Common`, `Chaos.DarkAges`, `Chaos.Geometry`, `Chaos.Pathfinding`) are inherited from the upstream project and are slated for replacement with Hybrasyl equivalents.

## Build Configuration

Centralized in `Directory.Build.props`: C# 14, net10.0, nullable enabled, implicit usings, TieredPGO + TieredCompilation (+ QuickJit) enabled, WarningLevel 4, EnforceCodeStyleInBuild. Package versions managed centrally in `Directory.Packages.props`. Versioning via Nerdbank.GitVersioning.

## Architecture

### Data Layer (`Brigid.Data`)
- **`DataContext`** -- Static singleton exposing all repositories via `Initialize()`.
- **`DatArchives`** -- Static holder for 22 game data archives loaded at startup via memory-mapped files.
- **`RepositoryBase`** -- Abstract base with MemoryCache (15-min sliding expiration). Uses `GetOrCreate<T>(key, factory)`.
- **11 repositories** (exposed on `DataContext` by these accessor names): AislingDrawData, CreatureSprites, Effects, Fonts, LightMasks, LocalPlayerSettings, MapsFiles, MetaFiles, PanelSprites, Tiles, UserControls (the UI-component repository).
- **`ControlPrefab`/`ControlPrefabSet`** -- Wraps DALib Control definitions + pre-rendered SKImage arrays. First control (Anchor) defines panel bounds.
- Control file catalog in `controlFileList.txt` at solution root.
- **`AssetPackRegistry`** (`AssetPacks/`) -- Static registry. On `Initialize()`, scans the per-user erisco assets directory (`AppPaths.AssetsDir`) for `*.datf` files (ZIP archives with modern asset overrides), reads each one's `_manifest.json`, validates `schema_version`, and registers a single pack per `content_type`. On first launch it performs a one-time best-effort migration of legacy `{DataContext.DataPath}/hybrasyl-data/` packs (the `LEGACY_PACK_SUBFOLDER`) into the assets directory. Lookups return null when no pack is registered; the renderer/audio layer falls through to legacy. See `asset-pack-format.md` in the document repo for the artist-facing format spec. Eleven content types are supported, each with a typed accessor:
  - `ability_icons` -> `IconPack` -> `GetIconPack()`
  - `item_icons` -> `ItemPack` -> `GetItemPack()`
  - `nation_badges` -> `NationBadgePack` -> `GetNationBadgePack()`
  - `npc_portraits` -> `NpcPortraitPack` -> `GetNpcPortraitPack()`
  - `static_tiles` -> `StaticTilePack` -> `GetStaticTilePack()`
  - `legend_mark_icons` -> `LegendMarkIconPack` -> `GetLegendMarkIconPack()`
  - `ui_sprite_overrides` -> `UiSpriteOverridePack` -> `GetUiSpriteOverridePack()`
  - `creature_sprites` -> `CreaturePack` -> `GetCreaturePack()` (per-creature auto-trim)
  - `music` -> `MusicPack` -> `GetMusicPack()` (streamed; entries `music_{id}.{ext}`)
  - `sound_effects` -> `SfxPack` -> `GetSfxPack()` (decoded; entries `sfx_{id}.{ext}`)
  - `world_maps` -> `WorldMapPack` -> `GetWorldMapPack()` (overworld field backgrounds; entries `{fieldName}.png`)
- **`AudioPack`** (`AssetPacks/`) -- Shared base for `MusicPack`/`SfxPack`. Entries named `{prefix}_{id}.{ext}` at the archive root; builds an extension-agnostic `id -> entry` map at construction and returns raw bytes via `TryGetAudioBytes` (no decode — the audio layer hands bytes to SDL_mixer). `MusicPack` overrides loose `{DataPath}/music/{id}.mus`; `SfxPack` overrides `legend.dat` `{id}.mp3`. A present id = replace, a new id = add.
- **`IconPack`** (`AssetPacks/`) -- Wraps a ZipArchive of `{prefix}_{id:D4}.png` entries. `TryGetIconImage(prefix, spriteId, out SKImage?)` case-insensitive lookup; decode failures treated as "not present" so renderer falls back cleanly to legacy.

### Rendering Layer (`Brigid.Rendering`)
- **`TextureConverter`** -- DALib `SKImage` -> MonoGame `Texture2D` (RGBA8888 premul). Entry points: `ConvertImage<T>()`, `ToTexture2D()`.
- **`ImageUtil`** -- Static class of stateless CPU pixel-manipulation primitives: tint/blend variants (`Blend50`, `ApplyHoverTint`, `ApplyGroundTint`, `BuildGroupTinted`, `BuildHitTinted`, `BuildHoverTinted`, `BuildGroundTinted`, `BuildCooldownTintedCached`), checker pattern, vertical alpha gradient, filled border, rectangle fill, projected-quadrants raster, chat-bubble body + tail, 2x2 box downsampler (`SKColor[]`), comb-dissolve kernel. Naming convention: `Build*` returns a new `Texture2D` (or `CachedTexture2D`); `Apply*`/`Fill*`/`Draw*` mutate a `Color[]` in place. No global state -- all device-requiring helpers take an explicit `GraphicsDevice`.
- **`PixelBufferScope`** -- `ref struct` RAII wrapper around `ArrayPool<Color>.Shared.Rent` + `Texture2D.GetData`/`SetData`. Use this instead of hand-rolling rent/get/set/return. Two constructors: `(Texture2D source)` reads the texture into a fresh buffer; `(int width, int height)` rents an **uninitialized** buffer (callers must `Array.Clear(scope.Pixels, 0, scope.Count)` if they depend on zeros). Exposes `Pixels`, `Count`, `Width`, `Height`, `AsSpan()` for bounds-safe iteration; `CommitTo(Texture2D)` uploads pixels back via `SetData`. This is the *only* place outside tests that should call `ArrayPool<Color>.Shared.Rent`.
- **`Camera`** -- Isometric camera: `WorldToScreen`, `ScreenToWorld`, `TileToWorld`, `WorldToTile`, `GetVisibleTileBounds()`.
- **`MapRenderer`** -- Background + foreground tile rendering. `DrawBackground()`, `DrawForegroundTile()`, `PreloadMapTiles()`.
- **`TextRenderer`** -- SkiaSharp text rendering: `RenderText()`, `RenderWrappedText()`, `MeasureWidth()`, `WrapText()`.
- **`UiRenderer`** -- UI panel rendering utilities.
- **`DarknessRenderer`** -- Light/darkness overlay. Consumes light sources from `LightingSystem`.
- **`TabMapRenderer`** -- Mini-map rendering. Also consumes from `LightingSystem` for fog-of-war.
- **`WeatherRenderer`** -- Snow/rain overlay driven by the low nibble of `MapFlags` (1=Snow, 2=Rain, 3=Darkness handled by `DarknessRenderer`).
- **`SilhouetteRenderer`** -- Silhouette effect for blocked entities.
- **`PaletteCyclingManager`** -- Animated palette shimmer effects.
- **`FontAtlas`** -- Font glyph atlas management.
- **`CreatureRenderer`/`AislingRenderer`/`EffectRenderer`/`ItemRenderer`** -- Per-frame texture caches. `Clear()` on map change.
- **`LegendColors`** -- Named color constants for UI text. Initialized at startup.
- **`LightSource`** -- Light source model for darkness system.
- **`RenderHelper`** -- Shared rendering utility methods.
- **`CacheExtensions`** -- Extension methods for consistent dictionary cache management across renderers.
- **`TextureAtlas`/`AtlasHelper`/`CachedTexture2D`** -- Grid/Shelf packing for performance optimization.
- **`SpriteAnimation`/`SpriteFrame`** -- Frame array with `GetFrame(index)`, timing, additive blending.
- **`EntityHitBox`** -- Hit testing geometry for clickable entities.
- **`IconTexture`** -- Record struct `(Texture2D Texture, int OffsetX, int OffsetY)` with `Legacy(t)`/`Modern(t)` factories and a `Draw(sb, pos, tint?)` helper. Wraps ability-icon returns from `UiRenderer.GetSkillIcon`/`GetSpellIcon` so modern 32×32 icons from `.datf` packs (offset -1/-1) can coexist with legacy 31×31 EPF icons (offset 0/0) in the same pipeline. Offset propagates through `UIImage.TextureOffset` and `UIButton.TextureOffset`.
- **Asset pipeline:** `DatArchives -> Repository -> Palettized<T> -> DALib Graphics.RenderXxx() -> SKImage -> TextureConverter.ToTexture2D() -> Texture2D -> SpriteBatch.Draw()`

### Networking Layer (`Brigid.Networking`)
- **`GameClient`** -- Low-level TCP: crypto, packet framing (0xAA + 2-byte BE length), sequence tracking, `InboundQueue` via `DrainPackets()`. Auto-responds to HeartBeat/SynchronizeTicks.
- **`ConnectionManager`** -- State machine (Disconnected->Connecting->Lobby->Login->World), array-indexed handler dispatch (60+ handlers), 48+ events. Full lobby/login/world-entry flows. Player action methods, communication, NPC/dialog, requests.
- **`ServerTableData`** -- Zlib-compressed server list parser.
- Protocol types come from the `Chaos.Networking` / `Chaos.DarkAges` NuGet packages (preview 1.11.0) — serialization, opcodes, and args types are all defined there rather than in this project.

### Client Project (`Brigid`) Internal Organization

```
Brigid/
├── ChaosGame.cs              — MonoGame Game class, entry point
├── Program.cs                — Process entry point (Main)
├── GlobalSettings.cs         — Static config (ClientVersion, DataPath, LobbyHost/Port)
├── InputBuffer.cs            — Event-driven input capture and buffering
├── InputDispatcher.cs        — UI event dispatch: hit-test, bubble, drag, click synthesis, control stack
├── Sdl.cs                    — Centralized SDL2 P/Invoke declarations (keyboard, text, mouse button, mouse wheel event constants consumed by InputBuffer; audio subsystem init/quit, SDL_GetError, SDL_RWFromConstMem consumed by SoundSystem)
├── SdlMixer.cs               — SDL2_mixer P/Invoke wrapper (Mix_* functions and constants, consumed by SoundSystem)
├── Collections/              — WorldState, CircularBuffer
├── Models/                   — WorldEntity, Animation, EntityRemovalAnimation, WorldFrameState, SlotDragPayload, PathfindingState, etc.
├── ViewModel/                — Authoritative state classes owned by WorldState
├── Systems/                  — AnimationSystem, CastingSystem, SoundSystem, Pathfinder, LightingSystem, LatencyMonitor, ClientSettings, MachineIdentity
├── Screens/                  — IScreen, ScreenManager, LobbyLoginScreen, WorldScreen (7 partial files)
├── Rendering/                — EntityOverlayManager, WorldDebugRenderer
├── Controls/                 — Full UI control hierarchy (see UI Control System below)
├── Definitions/              — Delegates, Enums, DoorTable, InputEvents, TextColors
├── Extensions/               — DirectionExtensions, RectangleExtensions, UIElementExtensions
└── Utilities/                — Clipboard, DialogFrame, SlideAnimator
```

### Screen System
- **`IScreen`/`ScreenManager`** -- Stack-based screen management.
- **`LauncherScreen`** -- Zero-config startup screen (server select + asset-folder picker + Connect), shown on every launch unless env vars (`DA_HOST`/`DA_ASSET_PATH`) fully specify a valid setup. **The one deliberately non-`UIElement` screen:** it renders before `DataContext.Initialize` (the asset path is what it exists to obtain), so it cannot use prefab panels/atlas fonts -- it draws with a 1x1 white texture + `SystemFontText` (OS typeface) and reads `InputBuffer` directly (`Root => null`, not in the `InputDispatcher` tree). `Connect` persists `LauncherConfig` then calls `ChaosGame.FinishAssetInitialization()` (the deferred asset-load seam) and switches to the lobby. If it grows past the add-server form, factor its primitive helpers into a shared asset-free UI helper rather than expanding the monolith.
- **`LobbyLoginScreen`** -- Full login flow: lobby connect, server select, login, character creation, transition to world.
- **`WorldScreen`** -- Main game screen, split into 7 partial class files:
  - `WorldScreen.cs` -- Base class, fields, construction
  - `WorldScreen.Draw.cs` -- Render logic (diagonal stripe entity interleaving, overlays)
  - `WorldScreen.Update.cs` -- Game logic update
  - `WorldScreen.InputHandlers.cs` -- Keyboard/mouse input (movement, hotkeys, pathfinding, click-to-interact)
  - `WorldScreen.ServerHandlers.cs` -- Network packet handler subscriptions
  - `WorldScreen.Wiring.cs` -- Event subscription setup
  - `WorldScreen.Map.cs` -- Map management

### UI Control System

**Component Primitives (`Controls/Components/`):** UIElement, UIPanel, UIButton, UITextBox, UIImage, UILabel, UIProgressBar, TextElement, PrefabPanel.

**Login Flow (`Controls/LobbyLogin/`):** LobbyLoginControl, LoginControl, ServerSelectControl, CharacterCreationControl, LoginNoticeControl, PasswordChangeControl, LogoImage.

**Generic Controls (`Controls/Generic/`):** OkPopupMessageControl, TextPopupControl, ScrollBarControl, SliderControl, DebugOverlay.

**World HUD (`Controls/World/Hud/`):** IWorldHud interface, WorldHudControl (classic compact HUD), LargeWorldHudControl (expanded HUD), OrangeBarControl, ChatInputControl, EffectBarControl/EffectSlotControl, MailButton (unread-mail pulse indicator driven by `PlayerAttributes.HasUnreadMail`).

**HUD Panels (`Hud/Panel/`):** PanelBase, ExpandablePanel, InventoryPanel, SkillBookPanel, SpellBookPanel, ToolsPanel, ChatPanel, StatsPanel, ExtendedStatsPanel, SystemMessagePanel, StatButton. Slots: PanelSlot, AbilitySlotBase, SkillSlot, SpellSlot.

**Self Profile (`Popups/Profile/`):** SelfProfileTabControl with Equipment/Legend/AbilityMetadata/Events/Family/Blank tabs, SelfProfileTextEditorControl, AbilityMetadataDetailsControl/AbilityMetadataEntryControl, EventMetadataDetailsControl/EventMetadataEntryControl, LegendMarkControl. **Other Profile:** OtherProfileTabControl (Equipment via _nui_eqa + Legend tabs), OtherProfileEquipmentTab. (Legend tab reuses `SelfProfileLegendTab`.)

**Options (`Popups/Options/`):** MainOptionsControl, MacrosListControl, SettingsControl, FriendsListControl.

**Popups (`Popups/`):** AislingContextMenu, GoldAmountControl, ItemAmountControl, ChantEditControl, GroupRecruitPanel, GroupTab/GroupTabControl, HotkeyHelpControl, ItemTooltipControl, NotepadControl, SocialStatusControl, TownMapControl. Subdirectories: `Boards/` (BoardListControl, ArticleListControl/ArticleReadControl/ArticleSendControl, MailListControl/MailReadControl/MailSendControl), `Dialog/` (NpcSessionControl, FramedDialogPanelBase, DialogAlphaGradient, MenuShopPanel, DialogTextEntryPanel, DialogProtectedTextEntryPanel, MenuTextEntryPanel, DialogOptionPanel, MenuListPanel), `Exchange/` (ExchangeControl/ExchangeItemControl), `WorldList/` (WorldListControl/WorldListEntryControl).

**Viewport Overlays (`ViewPort/`):** ChatBubble, HealthBar, LoadingBar/MapLoadingBar, WorldMap/WorldMapNode, ChantText, GroupBox, SystemMessagePaneControl, PersistentMessageControl.

### Game Systems (`Brigid/Systems/`)
- **`AnimationSystem`** -- Pure methods for walk/body/creature animations, frame calculation, walk offset lerp.
- **`CastingSystem`** -- Spell targeting + chant management.
- **`SoundSystem`** -- SDL2_mixer-based audio. SFX decoded to PCM once via `Mix_LoadWAV_RW` and cached as `Mix_Chunk` pointers; playback uses the mixer's channel pool with per-channel volume. Music streams via `Mix_LoadMUS`/`Mix_LoadMUS_RW` with `Mix_FadeOutMusic`/`Mix_FadeInMusic` for map transitions. Same-sound overlap ducks prior instances by -3 dB (equal-power) instead of voice-stealing. Both paths are pack-first: `LoadChunk` prefers `AssetPackRegistry.GetSfxPack()` over `legend.dat`, and `StartMusic` prefers `GetMusicPack()` (streamed from a pinned byte[] via `Mix_LoadMUS_RW`) over the loose `{DataPath}/music/{id}.mus`. The pinned music buffer is released in lockstep with the music handle by `FreeCurrentMusic`. Ambient loop API: `StartAmbientLoop(id)`/`StopAmbientLoop()` run one looping bed (rain, driven by `WorldScreen.UpdateWeatherAmbience` on the weather nibble) on a channel invisible to one-shot voice stealing; starts blocked only by a saturated channel pool retry from `Update()`, and the stop fade hands the channel to the one-shot tracking so the chunk stays eviction-safe. Reserved ambient sound ids come only from `.datf` sfx packs (rain = 10000, `WorldScreen.RAIN_AMBIENT_SOUND_ID`); no pack entry means a silent no-op.
- **`Pathfinder`** -- A* pathfinding algorithm.
- **`LightingSystem`** -- Owns the per-frame light source buffer. Walks world entities, reads `LanternSize`, and gathers into a span consumed read-only by `DarknessRenderer` and `TabMapRenderer` (neither stores its own copy). Caches Euclidean circle offset arrays (radius 3/5) and exposes `BaselineVisibilityOffsets` for the unconditional player-tile reveal on darkness maps.
- **`LatencyMonitor`** -- Static class. Passive sink for application-layer round-trip-time samples. Producers call `Update(long? rttMs)` (null clears the reading); the HUD subscribes to `LatencyChanged` and reads `LatencyMs`. May fire on any thread — subscribers must not block, and any non-trivial work should be marshalled to the game-loop thread by the consumer. Producer: `ChaosGame` runs a 2s polling task while in World state that calls `GameClient.TryGetTcpSmoothedRttMs` — real kernel-measured smoothed RTT, no protocol changes. Cross-platform via `SIO_TCP_INFO` IOCTL (Windows), `getsockopt(IPPROTO_TCP, TCP_INFO)` (Linux), or `getsockopt(IPPROTO_TCP, TCP_CONNECTION_INFO)` (macOS). Clears via `Update(null)` on World exit.
- **`MachineIdentity`** -- Machine-specific identification for the client.
- **`ClientSettings`** -- Static class. Persistent user settings. Access via `ClientSettings.SoundVolume`, etc.

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
- **`InputBuffer`** (static) -- Process-global input buffer driven by a single `SDL_AddEventWatch` callback. Unified event stream for keyboard, text, mouse button, and mouse wheel events in true OS post order (chronological `Events` buffer), with live cursor position refreshed each frame from `SDL_GetMouseState`. Query API: `WasKeyPressed()`, `IsKeyHeld()`, `TextInput`, `MouseX`/`MouseY`, `IsLeftButtonHeld`/`IsRightButtonHeld`. Lifecycle: `Initialize()` / `Update(isActive)` / `Shutdown()`.

### Input Dispatch (`InputDispatcher`)
Per-frame processor that reads `InputBuffer` state and produces UI events. Key concepts:
- **Hit-testing:** deepest-child-first, highest-ZIndex-first, respects `IsPassThrough`/`IsHitTestVisible`.
- **Capture:** mouse-down captures the target; mouse-up routes `MouseUp` to the captured element and synthesizes `Click` only if the cursor is still inside it and no drag occurred.
- **Click vs MouseDown:** `OnMouseDown` fires on press (used by `WorldScreen` for right-click pathfinding — instant response); `OnClick` fires on release. `DoubleClick` is synthesized on the second release within 300ms on the same element.
- **Control stack:** popups push themselves via `InputDispatcher.Instance.PushControl(this)` — the topmost entry receives keyboard events in Phase 2 of dispatch. Explicit focus (textboxes) intercepts Phase 1.
- **Drag:** initiated when the mouse moves ≥4px from the mousedown position while an element is captured. `OnDragStart` lets the source populate a payload; `DragMove`/`DragDrop` bubble to the element under the cursor.

## Conventions

### Concurrency
- Use `Lock` with `EnterScope()` instead of the `lock` keyword -- e.g. `using var scope = SendLock.EnterScope();`. This is the new .NET 9+ lock primitive with better usage semantics and performance.

### Packet Dispatch
- Use array-indexed handler dispatch (not switch-case) for opcode routing, mirroring the server's opcode-routing convention (the array-indexed dispatch used by the `Chaos.Networking` library)
- Delegate arrays sized `byte.MaxValue + 1`, indexed by opcode byte, registered via `IndexHandlers()`
- **Adding a handler:** write `private void OnXxx(ref ServerPacket packet)` in `ConnectionManager`, register it in `IndexHandlers()` as `Handlers[(byte)ServerOpCode.Xxx] = OnXxx`, deserialize via the appropriate `Chaos.Networking` converter, then raise an event on the manager for `WorldScreen` to subscribe to.

### UI Patterns
- All UI panels derive from `PrefabPanel` (for prefab-based layouts) or `UIPanel` (for manual layouts)
- `PrefabPanel` provides `CreateButton`/`CreateImage`/`CreateLabel`/`CreateTextBox`/`CreateProgressBar` to selectively create controls from prefab data. Panels explicitly create only the controls they need (no auto-populate).
- Popup panels use `Show()`/`Hide()` for visibility and are children of the WorldScreen Root panel
- HUD has two implementations behind `IWorldHud`: `WorldHudControl` (classic compact) and `LargeWorldHudControl` (expanded)
- HUD tab panels share the center-bottom area via `ShowTab(HudTab)` -- only one visible at a time
- World controls organized into subdirectories: `Hud/`, `Hud/Panel/`, `Hud/Panel/Slots/`, `Popups/`, `Popups/Boards/`, `Popups/Dialog/`, `Popups/Exchange/`, `Popups/Options/`, `Popups/Profile/`, `Popups/WorldList/`, `ViewPort/`
- Hotkeys: A=Inventory, S=Skills, D=Spells, Shift+S/D=Alt panels, F=Chat, Shift+F=MessageHistory, G=Stats, Shift+G=ExtendedStats, H=Tools, F9=Ignore, Tab=TabMap, F1=Help, F3=Macros, F4=Settings, F5=Refresh, F7=Mail, F8=Group, F10=Friends
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
- **Pathfinding:** Right-click A* to tile/entity, entity following with auto-assail, arrow/spacebar cancels
- **Casting flow:** CastingSystem coordinates targeting -> UseSpellOnTarget and chant progress

### Other
- Case-insensitive string operations: `StartsWithI`, `ContainsI`, `EqualsI`, `ReplaceI`
- Thread-safe cache access via `RepositoryBase.GetOrCreate<T>` (per-instance Lock)
- Repository `Get` methods return null on failure (try-catch pattern)
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
