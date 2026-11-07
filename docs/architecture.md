# Brigid architecture

This document describes how Brigid works inside: the projects, the world state, the draw order, the UI and input systems, and the renderers. The last section tells how to add a panel, a packet handler or a hotkey. The [README](../README.md) tells how to install, run and build Brigid.

## Contents

- [Projects](#projects)
- [World state](#world-state)
- [Draw order](#draw-order)
- [UI system](#ui-system)
- [Input](#input)
- [Renderers](#renderers)
- [Extending](#extending)

## Projects

| Project | Responsibility |
| --- | --- |
| `Brigid.Data` | Opens the `.dat` archives through memory-mapped files and gives repositories for sprites, tiles, fonts, metafiles, UI prefabs and other assets. Loads the `.datf` asset packs (`AssetPackRegistry`). Holds the metafile and map caches. |
| `Brigid.Rendering` | Converts the SkiaSharp images of DALib to MonoGame `Texture2D`s. Holds the map, camera, darkness, weather, Tab map, text and per-entity renderers, and the theme art. |
| `Brigid.Networking` | The TCP client (`GameClient`) and the connection state machine (`ConnectionManager`). DALib does the framing, the encryption and the packet types. `Hybrasyl.Protocol` does the TLS upgrade and the extension packets. |
| `Brigid` | The MonoGame `Game` (`ChaosGame`), the screens, the UI controls, the game systems and the world state. |
| `Brigid.Tests` | xunit regression and smoke tests. |

The `Chaos.Common`, `Chaos.DarkAges`, `Chaos.Geometry` and `Chaos.Pathfinding` packages give shared enums, extension methods, geometry and A* pathfinding. They do not serialize packets. Hybrasyl equivalents will replace them.

Dependency flow:

```text
DALib ──> Brigid.Data ──> Brigid.Rendering ────┐
                │                              ├──> Brigid ──> Brigid.Tests
                └──────────────────────────────┤
DALib, Hybrasyl.Protocol ──> Brigid.Networking ┘
```

`Brigid.Rendering` has no dependency on `Brigid.Networking`. The `Brigid` project connects the two.

DALib models the retail wire format. A difference in the Hybrasyl protocol goes in `Brigid.Networking/Definitions/HybrasylExtensions.cs`, not in DALib.

## World state

`WorldState` (`Brigid/Collections/`) is a static class. It tracks the entities, the active effects and projectiles, and the view-model objects: `Attributes`, `Board`, `Chat`, `Equipment`, `Exchange`, `Group`, `GroupInvite`, `Inventory`, `NpcInteraction`, `SkillBook`, `SpellBook`, `UserOptions` and `WorldList`.

`ConnectionManager` raises an event for each server packet. `WorldScreen.Wiring.cs` subscribes to the events and writes the data into `WorldState`. Controls read `WorldState` directly, with no constructor injection. `WorldState` is the one source of the data that the world screen shows.

## Draw order

`ChaosGame` draws each frame in two passes:

1. **The world pass** draws into a 640×480 render target. `ChaosGame` then stretches the target to the window with point sampling.
2. **The native pass** draws at the native size of the window, so that text and high-resolution art stay sharp.

All text draws in the native pass. Text in the world pass is stretched with the world and looks blurred. A `Debug.Assert` in `FontEngine` finds text that draws in the wrong pass.

`WorldScreen.Draw` does the world pass:

1. **Silhouette pre-pass.** The local player draws into a render target the size of the viewport. This step occurs before the main drawing, because a change of render target removes the contents of the main target.
2. **Background.** The background tiles and the tile cursor draw in one batch.
3. **Diagonal stripes.** The foreground tiles, entities and effects draw in stripes of the same depth (`x + y`). In each stripe the order is: ground items, aislings, creatures, the dissolves of dying creatures, ground effects, projectiles, entity effects, foreground tiles.
4. **Silhouettes.** The silhouette target draws over the world at reduced alpha.
5. **Darkness.** The darkness overlay draws in screen space. `WorldScreen.Update` calls `LightingSystem.Gather` and then `DarknessRenderer.Update`, so the overlay uses the light sources of the current frame.
6. **Weather.** Snow or rain draws after the darkness, so the weather shows on a dark map.
7. **Blind.** When the player is blind, the viewport fills with black and the player draws again on top.
8. **Debug overlay**, if it is on.
9. **Tab map**, if it is open. It draws over the world and under the HUD.

`WorldScreen.DrawNative` does the native pass:

1. **Entity overlays.** Chat bubbles, health bars, name tags, chant text and group boxes. They draw after the darkness, so the light level does not tint them. They draw after the blind fill, so they stay visible while the player is blind. Retail hides the health bars of a blind player, because it draws them with the entity sprite.
2. **Debug labels**, if the debug overlay is on.
3. **UI.** The root panel, with the HUD and all popups.
4. **Drag icon**, if the player drags something.

## UI system

### Components

The base controls are in `Brigid/Controls/Components/`.

| Component | Purpose | Notes |
| --- | --- | --- |
| `UIElement` | The abstract base of each control. | Position, size, padding, `ZIndex`, `BackgroundColor` and `BorderColor`, and a `ClipRect` that each frame intersects with the parent. Input events are virtual methods (`OnClick`, `OnMouseDown`, `OnKeyDown`, `OnTextInput`, `OnDragStart` and others). `X` and `Y` are relative to the parent; `ScreenX` and `ScreenY` are absolute. `UseTheme` is a `bool?`: null takes the value of the parent. |
| `UIPanel` | A container. Disposal goes to the children. | Children draw in `ZIndex` order, and in the order they were added when the `ZIndex` is the same. `IsPassThrough`: the children hit-test, but the panel does not. `IsModal`: the panel is a modal while it is on the control stack (see [Modals](#modals)). `UsesControlStack`: the panel goes on the control stack when it shows. |
| `PrefabPanel` | A `UIPanel` that loads a legacy control file from the data archives. | The first control (the anchor) sets the size, the position and the background. `CreateButton`, `CreateImage`, `CreateLabel`, `CreateTextBox` and `CreateProgressBar` make the named children; each returns null when the name is not in the file. `GetRect("name")` gives a rectangle and makes no child. `Show` and `Hide` push and remove the panel on the control stack. |
| `TextElement` | Text state. **It is not a `UIElement`.** | Holds the text, colour, wrap cache, shadow, alignment and colour-code setting. Controls that show text keep a `TextElement`. Measure with `TextElement.Measure`, and hit-test a click with `TextElement.IndexAtX`, not with `TextRenderer`. |
| `UIImage` | A texture. | Disposes its `Texture`. |
| `UIButton` | A clickable texture with up to five states. | `NormalTexture`, `HoverTexture`, `PressedTexture`, `SelectedTexture`, `DisabledTexture`. Set `CenterTexture = true` when the textures are not the same size. |
| `UILabel` | Text that the user cannot edit. | Optional selection and word wrap. |
| `UITextBox` | Text that the user can edit. | Caret, selection with the mouse, double and triple click, undo, and the clipboard. It raises `TextBoxFocusGained` to take the keyboard. |
| `UIProgressBar` | A fill bar. | |
| `DialogPalette` | The one source of the flat colours. | `ForRole` gives the flat colour of each theme colour role. |

### Primitives and themes

New UI does not read the legacy control files. It uses the primitives in `Brigid/Controls/Generic/`: `CenteredModalPanel`, `TextButton`, `SelectableTab`, `TabStrip`, `CheckBox`, `DropDown`, `Stepper`, `SliderControl`, `ScrollBarControl`, `FramePanel`, `PopupMenu`, `TooltipFrame` and others. Each primitive has a flat look from `DialogPalette`.

When `UIElement.IsThemed` is true, a primitive draws the parts of the active `ui_theme` pack. For a part that the theme does not supply, it keeps the flat look. Follow these rules:

- Draw theme art only through `UIElement.TryDrawThemePart`, and theme colours only through `ThemeColor`.
- A panel draws its fill, border and background in `UIPanel.DrawChrome`. A themed control overrides `DrawChrome`, and calls the base only when `TryDrawThemePart` returns false.
- A label or text box whose colour is part of the look calls `SetForegroundRole(role, fallback)` one time. Do not set colours from the theme in `Draw`.
- A colour that has a meaning (a cooldown, a server-sent name colour) stays literal.
- Give each child a `Name`.

The Primitive Gallery shows each primitive in each state. To open it, turn on the debug overlay (F11) and press Ctrl+G.

### Subclassing `UIElement`

- Override `Draw(SpriteBatch)` and call `base.Draw` first. The base draws the background and the border behind your content.
- Draw through the clipping helpers: `DrawTexture`, `DrawRectClipped`, `DrawBorderClipped` and `DrawTextClipped`. Do not draw through the `SpriteBatch` directly.
- Override `Update(GameTime)` for animations and timers. It runs each frame for each visible element.
- Override `ResetInteractionState` when the control keeps hover, press or drag state. A parent calls it on all its children when it hides.
- A control that wraps or fits text while it is visible keeps a `FontGenerationStamp`. It checks `Changed` in `Update` and calls `Mark` when it lays out the text. `FontEngine.Generation` changes when the font face or the window size changes.

## Input

Input has two layers. `InputBuffer` collects the raw input between frames. `InputDispatcher` changes the input of the frame into typed events and sends them to the UI.

`InputBuffer` is a static class. `InputDispatcher` belongs to `ChaosGame`, and `InputDispatcher.Instance` gives it to the controls. Each frame, `ChaosGame.Update` calls `InputBuffer.Update(IsActive)`, and then the active screen calls `Dispatcher.ProcessInput(Root, gameTime)`.

### `InputBuffer`

One SDL event watcher (`SDL_AddEventWatch`) receives each keyboard, text, mouse button and mouse wheel event, in the order that the OS sends them. Thus a macro such as click, scroll, click, scroll plays in that order.

The lifecycle is `Initialize()`, `Update(isActive)` and `Shutdown()`. `Update` pumps SDL, takes the pending events, and then reads the cursor from `SDL_GetMouseState`. Thus `MouseX` and `MouseY` give the true position at the end of the frame.

Keyboard:

- `WasKeyPressed(Keys)` and `WasKeyReleased(Keys)`: the key went down or up in this frame. OS key repeat does not set `WasKeyPressed`, but it adds characters to `TextInput`.
- `IsKeyHeld(Keys)`: the key is down. The events set this value; Brigid does not poll the keyboard.
- `TextInput`: the characters typed in this frame, with key repeat.
- The numpad digits, minus and Enter change to the keys of the main keyboard, so that a hotkey works from both.

Mouse:

- `MouseX` and `MouseY` are in virtual 640×480 coordinates. `SetVirtualScale` gives the buffer the stretch of the window. If you change the letterbox calculation, change this call too.
- Mouse buttons are events only. There is no held-button flag, because a flag loses a click whose press and release occur in the same frame.
- `Events` gives all the events of the frame in order. `Kind` (`KeyDown`, `KeyUp`, `TextInput`, `MouseButton`, `MouseWheel`) tells which fields are valid. Mouse events have the position and the modifiers of the moment of the event. Wheel events have a `WheelDelta` in notches; a positive value is up.

Focus:

- When the window loses focus, the held keys clear, because SDL sends no key-up to an inactive window.
- The click that gives focus to the window arrives as a usual click. The watcher receives only the events of the Brigid window.

### `InputDispatcher`

**Hit-testing.** The dispatcher looks for the deepest child first, and the highest `ZIndex` first. It ignores an element that is not `Visible`, `Enabled` or `IsHitTestVisible`. A panel with `IsPassThrough` never matches, but its children do.

**Mouse events.**

- **MouseMove** goes to the element under the cursor. While a button is down, it goes to the captured element, so a scroll bar or a text selection follows the cursor outside the control. The same hit-test drives `OnMouseEnter` and `OnMouseLeave`.
- **MouseScroll** goes to the element under the cursor, and bubbles.
- **MouseDown** goes to the element under the cursor, which the dispatcher then captures.
- **MouseUp** goes to the captured element. When the cursor is still in that element and no drag occurred, a **Click** follows. A second click on the same element in 500 ms also gives a **DoubleClick**. Both bubble.
- **Drag.** When the cursor moves 4 px or more from the press, `DragStart` goes to the captured element. The handler sets `e.Payload` to start the drag. Then `DragMove` goes each frame to the element under the cursor, and `DragDrop` goes to it at the release.
- A screen that handles a press before the dispatch calls `SwallowPress(eventIndex)`. The dispatcher then ignores only that press.

**Keyboard events** go through these steps:

1. **Explicit focus.** When an element has the explicit focus (usually a `UITextBox`), it receives `KeyDown`, `KeyUp` and `TextInput` with no bubbling.
2. **Parent of the focus.** When the focused element is not a panel, its parent panel also receives the event. Thus a popup closes on Escape while its text box takes the other keys.
3. **Control stack.** When no explicit focus handled the event, it goes to the panel at the top of the control stack, and bubbles up to the root. Bubbling stops when a handler sets `e.Handled = true`.

**Control stack.** A popup goes on the control stack so that its keys come before the keys of the world screen. `PushControl(panel)` puts a panel on top, and `RemoveControl(panel)` removes it. `PrefabPanel.Show` and `Hide` do this when `UsesControlStack` is true; `CenteredModalPanel`, `PopupMenu` and `MarkdownView` push themselves. A push takes the explicit focus, and the removal gives it back. `PushControl(panel, keepTextFocus: true)` leaves the focus on a text box, for a small entry such as an open `DropDown` list. The chat input keeps its focus while popups open.

<a id="modals"></a>**Modals.** `TopModal` is the top visible panel on the control stack whose `IsModal` is true. When a screen sets `EnforceModals` (the lobby does), the dispatcher consumes a mouse event outside the top modal and outside the stack panels above it. A blocked release still ends a capture: the captured element gets its `MouseUp`, a drag ends with `OnDragCancel`, and no click occurs.

**Mouse block during text entry.** While a text box has the explicit focus, the dispatcher consumes a mouse button event outside the panel of that text box. A click on the chat bar always goes through.

**Hotkey and text box.** When a `KeyDown` gives the focus to a text box (for example, Enter focuses the chat input), the dispatcher drops the `TextInput` that follows it. Thus the hotkey character does not go into the text box. This works because `InputBuffer.Events` keeps the `KeyDown` and `TextInput` order of the OS.

**Reset.** `ScreenManager` calls `Dispatcher.Clear()` when the screen changes. `Clear` removes the control stack, the explicit focus, the hover, the capture and the drag, and sets `EnforceModals` to false.

### Where to put new input handling

- **In a control:** override `OnKeyDown`, `OnClick`, `OnMouseScroll` or another event method. Set `e.Handled = true` to stop the bubbling.
- **A world hotkey:** add it to `WorldScreen.OnRootKeyDown` in `WorldScreen.InputHandlers.cs`. The root panel receives a key only when no focused element and no stack panel handled it. Test the key with `Keybinds.Matches(e, CommandId.Xxx)`, not with a literal key. See [Adding a hotkey](#adding-a-hotkey).
- **World mouse input:** `WorldScreen.Update` reads `InputBuffer.Events`, `MouseX` and `MouseY` for clicks on the map and on entities.

## Renderers

The renderers are in `Brigid.Rendering/`, except `LightingSystem`, which is in `Brigid/Systems/`.

| Renderer | Purpose |
| --- | --- |
| `TextureConverter` | Converts a SkiaSharp `SKImage` to a MonoGame `Texture2D`, and makes tinted copies. |
| `Camera` | Isometric world, screen and tile coordinates. |
| `MapRenderer` | Background and foreground tiles, and tile animations. |
| `PaletteCyclingManager` | Palette shimmer on tiles. `MapRenderer` owns it. |
| `LightingSystem` | The light sources of the frame, for `DarknessRenderer` and `TabMapRenderer`. |
| `DarknessRenderer` | The light and dark overlay. |
| `TabMapRenderer` | The Tab map, with fog of war on dark maps. |
| `WeatherRenderer` | Snow and rain. |
| `SilhouetteRenderer` | Silhouettes of hidden entities. |
| `TextRenderer` and `FontEngine` | Text. |
| `UiRenderer` | Shared cache of UI textures from the control files. |
| `ThemeArt`, `BuiltInArt`, `IconArt` | Theme parts, the built-in art, and icon art. |
| `CreatureRenderer`, `AislingRenderer`, `EffectRenderer`, `ItemRenderer` | Per-entity sprite caches. They **must** `Clear()` at a map change. |

> [!IMPORTANT]
> The four per-entity renderers cache `Texture2D`s lazily. If you do not call `Clear()` at a map change, GPU memory leaks. A new renderer that caches textures must follow the same pattern.

Supporting types:

- **`CachedTexture2D`** is a `Texture2D` whose `Dispose` does nothing. Only the cache that owns it can release it, with `ForceDispose`. Thus a control can keep a cached texture with no risk of a double dispose.
- **`TextureAtlas`** packs many small textures into pages. Grid packing is for tiles of one size. Shelf packing is for textures of different sizes, such as the Tab map wall variants.
- **`ImageUtil`** and **`PixelBufferScope`** do the CPU pixel work (tints, fills, gradients). Use `PixelBufferScope` to read and write the pixels of a texture.

### `TextureConverter`

Converts an `SKImage` to a `Texture2D` (RGBA8888, premultiplied) with `ToTexture2D`, and makes the group, hit and hover tints. Each asset goes through this class.

### `Camera`

Isometric coordinates: `WorldToScreen`, `ScreenToWorld`, `TileToWorld`, `WorldToTile` and `GetVisibleTileBounds`. `WorldScreen` owns the one camera and gives it to each renderer that must know what the player sees.

### `MapRenderer`

At map load, `MapRenderer` packs the background tiles into a `TextureAtlas`, so that they draw in one batch. A `static_tiles` pack image replaces a legacy tile in this step. The foreground tiles come from a dictionary, one tile at a time, and align at their bottom edge, which the painter's algorithm needs.

Each frame, `MapRenderer` reads the `gndani.tbl` and `stcani.tbl` sequences through `DataContext.Tiles.GetBgAnimation` and `GetFgAnimation`, and selects the current tile ID of each animated tile. Then it reads the region overrides of `PaletteCyclingManager`, so that a shimmer tile changes palette with no new texture. Call `Clear()` at a map change.

### `PaletteCyclingManager`

Some tiles have palette entries that cycle through a colour range. The `mpt` and `stc` palette files declare them, and `PaletteLookup` gives them. At map load, the manager finds the tiles of the map that use a cycling palette. It renders each shifted variant before play starts, and records the atlas regions. Each frame it advances its tick and writes the current regions into `BgOverrides` and `FgOverrides`, which `MapRenderer` reads before the default atlas.

The manager also reads the tile animation tables during the scan, but only to find more tiles that need shimmer variants. `MapRenderer` changes the animation frames.

### `LightingSystem`

`LightingSystem` owns the light-source buffer of the frame. It is in `Brigid/Systems/` because it reads `WorldState`. `DarknessRenderer` and `TabMapRenderer` read the buffer and keep no copy.

`WorldScreen.Update` calls `Gather(MapFile, MapFlags, Camera)` one time each frame. When there is no map, or the map does not have `MapFlags.Darkness`, the buffer is empty. Thus the light sources of a dark map do not stay on a lit map. On a dark map, `Gather` reads each entity whose `LanternSize` is not `None`. It gets the pixel-space `LightMask` from `DataContext.LightMasks` and the tile-space offsets from `GetTileOffsets`, and puts both in a `LightSource`. `Sources` gives the buffer as a `ReadOnlySpan<LightSource>`, which is valid until the next `Gather`.

A `LightSource` has the data of both renderers: the screen position and `LightMask` for `DarknessRenderer`, and `TileX`, `TileY`, `Direction` and `TileOffsets` for `TabMapRenderer`. One gather for both renderers means that they always use the same light sources.

The tile shapes are static arrays, so all sources of one lantern size share one array:

- `Euclidean3`: a circle of radius 3, for `LanternSize.Small`.
- `Euclidean5`: a circle of radius 5, for `LanternSize.Large`.
- `BaselineVisibilityOffsets`: radius 0, the player tile only. `TabMapRenderer` uses it so that the player always sees their own tile.

`ComputeEuclidean` includes a tile when `4*(dx² + dy²) < (2*radius + 1)²`. This is an integer form of `√(dx² + dy²) < radius + 0.5`, and gives rounder circles than `≤ radius`.

`GetTileOffsets(LanternSize, Direction)` ignores the direction now. The parameter is there so that a directional shape (a cone, a line of sight) can come later with no change to the renderers.

### `DarknessRenderer`

Four inputs drive the overlay:

1. **The darkness flag of the map** (`isDarkMap`, given to `OnMapChanged`). A dark map starts fully black at the map change, so the lit map does not show before the first `LightLevel` packet. `IsFullBlackDark` tells `TabMapRenderer` when to use fog of war.
2. **The `LightLevel` packets and the light metadata.** Each map has a light type in `LightMetadata.MapLightTypes` (the default is `"default"`). For each `LightLevel` packet, the renderer makes the key `{lightType}_{hexLightLevel}` and reads the `(R, G, B, Alpha)` of that key from `LightMetadata.LightProperties`. Thus one light level can give a different tint in a cave and outdoors. When the key is not in the metadata, a dark map is black and another map has no overlay.
3. **The HEA file of the map**, a light map for each pixel, if the map has one. Without one, the overlay is one flat colour.
4. **The light sources of the frame**, given to `Update(camera, viewport, sources)`. `StampLightSources` blends them into the overlay with a maximum. `ComputeSourceHash` lets the renderer skip a rebuild when no source moved.

> [!WARNING]
> The overlay rebuilds only when the camera offset, the viewport size or the hash of the light sources changes. If you add a new cause of a viewport change, add it to this check. If you do not, the overlay goes stale. A HUD swap caused this bug before.

### `TabMapRenderer`

Walls draw as 20×10 diamonds. Adjacent walls merge their shared borders through a 4-bit neighbour mask, which selects one of 16 atlas variants. Entities draw as coloured diamonds: yellow for the player, red for monsters, green for merchants, blue for aislings. Stencil masks resolve overlapping entities. Page Up and Page Down zoom the map around the player.

**Fog of war.** On a fully black dark map, `ComputeVisibility` makes a visibility grid each frame. Each light source stamps its `TileOffsets` around its tile, and the baseline offsets stamp around the player. Tiles outside the stamped set do not draw. On a lit map, the full map draws.

### `WeatherRenderer`

The low nibble of `MapFlags` selects the weather: `0` is none, `1` is snow, `2` is rain, `3` is darkness. `DarknessRenderer` handles darkness, so this renderer is not active for `3`. The high nibble has other flags (`NoTabMap`, `SnowTileset`).

Snow uses the `snowaNN.epf` files from `legend.dat`, and rain uses `rain01.epf`. Both use `legend01.pal`, as retail does. `OnMapChanged` loads textures only when the nibble changes. When a load fails, the renderer clears the nibble and stays inactive on that map.

Retail does nothing for rain. Brigid draws it, and plays the rain sound (`amb_1`) from an `ambient_sounds` pack when one has it. The particle count, the fall speed, the drift and the frame time are constants at the top of the file.

### `SilhouetteRenderer`

**Silhouettes.** Each entity that `AddSilhouette(entityId)` registers for the frame draws into a render target the size of the viewport. The target then draws over the world at `SILHOUETTE_ALPHA` (0.5). The entities draw at their real positions, so they hide each other correctly in the target. Now the only caller is the pre-pass in `WorldScreen.Draw.cs`, and it registers only the local player. In the open, the silhouette matches the pixel under it, so it shows no change. Behind a foreground tile, it shows the player at 50%. To give another entity a silhouette, call `AddSilhouette` for it in the same pre-pass.

**Transparent aislings.** An aisling with `IsTransparent` draws its composite image at `TRANSPARENT_ALPHA` (1/3). The composite is one pre-blended image, so one alpha gives the correct result with no bleed between layers. In the silhouette pass, a transparent entity draws at `TRANSPARENT_SILHOUETTE_ALPHA` (0.5). Together, a transparent player shows at about 50% in the open and 25% behind a foreground tile. The constants are in `WorldScreen.cs`.

Call `Clear()` at the start of each frame, before you add entities.

### `TextRenderer` and `FontEngine`

`FontEngine` loads TrueType fonts through FontStashSharp. It rasterizes glyphs when it needs them into a dynamic atlas, with anti-aliasing and full Unicode (with a CJK fallback). The player can select the font face. A face can have bold and italic files, selected through the `FontStyle` flags. `FontStyle.Mono` uses Noto Sans Mono, and `FontStyle.Display` uses the display face for titles, which a `ui_theme` pack can replace.

`TextRenderer` adds the inline `{=x}` colour codes, drop shadows, clipping, measurement and wrapping on top of `FontEngine`. `ColorCodesEnabled` turns the colour codes off for one control.

Follow these rules. A break of one gives a visual defect, not an error:

- **`FontEngine.UiSize` is the default size**, not `FontEngine.RENDER_SIZE` (15). `UiSize` is the largest size whose glyph ink fits the 12 px line of the Dark Ages layouts, and it is different for each face. Give an explicit size only when the control owns its own line grid.
- **Measure at the size and spacing that you draw at.** If you do not, the caret, the selection and the hit-test move away from the glyphs. Keep the size and spacing of a control in one private helper.
- **Do not add up the widths of single glyphs.** A single glyph has no tracking to the next glyph, and its rounded width loses up to 0.5 px.
- **Key each cached measurement on `FontEngine.Generation`.** The generation changes when the face changes and when the window size changes.

`LegendColors` maps the Dark Ages `LegendColor` values to MonoGame colours. It initializes at startup.

### `UiRenderer`

`UiRenderer.Instance` caches the UI textures that come from the control files, and shares them across all panels that use the same file. It gives `CachedTexture2D`s, so controls can keep them with no disposal. It keeps its textures for the session, because the HUD is always on screen. It also gives the item, skill and spell icons as `IconTexture`s, which carry the offset of a legacy 31×31 icon or a pack icon.

### Per-entity renderers

All four renderers cache textures lazily, keyed on the state that made them, and clear the cache at a map change.

- **`CreatureRenderer`**: creatures and NPCs from `MpfFile`s, or from a `creature_sprites` pack. The key is `(spriteId, frameIndex)`.
- **`AislingRenderer`**: player characters. It composites the body, face, hair, armor, pants, boots, overcoat, weapon, shield and accessories, in an order that changes when the aisling faces away. Several [differences from retail](../README.md#differences-from-the-retail-client) are here: the `b` body for BlowKiss, overcoat palettes of 1000 or more, and pants under overcoats.

  > [!WARNING]
  > The cache key must include **each** visible piece of state: the direction, the frame, the dye colours and each sprite ID. A missing piece of state in the key is the most frequent cause of visual bugs in this code.

- **`EffectRenderer`**: spell and hit effects, as EFA (one animation file) or EPF (frames from `effect.tbl`). In `effect.tbl`, an entry of `[0]` means EFA, and another entry lists the EPF frames to play.
- **`ItemRenderer`**: ground items. It is separate from the icon cache of `UiRenderer`, because ground items clear at a map change. It gets each image from `ItemIconSource`, which reads the `item_icons` packs and then the legacy EPF. The key includes the dye colour.

## Extending

### Adding a UI panel

Make new panels from `CenteredModalPanel` and the primitives, not from a legacy control file. Brigid must run without the legacy data in the future, so a new panel must not read it.

1. Derive a class from `CenteredModalPanel` in the correct folder of `Brigid/Controls/`. Give the title and the size to the base constructor, with `useTheme: true` for a themed panel. Construct a themed panel after the asset packs load.
2. Lay out the content inside `ContentBounds`. Add actions to the bottom bar with `AddBottomBarButton`. Give each child a `Name`.
3. Add the panel as a child of the world screen's `Root`, and show it with `Show()` and hide it with `Hide()`. `CenteredModalPanel` puts itself on the control stack and handles Escape.
4. Subscribe to the `ConnectionManager` events that it needs in `WorldScreen.Wiring.cs`.

```csharp
public sealed class ExamplePopup : CenteredModalPanel
{
    public ExamplePopup()
        : base("Example", 300, 120, useTheme: true)
    {
        var message = new UILabel
        {
            Name = "message",
            Text = "Hello.",
            X = ContentBounds.X,
            Y = ContentBounds.Y,
            Width = ContentBounds.Width,
            Height = ContentBounds.Height
        };
        message.SetForegroundRole(ThemeRoles.Text);
        AddChild(message);

        AddBottomBarButton("OK", 64, Hide, "ok");
    }
}
```

`OkPopupMessageControl` is a complete small example. Use `PrefabPanel` only to change a panel that already uses a legacy control file. [controlFileList.txt](../controlFileList.txt) lists those files.

### Adding a packet handler

DALib defines the retail packets: the server packets in `DALib.Networking.Packets.Server`, the client packets in `DALib.Networking.Packets.Client`, and the opcodes in `DALib.Networking.Wire`. To make the client react to a server packet:

1. In `ConnectionManager`, write a handler that casts the packet and raises an event:

   ```csharp
   private void HandleLightLevel(IServerPacket p) => OnLightLevel?.Invoke((Server.LightLevelPacket)p);

   public event Action<Server.LightLevelPacket>? OnLightLevel;
   ```

2. Register the handler in `IndexHandlers()`:

   ```csharp
   PacketHandlers[(byte)ServerOpcode.LightLevel] = HandleLightLevel;
   ```

3. Subscribe to the event in `WorldScreen.Wiring.cs`, and update `WorldState` or the UI there.

`ConnectionManager.ProcessPackets` calls the handlers on the game loop. When a handler throws, `ConnectionManager` writes the exception to `NoticeDebugLog`, and the client continues.

To send a packet, add a method to `ConnectionManager`:

```csharp
public void Turn(Direction direction) => SendIfWorld(new Cli.TurnPacket { Direction = ToDalibDirection(direction) });
```

When the retail protocol has a packet that DALib does not have, add it to DALib. Do not put Hybrasyl divergence in DALib: a Hybrasyl value of a retail field goes in `HybrasylExtensions.cs`. A new Hybrasyl message goes in the extension dialect of `Hybrasyl.Protocol`, with an opcode from its registry (0x0100 and up). `ConnectionManager.ProcessExtensionPackets` receives the extension packets.

### Adding a hotkey

1. Add a value to `CommandId` (`Brigid/Systems/Keybinds/CommandId.cs`).
2. Add the default key to `Keybinds.Defaults`, with the `KeybindContext` (`WorldHud` for a world hotkey). Add a macOS key to `MacDefaults` only when the key must be different on macOS.
3. Add the command to `KeybindCatalog`, so that Options → Keybinds shows it. A test makes sure that the catalog and the defaults agree.
4. Test the key in `WorldScreen.OnRootKeyDown` with `Keybinds.Matches(e, CommandId.Xxx)`, and set `e.Handled = true`.
