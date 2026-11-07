# Brigid

Brigid is the Hybrasyl team's client for Dark Ages. It is written in C# on .NET 10, with [MonoGame](https://monogame.net/) for the window and graphics and [DALib](https://github.com/eriscorp/dalib) for the Dark Ages file formats and protocol.

Brigid speaks the standard Dark Ages protocol of client version **7.41**. Its first target is the [Hybrasyl server](https://github.com/hybrasyl/server), and it also connects to the retail servers. Brigid is not compatible with Chaos-Server.

Brigid runs on Windows, macOS and Linux.

_Brigid was originally derived from [Chaos.Client](https://github.com/sichii/Chaos.Client) by Sichii._

## Contents

- [Status](#status)
- [Download](#download)
- [First run](#first-run)
- [Asset packs and themes](#asset-packs-and-themes)
- [Default keys](#default-keys)
- [Differences from the retail client](#differences-from-the-retail-client)
- [Build from source](#build-from-source)
- [Launch options](#launch-options)
- [Contributing](#contributing)
- [Related repositories](#related-repositories)
- [License](#license)

## Status

Brigid has the full lobby, login and world flow. In the world it has the HUD, inventory, skills, spells, chat, exchange, boards and mail, groups, profiles, NPC dialogs, and most popups.

Brigid also adds these features:

- A launcher screen that sets the data folder, the server and the theme. You do not edit a file before the first run.
- `.datf` asset packs that replace or add art, icons, maps, music and sounds.
- `ui_theme` packs that change the art, colours and fonts of the interface.
- Rebindable keys in Options → Keybinds.
- TrueType fonts with full Unicode, in place of the legacy bitmap fonts.
- A TLS connection to a server that supports it.
- A check for new releases at startup.

## Download

Each [release](https://github.com/hybrasyl/Brigid/releases) has these files:

| File | Platform | Notes |
| --- | --- | --- |
| `Brigid-<version>-win-x64.zip` | Windows x64 | `Brigid.exe` is signed. |
| `Brigid-<version>-osx-arm64.dmg` | macOS on Apple silicon | The app and the disk image are signed and notarized. |
| `Brigid-<version>-linux-x64.deb` | Ubuntu 24.04 or later, Debian 12 | apt installs the audio codecs. |
| `Brigid-<version>-x86_64.AppImage` | Linux x64, glibc 2.39 or later | The audio codecs are in the AppImage. |
| `Brigid-<version>-linux-x64.tar.gz` | Linux x64 | Uses the audio codecs of the system. |
| `SHA256SUMS` | All | The SHA-256 hash of each file. |

The Windows and macOS builds are self-contained. You do not install .NET.

### Linux audio

The bundled `libSDL2_mixer.so` links six system codec libraries: `libopusfile`, `libmpg123`, `libvorbisfile`, `libFLAC`, `libfluidsynth` and `libmodplug`. The `.deb` declares them, and the AppImage bundles them. On another Ubuntu or Debian release (for example Ubuntu 22.04), apt refuses the `.deb` because the codec versions are different. Use the `.tar.gz` there.

The `.tar.gz` and a source build use the codecs of the system. If the bundled mixer cannot load them, Brigid loads the mixer of the distribution. Install it with one of these commands:

```bash
sudo apt install libsdl2-mixer-2.0-0       # Debian/Ubuntu
sudo dnf install SDL2_mixer                # Fedora/RHEL
sudo pacman -S sdl2_mixer                  # Arch
```

If Brigid cannot load a mixer, it runs with no sound, and the main menu shows an alert.

## First run

Brigid needs the game data of a Dark Ages installation: the folder that holds the `.dat` archives.

1. Start Brigid. The launcher screen opens.
2. Select the data folder.
3. Select a server. The list starts with the retail server `da0.kru.com:2610`. You can add more servers.
4. Select a theme, if you have installed a `ui_theme` pack.
5. Click **Connect**.

Brigid saves these choices. To change them later, click **Config** on the main menu.

Brigid keeps its settings, caches and asset packs in one folder for each user:

| Platform | Folder |
| --- | --- |
| Windows | `%LOCALAPPDATA%\Erisco\Brigid` |
| macOS and Linux | `~/.config/Erisco/Brigid` |

## Asset packs and themes

An asset pack is a `.datf` file: a ZIP archive with a `_manifest.json`. Artists make packs with [Taliesin](https://github.com/hybrasyl/taliesin). To install a pack, put it in the `assets` folder in the folder above. Brigid reads the packs at startup.

A pack can replace art that the legacy data has, or it can add new art. When no pack has an asset, Brigid uses the legacy data. When two packs have the same asset, the pack with the higher `priority` wins.

These are the content types:

- `ability_icons`, `item_icons`, `nation_badges`, `legend_mark_icons` and `npc_portraits`
- `creature_sprites` and `static_tiles`
- `world_maps` and `town_maps`
- `music`, `sound_effects` and `ambient_sounds`
- `ui_sprite_overrides` and `ui_theme`

[assetPackList.md](assetPackList.md) gives the entry names, the manifest fields and the rules of each type. [docs/static-tiles-authoring-guide.md](docs/static-tiles-authoring-guide.md) shows how to make a `static_tiles` pack.

A `ui_theme` pack is different from the other types: only one theme is active at a time. Select the theme in the launcher, or in Options → Settings. The theme that you select before the first connection applies at once. A theme that you change later applies at the next start.

## Default keys

You can change each of these keys in Options → Keybinds.

| Key | Action |
| --- | --- |
| A, S, D, F, G, H | Inventory, skills, spells, chat, stats and tools. Shift selects the other panel (for example Shift+F shows the message history). |
| Q | Pause menu |
| W, E, R, T, Y | Boards, world list, social status, town map, group |
| Tab | Tab map. Page Up and Page Down zoom it. |
| 1 to 0, -, = | Use the slot of the current panel |
| Space | Assail |
| Enter | Chat |
| Shift+1, Shift+' | Shout, whisper |
| F1, F3, F4, F5, F7, F8, F9, F10 | Help, macros, settings, refresh, board list, keybinds, ignore list, friends |
| B, J, \` | Pick up an item, highlight the group, take off the weapon and shield |
| / | Change the HUD layout |
| Alt+Enter | Change the window size |
| Ctrl, Ctrl+Alt or Alt with 1 to 0, - | Emotes |

## Differences from the retail client

These differences are intentional:

1. **BlowKiss shows its heart.** Retail uses the khan `m` bodies, which erase the heart effect. Brigid draws the khan `b` bodies behind the aisling, so the heart stays.
2. **Event availability uses the circle list correctly.** Retail marks an event as not available from circle 6, also when the event accepts circle 6.
3. **Ground tints keep the draw order.** On retail, a character on a tinted ground tile (`gndattr.tbl`) moves in front of the foreground object that hides it. Brigid keeps the character behind it and applies the tint.
4. **Background tile animations play.** Retail ignores `gndani.tbl`.
5. **Overcoat and armor palettes with IDs of 1000 or more work.** Retail uses the default palette for them.
6. **The Tab map is a new implementation.** It looks different from retail.
7. **Each class can zoom the Tab map.** Retail lets only rogues zoom.
8. **Idle animations of items continue during an emote.** Retail stops them or plays them only in part.
9. **Inline colour codes work in all text and apply at once.**
10. **Pants show under an overcoat when the server permits it.** Retail never shows them.
11. **The Album is not available.** The Album tab in the profile is blank.
12. **Alt+Enter changes the window size.** The game area is 640×480. Each press multiplies it by the next whole number that fits the monitor, and then goes back to 1×.
13. **Swimming has no skill gate.** Retail lets a character walk on water only with the `Swimming` skill or the GM flag. Brigid lets each character swim. To use the retail rule, change `GlobalSettings.RequireSwimmingSkill` in the source to return `true`.
14. **Health bars, chants and chat bubbles on creatures use a blended height.** They sit halfway between a fixed height and the top of the visible sprite. Small sprites show them a little higher than retail, and large sprites a little lower.
15. **Snow looks different, and rain works.** Retail does nothing on a rain map.
16. **Ghosts and invisible aislings use the older retail look.** A ghost has a light blue face, and an invisible aisling shows as an outline.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build Brigid.slnx
dotnet run --project Brigid/Brigid.csproj
dotnet test Brigid.slnx
```

All dependencies come from NuGet, so you do not clone another repository first.

To build with a local copy of a dependency, set one of these properties:

| Property | Dependency | Default path |
| --- | --- | --- |
| `UseLocalDALib=true` | DALib | `../dalib/DALib/DALib.csproj` |
| `UseLocalProtocol=true` | Hybrasyl.Protocol | `../sreang/src/Hybrasyl.Protocol/Hybrasyl.Protocol.csproj` |

Give a property on the command line (`dotnet build -p:UseLocalDALib=true`), or copy [Directory.Build.local.props.example](Directory.Build.local.props.example) to `Directory.Build.local.props` and set it there. The example file also tells how to change the paths.

`Build/` holds the release packaging scripts for macOS and Linux. Write local packages to `dist/`, which git ignores.

## Launch options

A launcher such as [Epona](https://github.com/eriscorp/epona), or a script, can start Brigid with these environment variables:

| Variable | Effect |
| --- | --- |
| `DA_HOST` | The lobby host. |
| `DA_HOST_PORT` | The lobby port: 1 to 65535. Another value gives 2610. |
| `DA_ASSET_PATH` | The data folder. Brigid uses it only when it holds the `.dat` archives. |
| `DA_NO_LAUNCHER` | `1`, `true` or `yes` skips the launcher screen. |

Brigid skips the launcher screen and connects at once in two cases:

- `DA_HOST` is set, and `DA_ASSET_PATH` is a valid data folder.
- `DA_NO_LAUNCHER` is set, and a valid data folder comes from `DA_ASSET_PATH` or the saved settings. The host comes from `DA_HOST`, else the saved server.

In every other case, Brigid shows the launcher screen. `notice-debug.log`, in the folder of the executable, records the values that Brigid read and the decision that it made.

## Contributing

[docs/architecture.md](docs/architecture.md) describes the projects, the draw order, the UI and input systems, the renderers, and how to add a panel, a packet handler or a hotkey.

These files are also useful:

- [assetPackList.md](assetPackList.md): the asset pack types.
- [controlFileList.txt](controlFileList.txt): the legacy control files in the data archives, and the classes that use them.

Open pull requests against `main`.

## Related repositories

- [hybrasyl/server](https://github.com/hybrasyl/server): the Hybrasyl server.
- [eriscorp/dalib](https://github.com/eriscorp/dalib): the Dark Ages file format and protocol library.
- [hybrasyl/taliesin](https://github.com/hybrasyl/taliesin): the tool that makes `.datf` asset packs.
- [hybrasyl/creidhne](https://github.com/hybrasyl/creidhne): the Hybrasyl world data editor.
- [eriscorp/epona](https://github.com/eriscorp/epona): a launcher for the client and the server.

## License

Brigid is licensed under the GNU Affero General Public License, version 3 or later. See [LICENSE](LICENSE).
