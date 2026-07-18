#region
using System.Text.Json.Serialization;
using Brigid.Definitions;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Systems.Keybinds;

/// <summary>
///     Modifier flags for a bound chord. Distinct from <see cref="KeyModifiers" /> (the live event flags):
///     a chord declares intent, an event reports state.
///     <list type="bullet">
///         <item><see cref="Meta" /> is the platform primary modifier — Ctrl on Windows/Linux, Cmd on
///         macOS. Bind primary-modifier actions (clipboard, undo, select-all) to <see cref="Meta" /> and
///         they resolve per-OS for free.</item>
///         <item><see cref="Ctrl" /> is <b>literal</b> Ctrl on every OS — for chords that must stay on
///         physical Ctrl even on macOS (e.g. the redo secondary Ctrl+Y) rather than folding into
///         <see cref="Meta" />.</item>
///     </list>
///     A chord uses at most one primary modifier (<see cref="Meta" /> or <see cref="Ctrl" />), never both.
/// </summary>
[Flags]
public enum ChordMods
{
    None  = 0,
    Shift = 1,
    Alt   = 2,
    Meta  = 4,
    Ctrl  = 8
}

/// <summary>
///     An immutable key + modifier combination bound to a command. Compared against live
///     <see cref="KeyEvent" />s by <see cref="Keybinds.Matches" />, which resolves <see cref="ChordMods.Meta" />
///     to the platform primary modifier at match time.
/// </summary>
public readonly record struct KeyChord(Keys Key, ChordMods Mods = ChordMods.None)
{
    [JsonIgnore] public bool HasShift => (Mods & ChordMods.Shift) != 0;
    [JsonIgnore] public bool HasAlt => (Mods & ChordMods.Alt) != 0;
    [JsonIgnore] public bool HasMeta => (Mods & ChordMods.Meta) != 0;
    [JsonIgnore] public bool HasCtrl => (Mods & ChordMods.Ctrl) != 0;
}

/// <summary>
///     What a command is bound to: a <see cref="Primary" /> chord and an optional <see cref="Secondary" />.
///     Two slots because some actions have historically had two default chords on the same OS (e.g. redo =
///     Ctrl+Shift+Z <i>and</i> Ctrl+Y), and the rebinding UI (Track C) exposes both. A command matches if the
///     event hits <b>either</b> chord.
/// </summary>
public readonly record struct KeyBinding(KeyChord Primary, KeyChord? Secondary = null);

/// <summary>
///     Which chord of a <see cref="KeyBinding" /> a rebind targets. The Keybinds tab shows a
///     <see cref="Primary" /> field for every command and a <see cref="Secondary" /> field for commands whose
///     default has two chords (movement); a rebind edits one slot and preserves the other.
/// </summary>
public enum ChordSlot
{
    Primary,
    Secondary
}
