# Brigid

(A fork of [Sichii's][Chaos.Client])

A custom Dark Ages client written in C# (.NET 10) on top of MonoGame, [DALib](https://github.com/eriscorp/DALib), and
the [Chaos.Networking](https://github.com/Sichii/Chaos-Server) layer. Works with [Chaos-Server](https://github.com/Sichii/Chaos-Server),
[Hybrasyl Server][https://github.com/hybrasyl/server], and retail servers.

Targets Dark Ages client version **7.4.1** for feature parity.

Runs on **Windows, macOS, and Linux** — anywhere the .NET 10 SDK and MonoGame's DesktopGL backend are supported. Signed / notarized apps
are provided for multiple platforms.

## Contents

- [Status](#status)
- [Differences from the Retail Client](#differences-from-the-retail-client)
- [Architecture](#architecture)
    - [Project layout](#project-layout)
    - [World state](#world-state)
    - [Draw order](#draw-order)
- [UI System](#ui-system)
- [Input](#input)
    - [InputBuffer](#inputbuffer)
    - [InputDispatcher](#inputdispatcher)
    - [Where to put new input handling](#where-to-put-new-input-handling)
- [Renderers](#renderers)
    - [TextureConverter](#textureconverter-static)
    - [Camera](#camera)
    - [MapRenderer](#maprenderer)
    - [PaletteCyclingManager](#palettecyclingmanager)
    - [LightingSystem](#lightingsystem)
    - [DarknessRenderer](#darknessrenderer)
    - [TabMapRenderer](#tabmaprenderer)
    - [WeatherRenderer](#weatherrenderer)
    - [SilhouetteRenderer](#silhouetterenderer)
    - [TextRenderer + FontAtlas](#textrenderer--fontatlas)
    - [UiRenderer](#uirenderer)
    - [Per-entity renderers](#per-entity-renderers-creaturerenderer-aislingrenderer-effectrenderer-itemrenderer)
- [Build & Run](#build--run)
- [Configuration](#configuration)
- [Extending](#extending)
    - [Adding a UI panel](#adding-a-ui-panel)
    - [Adding a packet handler](#adding-a-packet-handler)
- [Related Repositories](#related-repositories)

## Status

This client implements the full lobby/login/world flow, rendering, HUD, inventory, skills, spells, chat, exchange,
boards/mail, groups, profile, dialogs, and most of the popup UI. It is close to the retail client's look and feel but
intentionally differs in several places (see below).

## Differences from the Retail Client

These are intentional, and are the first things a fork should know about:

1. **Khan 'b' bodies rendered behind everything for BlowKiss.** Retail uses the `m` khan bodies, which erases the
   BlowKiss heart effect. This client renders the `b` bodies behind the rest of the aisling so the heart effect
   survives.
2. **Event metadata availability respects your current circle.** Retail incorrectly marks events as unavailable once you
   hit master (circle 6), even when the entry lists circle 6 as acceptable. This client evaluates the acceptable-circle
   list correctly.
3. **gndattr.tbl tinting no longer breaks draw order.** On retail, standing on a tinted ground tile while occluded
   caused the character to pop in front of the foreground object. This client keeps the character behind the foreground
   and still applies the ground tint.
4. **Background tile animations from gndani.tbl are implemented.** Retail ignores these entirely. Animated background
   tiles now play.
5. **Overcoat/armor palette mappings with IDs >= 1000 work.** Retail falls back to the default palette for these IDs;
   this client honors the mapped palette.
6. **Tab map is a custom reimplementation.** The look is intentionally different from retail. If you want pixel-accurate
   retail behavior, you will need to replace `TabMapRenderer` and related controls.
7. **Tab map zoom is not rogue-locked.** Every class can zoom. If you want to gate this on class, put the check back in
   the input handler.
8. **Idle animations survive emotes on items with idle animations.** Retail stops or partially plays the item's idle
   animation when you emote. This client keeps them running.
9. **Inline color codes work everywhere and apply immediately.** Color codes are resolved at the renderer level, so the
   source string still contains the codes even though they're invisible. Every `TextElement` / `UILabel` has a
   `ColorCodesEnabled` toggle so you can turn them off per-control if you need to.
10. **Pants render under overcoats when the server allows it.** If the server's item definition says the overcoat
    permits pants, this client draws them. Retail does not.
11. **Album and Portrait systems are not implemented.** The Album tab in the self-profile is not wired up, and the
    portrait button does not actually take a portrait of your character. Both are straightforward to fill in if you need
    them.
12. **Alt+Enter cycles through window sizes.** The virtual canvas is 640×480; Alt+Enter steps the backbuffer through
    multiples of that — 1×, 2×, 3×, … — up to whatever the current monitor can fit, then wraps back to 1×.
13. **Swimming is unrestricted by default.** Retail gates swimming(walking on water tiles) behind the `Swimming` skill,
    or having the GM flag to ignore collision. This client ships with that gate **off** — any character walks/pathfinds
    onto water tiles freely. Set `GlobalSettings.RequireSwimmingSkill = true` to restore the retail behavior.
14. **Health bars, chants, and chat bubbles on creature sprites use a blended offset.** They sit halfway between a fixed
    baseline and the sprite's mean visible top instead of tracking frame heights — small sprites a little higher than
    retail, large sprites a little lower.
15. **Snow looks different and Rain actually works.** This client uses a different snow implementation, and unlike the
    original client, Rain actually works.

This is not an exhaustive list, but other differences are likely too minor to bother with.

## Architecture

### Project layout

| Project                     | Responsibility                                                                                                                                                                                                                                         |
|-----------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **DALib**                   | Dark Ages file formats and SkiaSharp rendering.                                                                                                                                                                                                        |
| **Chaos.Client.Data**       | Opens the `.dat` archives via memory-mapped files and exposes repositories for sprites, tiles, fonts, metafiles, UI prefabs, etc. Some repositories cache their entries with eviction policies appropriate to the asset type; others are pass-through. |
| **Chaos.Client.Rendering**  | Converts DALib's SkiaSharp output into MonoGame `Texture2D` and owns the map, camera, darkness, tab map, and per-entity renderers.                                                                                                                     |
| **Chaos.Client.Networking** | TCP, crypto, packet framing, and a state-machine `ConnectionManager` on top of the `Chaos.Networking` NuGet package. Packet handlers are registered into an opcode-indexed delegate array.                                                             |
| **Chaos.Client**            | MonoGame `Game`, screens, UI controls, game systems, and world state.                                                                                                                                                                                  |

Dependency flow:

```
DALib ──> Chaos.Client.Data ──────┐
DALib ──> Chaos.Client.Rendering ─┼─> Chaos.Client
         Chaos.Client.Networking ─┘
```

### World state

`WorldState` is a static class holding the ViewModel objects (`Attributes`, `Inventory`, `Equipment`, `SkillBook`,
`SpellBook`, `Chat`, `Exchange`, `Board`, `Group`, `GroupInvite`, `NpcInteraction`, `UserOptions`, `WorldList`). Server
packets write into these via `ConnectionManager` events wired in `WorldScreen.Wiring.cs`; controls read from them
directly, no constructor injection. Treat `WorldState` as the single source of truth for anything shown in the world
screen.

### Draw order

Each frame goes through three phases (see `WorldScreen.Draw.cs`):

**1. Off-screen pre-pass** (only when a map is loaded):

- Occluded-entity silhouettes are pre-rendered into a single viewport-sized render target.

**2. World pass** — scissored to the HUD viewport, camera transform applied:

- Background tiles + the tile cursor, in a single batched pass.
- Foreground tiles, entities, and effects interleaved in diagonal stripes by `x + y` depth. Within a stripe the order is
  **ground items → aislings → creatures → dying-creature dissolves → ground-targeted effects → entity-attached effects →
  foreground tiles**.
- Silhouette render target overlaid at reduced alpha.
- Darkness overlay (screen space, no camera transform). `LightingSystem.Gather` runs just before
  `DarknessRenderer.Update` so the overlay reads the current frame's light sources.
- Weather overlay (snow or rain, screen space). Drawn after darkness so flakes/drops are still visible on dark maps.
  Inactive on maps whose `MapFlags` low nibble is `0` or `3`.
- Blind overlay (full black, player redrawn on top) if the player is blinded. Drawn **before** the entity overlays so
  chat bubbles, name tags, chant text, and health bars all stay visible while blinded. Retail keeps chat bubbles and
  name tags visible the same way. It hides HP bars because on retail the HP bar is drawn with the entity sprite itself —
  if you want strict retail parity, split health bars out of `EntityOverlayManager.Draw` and render them alongside the
  entity body. Chant overlay retail behavior is not confirmed; worth verifying in-game.
- Entity overlays — chat bubbles, health bars, name tags, chant text, group box text — drawn after darkness, so the
  light level doesn't tint them, and after blind so they remain visible while blinded.
- Debug overlay if active.

**3. Screen pass** — no camera transform:

- Tab map overlay if visible (on top of the world, under the HUD).
- UI root panel (HUD + all popups).
- Drag icon at the cursor if something is being dragged.

## UI System

All UI primitives live in `Chaos.Client/Controls/Components/`. A catalog of the prefab control files shipped in the dat
archives and their consuming classes is in `controlFileList.txt` at the solution root.

| Component       | Purpose                                       | Notes                                                                                                                                                                                                                                                                                                                                                                  |
|-----------------|-----------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `UIElement`     | Abstract base for every UI primitive.         | Position, size, padding, `ZIndex`, `BackgroundColor`/`BorderColor`, per-frame `ClipRect` intersected with the parent. Input events are virtual methods (`OnClick`, `OnMouseDown`/`Up`/`Move`, `OnMouseEnter`/`Leave`, `OnMouseScroll`, `OnKey*`, `OnTextInput`, `OnDrag*`). `X`/`Y` are parent-local; `ScreenX`/`ScreenY` walk the chain. See subclassing notes below. |
| `UIPanel`       | Container with `Children`; disposal cascades. | `IsPassThrough` — children still hit-test, the panel itself never does (full-screen HUD overlays). `IsModal` — captures all input while visible; others still `Update`. `UsesControlStack` — participates in the `InputDispatcher` control stack (auto-managed for `PrefabPanel`). Draw order within a panel is by `ZIndex`, ties broken by insertion order.           |
| `PrefabPanel`   | Abstract `UIPanel` loading a DALib prefab.    | First entry is the "anchor" (sets `Width`, `Height`, position, background). Use `CreateButton`/`CreateImage`/`CreateLabel`/`CreateTextBox`/`CreateProgressBar` to instantiate named children — no autopopulate; returns `null` if the name is absent. `GetRect("name")` returns an anchor-relative rect without creating a child.                                      |
| `TextElement`   | Text-drawing helper. **Not a `UIElement`.**   | Holds the state a string needs to draw via `TextRenderer` (text, color, wrap cache, shadow, alignment, color-code toggle). No bounds, no GPU state — owning widgets draw it wherever they want. `Update`/`UpdateShadowed`/`UpdateWrapped` pick the mode and skip work when nothing changed. Wrap a `TextElement` instead of calling `TextRenderer` directly.           |
| `UIImage`       | Static texture.                               | Owns its `Texture` and disposes it.                                                                                                                                                                                                                                                                                                                                    |
| `UIButton`      | Clickable with up to 5 state textures.        | `NormalTexture`, `HoverTexture`, `PressedTexture`, `SelectedTexture`, `DisabledTexture` (all optional). Set `CenterTexture = true` when the textures differ in size (e.g. status-book tabs with a small normal and a big selected).                                                                                                                                    |
| `UILabel`       | Non-editable text.                            | Wraps a `TextElement`. Optional selection and word wrap. Re-measures only when content or color changes. `ColorCodesEnabled` passes through to the renderer.                                                                                                                                                                                                           |
| `UITextBox`     | Editable text input.                          | Wraps a `TextElement`. Blinking caret, click-to-position, drag-to-select, double/triple click for word/line selection. Routes its own focus via the `TextBoxFocusGained` event.                                                                                                                                                                                        |
| `UIProgressBar` | Fill bar.                                     | —                                                                                                                                                                                                                                                                                                                                                                      |

**Subclassing `UIElement`.** Override `Draw(SpriteBatch)` and call `base.Draw()` first so the background and border
render behind your content. Use the auto-clipping helpers — `DrawTexture`, `DrawRectClipped`, `DrawTextClipped`,
`DrawTextShadowedClipped` — rather than drawing through `SpriteBatch` directly. Override `Update(GameTime)` for
animations and timers; it runs every frame for every visible element regardless of focus. Override
`ResetInteractionState()` if you track transient hover/press/drag state — it's called recursively when a parent hides so
stale state doesn't linger next time the element becomes visible.

## Input

Input is a two-layer stack: `InputBuffer` captures raw input from the OS between frames, and `InputDispatcher` turns the
buffered snapshot into typed events and routes them to UI elements. `InputBuffer` is a **static** class — read
`InputBuffer.MouseX`, `InputBuffer.WasKeyPressed(...)`, or walk `InputBuffer.Events` from anywhere. `InputDispatcher` is
instance-based and owned by `ChaosGame`. Each frame, `ChaosGame.Update` calls `InputBuffer.Update(IsActive)` first to
freeze a snapshot, then the active screen calls `Dispatcher.ProcessInput(Root, gameTime)`.

### `InputBuffer`

Event-driven capture with a per-frame freeze. Every keyboard, text, mouse button, and mouse wheel event arrives via a
single SDL event watcher (`SDL_AddEventWatch`) that fires synchronously on the main thread during `SDL_PumpEvents`. One
callback in OS order means a macro like `click → scroll → click → scroll` replays in exactly that order instead of being
reordered by kind.

Lifecycle is `Initialize()` / `Update(isActive)` / `Shutdown()`. `Update` pumps SDL, drains the pending event list, and
refreshes the cursor from `SDL_GetMouseState` — pump-then-read so `MouseX`/`MouseY` always reflect the true end-of-frame
position, even when a macro's trailing mouse move lands mid-frame.

Mouse button and wheel events carry the cursor position and `SDL_GetModState()` snapshot captured at the moment of the
event, in virtual (640×480) coordinates. Keyboard events are translated from `SDL_Scancode` to MonoGame `Keys`.

Keyboard API:

- `WasKeyPressed(Keys)` / `WasKeyReleased(Keys)` — rising/falling edge this frame. OS key-repeat is filtered out of
  `WasKeyPressed` but still produces `TextInput` characters.
- `IsKeyHeld(Keys)` — event-tracked, not polled.
- `TextInput` — `ReadOnlySpan<char>` of characters typed this frame (includes key-repeat).
- Numpad digits are normalized to the main row (`NumPad3` → `D3`) so hotkeys don't care which one the user hit.

Mouse API:

- `MouseX` / `MouseY` in **virtual** (640×480) coordinates. `SetVirtualScale(scale)` tells the buffer the backbuffer
  stretch factor so raw window coords get divided back down — same divisor for polled getters and per-event capture, so
  they always agree. If you change the letterboxing math, also update this.
- `IsLeftButtonHeld` / `IsRightButtonHeld` — per-**window** flags, flipped by the SDL watcher. A click in another
  application never sets them to `true`.
- `Events` — a `ReadOnlySpan<BufferedInputEvent>` of all this frame's input in OS post order. Each entry is a tagged
  record struct; `Kind` (`KeyDown` / `KeyUp` / `TextInput` / `MouseButton` / `MouseWheel`) selects which fields are
  meaningful. Mouse events carry virtual `X`/`Y` + modifiers; wheel events carry integer `WheelDelta` notches (
  positive = up).

Behaviors worth knowing about:

- When `Game.IsActive` is false, buffered input is discarded and nothing is reported, but the cursor position still
  refreshes so the custom cursor draws in the right spot. On focus regain, buffered mouse button events are dropped and
  the held flags cleared so the focus-click doesn't fire a UI interaction — keyboard events are preserved so held
  hotkeys remain responsive.
- The dispatcher walks `Events` in order to (a) suppress a `TextInput` whose preceding `KeyDown` was consumed as a
  hotkey, and (b) maintain a running modifier state so each keystroke is stamped with the modifiers held at the moment
  it fired — important for macros that chord modifiers with other keys inside a single frame.

### `InputDispatcher`

Turns the buffered snapshot into typed `InputEvent`s (`MouseDownEvent`, `ClickEvent`, `KeyDownEvent`, `TextInputEvent`,
`DragStartEvent`, and so on) and delivers them to UI elements. Call `ProcessInput(root, gameTime)` once per frame from
the active screen. Exposed as a singleton via `InputDispatcher.Instance` for UI controls that need to push themselves
onto the control stack (see `PrefabPanel.Show`/`Hide`).

**Hit-testing.** Walks the element tree top-down, deepest-child-first, highest-`ZIndex`-first. Skips elements that
aren't `Visible` / `Enabled` / `IsHitTestVisible`. A panel with `IsPassThrough = true` never matches itself — only its
children — so clicks that miss every child fall through to whatever is behind the panel.

**Mouse event routing.**

- **MouseMove** — routed to the hit element under the cursor (or to the *captured* element if a button is currently
  held, so a scrollbar or text selection keeps tracking after the cursor leaves the widget). Hover tracking uses the
  same hit result to drive `OnMouseEnter` / `OnMouseLeave`.
- **MouseScroll** — delivered to the hit element, bubbles.
- **MouseDown** — hit-tested, then the hit element is *captured*. All subsequent `MouseMove` events go to the captured
  element until release.
- **MouseUp → Click → DoubleClick** — on release, `MouseUp` is delivered to the captured element. If the cursor is still
  inside the captured element's bounds and no drag occurred, `Click` follows. A second click on the same target within
  300 ms synthesizes `DoubleClick`. Both bubble.
- **Drag** — once the cursor travels more than 4 px from the down position, `DragStart` fires on the captured element.
  The handler sets `e.Payload` if it wants to commit — otherwise the drag is dropped. For a committed drag, `DragMove`
  fires every frame on the element currently under the cursor (not the captured source), and `DragDrop` fires on
  release.

**Keyboard event routing** is two-phase:

1. **Phase 1 — explicit focus.** If something has called `SetExplicitFocus(element)` (the built-in case is `UITextBox`,
   which routes its own focus via the `TextBoxFocusGained` event), the focused element receives `KeyDown` / `KeyUp` /
   `TextInput` directly with **no bubbling**. If the focused element is not a panel, "phase 1.5" then delivers the event
   to its immediate parent panel as well — that's what lets a dialog close on Escape while the textbox eats all the
   other keystrokes.
2. **Phase 2 — control stack.** If there's no explicit focus, or the focused element didn't set `e.Handled`, the event
   goes to the topmost panel on the **control stack** and bubbles up to `root`. Bubbling stops as soon as a handler sets
   `e.Handled = true`.

**The control stack** is the mechanism for "this popup is open, so its keys win over the world screen."
`PushControl(panel)` puts a panel on top, `RemoveControl(panel)` pulls it off. `PrefabPanel.Show` / `Hide` do this
automatically when the panel has `UsesControlStack = true`. Most popups (inventory, dialogs, exchange, etc.) opt in,
which is why opening the inventory doesn't let the number-row hotkeys leak through to the world until you close it.

**Mouse blocking during textbox focus.** When a textbox has explicit focus, mouse button events outside the panel
containing the textbox are swallowed. You can't accidentally click past a modal dialog onto the world behind it while
typing.

**Hotkey-to-textbox leak suppression.** When a `KeyDown` causes a textbox to gain explicit focus (e.g., pressing Enter
to focus the chat textbox), the immediately-following `TextInput` is suppressed so the hotkey character doesn't leak
into the now-focused textbox — otherwise the Enter would immediately insert a newline. This works because
`InputBuffer.Events` preserves the OS `KeyDown → TextInput` ordering — without that ordering, the dispatcher wouldn't
know the two events were paired.

**State reset.** `Dispatcher.Clear()` is called by `ScreenManager` on screen switch to wipe the control stack, explicit
focus, hover, capture, and drag state so nothing bleeds across transitions.

### Where to put new input handling

Depends on the layer you want to intercept at.

- **Inside a UI element**: override `OnKeyDown` / `OnClick` / `OnMouseScroll` / etc. on your `UIElement` or `UIPanel`
  subclass. Set `e.Handled = true` to stop bubbling. The event reaches you either because your panel is the current
  control-stack top, because it's under the cursor, or via bubbling from a descendant.
- **From a screen**: put the logic in `WorldScreen.InputHandlers.cs` (or the equivalent in your own screen). That code
  runs inside the screen's `Update` and reads `InputBuffer.WasKeyPressed`, `InputBuffer.IsKeyHeld`, etc. directly
  against the static buffer snapshot — the right place for world-screen hotkeys like movement, casting, and pathfinding,
  because they shouldn't care about the dispatcher's control-stack routing.

## Renderers

All renderers live in `Chaos.Client.Rendering/`. Quick reference:

| Renderer                                                                | Purpose                                                                                                         |
|-------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------|
| `TextureConverter`                                                      | Static utility. SkiaSharp `SKImage` → MonoGame `Texture2D`.                                                     |
| `Camera`                                                                | Isometric world/screen/tile coordinate math.                                                                    |
| `MapRenderer`                                                           | Background + foreground tile rendering, animated tile playback.                                                 |
| `PaletteCyclingManager`                                                 | Palette shimmer for cycling-palette tiles. Owned by `MapRenderer`.                                              |
| `LightingSystem`                                                        | Per-frame light source buffer. Feeds `DarknessRenderer` and `TabMapRenderer`. Lives in `Chaos.Client/Systems/`. |
| `DarknessRenderer`                                                      | Light/dark overlay — light metadata lookup, HEA sampling, light sources.                                        |
| `TabMapRenderer`                                                        | Custom Tab map (wall diamonds + entity dots) with fog-of-war on dark maps.                                      |
| `WeatherRenderer`                                                       | Snow and rain overlay driven by the `MapFlags` low nibble.                                                      |
| `SilhouetteRenderer`                                                    | Occluded-entity silhouettes and transparent-aisling compositing.                                                |
| `TextRenderer` + `FontAtlas`                                            | Per-character text draws from a shared glyph atlas.                                                             |
| `UiRenderer`                                                            | Deduplicated UI texture cache from control prefabs.                                                             |
| `CreatureRenderer`, `AislingRenderer`, `EffectRenderer`, `ItemRenderer` | Per-entity sprite caches. **Must** `Clear()` on map change.                                                     |

> [!IMPORTANT]
> The four per-entity renderers cache `Texture2D` outputs lazily. Forgetting to call `Clear()` on map change leaks GPU
> memory. If you add a new renderer that caches textures, follow the same pattern.

Supporting types are worth knowing:

- **`CachedTexture2D`** — `Texture2D` subclass whose `Dispose` is a no-op. Only the owning cache can release GPU memory,
  via `ForceDispose`. Let's cache consumers hand the texture around freely without worrying about double-dispose.
- **`TextureAtlas`** — packs many small textures into atlas pages. Grid packing (uniform sizes, used for tiles) and
  shelf packing (variable, used for tab-map wall variants). Used wherever batch throughput matters more than per-texture
  flexibility.

### `TextureConverter` (static)

The SkiaSharp → MonoGame bridge. Converts `SKImage` to `Texture2D` as RGBA8888 premultiplied, plus `LoadSpfTexture`,
`LoadEpfTextures`, and `RenderSprite` helpers. Every asset path in the codebase eventually routes through this class.

### `Camera`

Isometric coordinate math: `WorldToScreen`, `ScreenToWorld`, `TileToWorld`, `WorldToTile`, `GetVisibleTileBounds`.
Configurable viewport, zoom, and center offset. One instance, owned by `WorldScreen`, handed to every renderer that
needs to know what the player is looking at.

### `MapRenderer`

Background and foreground tile rendering. Background tiles are packed into a `TextureAtlas` at map load for batch
throughput; foreground tiles are looked up per-tile from a dictionary and bottom-aligned (the painter's algorithm
depends on it). Per frame, it walks `gndani.tbl` / `stcani.tbl` animation sequences via
`DataContext.Tiles.GetBgAnimation` / `GetFgAnimation` to pick the current tile ID for animated tiles, then consults
`PaletteCyclingManager` for any overridden atlas regions so shimmer tiles swap palette in place without a texture
rebuild. Call `Clear()` on map change.

### `PaletteCyclingManager`

Handles palette shimmer — tiles whose palette entries cycle through a color range, declared in the `mpt` / `stc` palette
files and exposed via `PaletteLookup`. On map load it scans which tiles on the current map use cycling palettes,
pre-renders each palette-shifted variant into the tile image cache, and registers the resulting atlas regions. Each
frame it advances its own tick and writes the current-step region overrides into `BgOverrides` / `FgOverrides`, which
`MapRenderer` checks before the default atlas lookup. It also consults the tile animation tables (`gndani.tbl` /
`stcani.tbl`) during the pre-render scan, but **only** to widen the set of tiles that need shimmer variants — actual
animation-frame switching lives in `MapRenderer`, not here. Owned by `MapRenderer`, not the game directly.

### `LightingSystem`

Centralized owner of the per-frame `LightSource` buffer. Lives in `Chaos.Client/Systems/` (not
`Chaos.Client.Rendering/`) because it walks `WorldState` to build the buffer, but its single consumer audience is the
two renderers that read light sources — `DarknessRenderer` and `TabMapRenderer`. Both renderers treat the system as
read-only and do **not** keep their own copy.

`Gather(MapFile, MapFlags, Camera)` runs once per frame from `WorldScreen.Update.cs`, short-circuiting to an empty span
when the map is null or `MapFlags.Darkness` isn't set — so stale sources from a previous dark map can't leak across a
transition to a lit one. When the map is dark, it walks `WorldState.GetEntities()`, filters for `LanternSize != None`,
pulls the pixel-space `LightMask` from `DataContext.LightMasks`, picks up the tile-space `TileOffsets` array from
`GetTileOffsets`, and packs both together into a `LightSource` record. `Sources` then exposes the populated region as a
`ReadOnlySpan<LightSource>` whose lifetime is bounded by the next `Gather` call.

`LightSource` carries **both** the pixel-space data used by `DarknessRenderer` (screen position + `LightMask`) and the
tile-space data used by `TabMapRenderer` (`TileX`, `TileY`, `Direction`, `TileOffsets`). Doing the gather once and
publishing a unified buffer means both renderers agree on the set of light sources every frame — there's no drift.

Tile-space shapes are cached static arrays computed at class initialization, so every light source of a given lantern
size shares the same array reference:

- `Euclidean3` — radius 3 circle, used for `LanternSize.Small`.
- `Euclidean5` — radius 5 circle, used for `LanternSize.Large`.
- `BaselineVisibilityOffsets` — radius 0 (player tile only), used by `TabMapRenderer` to guarantee the player always
  sees their own tile even with no lanterns in range.

`ComputeEuclidean` uses a half-step bulge: a tile counts when `4*(dx² + dy²) < (2*radius + 1)²`. That's an all-integer
rearrangement of `√(dx² + dy²) < radius + 0.5`, which produces slightly fuller, rounder circles than a strict `≤ radius`
test. The same expression doubles as the size hint for the stackalloc scratch buffer. Computation runs off a `Span<>`
into a `stackalloc` buffer and ends in a single `.ToArray()` — no intermediate `List<>`.

`GetTileOffsets(LanternSize, Direction)` currently returns the same circular array regardless of direction, but the
`Direction` parameter is wired through so future directional shapes (cones, line-of-sight, cardinal sectors) drop in by
returning a different cached array per direction without touching either renderer.

### `DarknessRenderer`

The light/dark overlay. Four inputs drive it:

1. **The map's darkness flag** (`isDarkMap`, passed to `OnMapChanged`). Dark maps start pure black immediately on map
   change, so the unlit map never flashes in before the first `LightLevel` packet arrives. `IsFullBlackDark` then
   exposes the "alpha = 1, color = black" condition to `TabMapRenderer` for fog-of-war gating.
2. **Server `LightLevel` packets combined with light metadata.** Each map has a *light type* looked up in
   `LightMetadata.MapLightTypes` (defaults to `"default"`). On every `LightLevel` packet the renderer builds a key
   `{lightType}_{hexLightLevel}` and looks up `(R, G, B, Alpha)` in `LightMetadata.LightProperties`. That's how the same
   light level produces a different tint in a cave vs. outdoors. If the key isn't in metadata but the map is flagged
   dark, it falls back to pure black; if neither, the overlay is fully transparent.
3. **The map's HEA file**, a per-pixel light map loaded in `OnMapChanged` when one exists. It encodes layered brightness
   data that gets sampled into the overlay texture as the camera moves; without one, the overlay is flat-filled with the
   current color.
4. **The current-frame `LightSource` span from `LightingSystem`**, passed into `Update(camera, viewport, sources)` by
   `WorldScreen.Draw.cs` each frame. Sources are max-blended into the overlay via `StampLightSources` so lanterns,
   windows, and entity-attached lights brighten specific areas. The renderer owns no `LightSource` state of its own —
   it's a pure consumer of the span — and dirty-checks the incoming sources via `ComputeSourceHash` to skip rebuilds
   when nothing has moved.

The final texture is sized to the current viewport.

> [!WARNING]
> The overlay texture is dirty-checked on the camera offset, the viewport dimensions, **and** the hash of the incoming
> light source span. If you add a new source of viewport change, extend the dirty check, or you will see stale overlays —
> this is how the HUD-swap bug happened.

### `TabMapRenderer`

The custom Tab map. Walls are drawn as 20×10 scaled diamonds, and adjacent walls collapse their shared borders via a
4-bit neighbor mask that indexes into 16 pre-baked atlas variants. Entities draw as colored diamonds on top (yellow
player, red monsters, green merchants, blue aislings), and entity overlap is resolved with stencil masking.
PageUp/PageDown zoom, centered on the player. Look is intentionally different from retail — replace this class if you
need pixel-accurate retail behavior.

**Fog-of-war on dark maps.** When the player is on a full-black dark map (`DarknessRenderer.IsFullBlackDark` is true),
`TabMapRenderer.Draw` takes the light source span from `LightingSystem` and a baseline offset array (radius 0 — player's
own tile) and computes a per-frame visibility scratch grid via `ComputeVisibility`. Every light source in the span
stamps its cached `TileOffsets` into the grid centered on its `(TileX, TileY)`; the baseline offsets stamp around the
player. Tiles outside the stamped set are omitted from the tab map draw that frame, so you only see what the player and
their lanterns actually illuminate. On lit maps the fog-of-war path is skipped and the full map draws as normal. Because
the visibility grid is rebuilt every frame from the span, moving lanterns and direction changes propagate automatically
with no extra bookkeeping.

### `WeatherRenderer`

Snow and rain overlay driven by the low nibble of the map's `MapFlags` byte — `0` = none, `1` = snow, `2` = rain, `3` =
darkness (handled by `DarknessRenderer`, so this renderer stays inactive on value 3). The high nibble is unrelated (
`NoTabMap`, `SnowTileset`). See `docs/re_notes/map_flags.md` for the full encoding. Snow uses the `snowaNN.epf` sprite
files from `legend.dat`; rain uses `rain01.epf`. Both load paths share a cached `legend01.pal` palette — the same
palette the retail client uses for weather sprites. Owned by `WorldScreen` in parallel with `DarknessRenderer`;
`OnMapChanged` only touches textures when the nibble actually changes, and asset-load failures clear the nibble so
`IsActive` flips false for the rest of the map.

Every tunable — particle count, fall speed, drift range, frame duration, column count — is a `const` at the top of the
file. Change, rebuild, and the effect reflects the new values. There's no runtime config surface.

> [!NOTE]
> Rain (nibble `2`) is client-only. Retail treats it as a no-op, but this client renders it intentionally as a
> deliberate divergence.

### `SilhouetteRenderer`

Two related effects composited through offscreen `RenderTarget2D`s.

**Occluded-entity silhouettes.** Any entity registered for the current frame via `AddSilhouette(entityId)` is drawn into
a single viewport-sized render target, which is then overlaid on top of the world pass at reduced alpha. Because the RT
captures all the registered entities at their real world positions, inter-entity occlusion inside the silhouette layer
still works. The mechanism is entity-type agnostic — but right now the only caller is the silhouette pre-pass in
`WorldScreen.Draw.cs`, and that block only registers the player. That's why your own character is the only thing that
currently shows a silhouette through foreground tiles.

To give another entity the same treatment, add a call to `SilhouetteRenderer.AddSilhouette(entity.Id)` in that same
pre-pass block, alongside the existing player registration. Pick whatever criterion you want — party members, every
aisling on the map — the renderer doesn't care.

**Transparent aislings.** Aislings flagged `IsTransparent` are drawn by `AislingRenderer.Draw` at `TRANSPARENT_ALPHA` (
0.33) through the same per-entity `CompositeCache` texture used for normal drawing. Because the composite is a single
pre-blended image, modulating it at uniform alpha produces the correct result with no layer bleed-through. The local
player is handled specially: it's routed **exclusively** through the silhouette pass (skipping the stripe-pass draw
entirely in `DrawAisling`) so its displayed alpha is identical in the open and behind walls. During the silhouette pass,
transparent entities draw at `TRANSPARENT_SILHOUETTE_ALPHA` (`TRANSPARENT_ALPHA / SILHOUETTE_ALPHA` = 0.66) so the
silhouette RT overlay compounds to 0.33 net on screen.

Call `Clear()` at the start of every frame before adding entries.

### `TextRenderer` + `FontAtlas`

`TextRenderer` is a static class that draws text per-character from `FontAtlas`. Supports mixed English (8×12) and
Korean (16×12) glyphs decoded through codepage 949. Inline `{=x` color codes, drop shadows, and word wrap are all
handled here. `ColorCodesEnabled` is a per-call flag; widgets expose it as a property and pass it down.

`FontAtlas` holds the pre-built glyph atlases. Glyphs are rasterized white-on-transparent so `SpriteBatch` vertex
coloring can tint them to any color at draw time — which is why there's exactly one atlas per script regardless of how
many colors text is drawn in.

`LegendColors` maps the Dark Ages `LegendColor` enum to MonoGame `Color` values and is initialized at startup.

### `UiRenderer`

Cache for UI textures loaded from control prefabs. Single instance accessed via `UiRenderer.Instance`; deduplicates
textures across every panel that uses the same prefab entry. Returned textures are `CachedTexture2D`s, so UI controls
can assign them freely without worrying about disposal. Never evicts — UI textures are `NeverRemove` priority because
the HUD is always on screen.

### Per-entity renderers: `CreatureRenderer`, `AislingRenderer`, `EffectRenderer`, `ItemRenderer`

All four follow the same pattern: lazy `Texture2D` cache keyed on the state that produced the output, `Clear()` on map
change, leak GPU memory if you forget to call `Clear`.

- **`CreatureRenderer`** — creatures and NPCs from `MpfFile`. Cache key is `(spriteId, frameIndex)`.
- **`AislingRenderer`** — player characters. Layered compositing of body, face, hair, armor, pants, boots, overcoat,
  weapon, shield, and accessories, in an order that depends on whether the aisling is facing the camera or away. This is
  where several of the Differences listed above are implemented: `b` body for BlowKiss (#1), overcoats with palette
  IDs ≥ 1000 (#5), and pants-under-overcoats (#10).

  > [!WARNING]
  > The cache key must include **every** visible piece of state — direction, frame, dye colors, each sprite ID, the
  overcoat-permits-pants flag. Missing state in the cache key is the single most common source of visual bugs in this
  codebase.
- **`EffectRenderer`** — spell and hit effects. Supports both EFA (self-contained animation file) and EPF (
  frame-sequence driven by `effect.tbl`). The format is chosen per entry in `effect.tbl`: `[0]` means EFA; any other
  entry lists the EPF frame indices to play.
- **`ItemRenderer`** — ground items. Deliberately separate from `UiRenderer`'s permanent icon cache because ground-item
  textures are evicted on map change while UI icons are not. Cache key includes dye color, and per-frame `(Left, Top)`
  offsets are stored so items center visually on their tile.

## Build & Run

Requires the **.NET 10 SDK**. Builds and runs on Windows, macOS, and Linux.

> [!IMPORTANT]
> The solution has a `ProjectReference` to [DALib](https://github.com/Sichii/DALib) at `../dalib/DALib/DALib.csproj`.
> DALib must be checked out into a sibling `dalib/` directory before the build will resolve. From inside this repo:
>
> ```bash
> git clone https://github.com/Sichii/DALib ../dalib
> ```

Then:

```bash
dotnet build Chaos.Client.slnx
dotnet run --project Chaos.Client/Chaos.Client.csproj
```

> [!NOTE]
> The client also needs a retail Dark Ages data folder to load its archives from. Point `GlobalSettings.DataPath` at
> yours before the first run, or the game will fail to start.

> [!NOTE]
> **Linux users:** install SDL2_mixer's runtime deps via your package manager — the bundled `libSDL2_mixer.so` relies on
> system-provided codec libraries (`libmpg123`, `libvorbisfile`, `libFLAC`, `libfluidsynth`, etc.) that come free with
> the distro package:
>
> ```bash
> sudo apt install libsdl2-mixer-2.0-0       # Debian/Ubuntu
> sudo dnf install SDL2_mixer                # Fedora/RHEL
> sudo pacman -S sdl2_mixer                  # Arch
> ```
>
> Windows and macOS ship fully self-contained binaries — no extra install step.

## Configuration

Almost everything a fork needs to change is in `Chaos.Client/GlobalSettings.cs`:

| Setting                | What it is                                                                                                                          |
|------------------------|-------------------------------------------------------------------------------------------------------------------------------------|
| `ClientVersion`        | Version sent to the server on handshake. Default `741`.                                                                             |
| `DataPath`             | Absolute path to the Dark Ages data folder (contains the `.dat` archives).                                                          |
| `LobbyHost`            | Lobby server hostname or IP.                                                                                                        |
| `LobbyPort`            | Lobby server port.                                                                                                                  |
| `RequireSwimmingSkill` | When `true`, restores retail swim gate — water tiles require the GM flag or the `Swimming` skill. Default `false` (no requirement). |

### Environment-variable overrides

The following env vars override the defaults at startup without requiring a rebuild. Useful for dev workflows, CI, and launchers that need to redirect the client to a different lobby or data directory.

| Env var         | Overrides     | Validation                                                |
|-----------------|---------------|-----------------------------------------------------------|
| `DA_HOST`       | `LobbyHost`   | Non-empty, non-whitespace; else default `da0.kru.com`.    |
| `DA_HOST_PORT`  | `LobbyPort`   | Integer 1–65535; else default `2610`.                     |
| `DA_ASSET_PATH` | `DataPath`    | Path string; not validated by the client.                 |

The resolved lobby target (and the raw env-var values it was derived from) is written to `notice-debug.log` at startup, so an override that was rejected by validation is visible without attaching a debugger.

## Extending

### Adding a UI panel

1. Place the `.txt` + `.spf`/`.epf` prefab in an archive.
2. Derive a class in `Chaos.Client/Controls/` from `PrefabPanel` and use `CreateXxx` to instantiate the children you
   care about.
3. If it's a popup, add it as a child of `WorldScreen.Root` and toggle with `Show()` / `Hide()`.
4. Subscribe to any needed `ConnectionManager` events in `WorldScreen.Wiring.cs`.

### Adding a packet handler

There are two cases, and they're very different.

**Handling an opcode `Chaos.Networking` already defines.** This is the common case — the library knows the packet shape
and has the args type and converter; you need the client to react. Three additions:

1. In `ConnectionManager`, write a handler that deserializes into the existing args type and fires an event:

   ```csharp
   private void HandleFoo(ServerPacket pkt)
   {
       var args = Client.Deserialize<FooArgs>(in pkt);
       OnFoo?.Invoke(args);
   }

   public event Action<FooArgs>? OnFoo;
   ```

2. Register it in `IndexHandlers()`:

   ```csharp
   PacketHandlers[(byte)ServerOpCode.Foo] = HandleFoo;
   ```

3. Subscribe to `OnFoo` in `WorldScreen.Wiring.cs` and update game state / UI from there.

Outbound packets are symmetric — add a method on `ConnectionManager` that calls `Client.Send(new FooArgs { ... })`.

**Adding a brand-new packet the library has never seen.** A new packet means a new `ServerOpCode` (or `ClientOpCode`)
enum value, a new args type, and a new `IPacketConverter` to serialize/deserialize it. All of that lives in
`Chaos.Networking` and its dependencies, which this project consumes as a NuGet package — you can't add new types to a
compiled dependency.

The most obvious path is to **fork [Chaos-Server](https://github.com/Sichii/Chaos-Server), drop the `Chaos.Networking`
NuGet reference from this project, and add `ProjectReference`s to the networking source projects from your server fork
**. The server repo ships the full source for `Chaos.Networking` and its dependencies; you can pull those into the
client solution and leave the server-only `Chaos` project out if you don't want it here. New opcodes / args / converters
then live in a single source tree that both client and server compile against, which keeps them in sync by construction.

Other routes exist — republishing your own preview NuGets, or shimming new converters on the client side alongside the
library's — but referencing the source projects directly is the path with the fewest moving parts, and if you're adding
a protocol extension, you'll already have the server repo open anyway.

## Related Repositories

- [Chaos-Server](https://github.com/Sichii/Chaos-Server) — the private server this client targets, and the source of the
  canonical packet shapes.
- [DALib](https://github.com/eriscorp/dalib) — nuget package for reading and writing Dark Ages `.dat` archives.
