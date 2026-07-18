#region
using System.Collections.Frozen;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Systems.Keybinds;

/// <summary>
///     Display metadata for the rebinding UI (Track C): the ordered, human-labelled list of commands the
///     Keybinds tab exposes, grouped into <see cref="KeybindSection" />s, plus chord/binding → text
///     formatting. This is purely presentational — the resolver (<see cref="Keybinds" />) neither knows nor
///     needs any of it, so display concerns stay out of the dispatch core.
///     <para>
///         Lists the <see cref="KeybindContext.WorldHud" /> commands (world hotkeys + movement) and the
///         <see cref="KeybindContext.TextEditing" /> editor commands. The <see cref="KeybindContext.ReadView" />
///         commands are deliberately NOT listed — they are consumed but not independently rebindable. A test
///         asserts the catalog and the resolver's exposed defaults (WorldHud ∪ TextEditing) stay in one-to-one
///         sync, so a new command can't be silently omitted from the rebinder.
///     </para>
/// </summary>
public static class KeybindCatalog
{
    /// <summary>A display grouping for the rebind list. Independent of <see cref="KeybindContext" />, which
    ///     is about focus routing, not presentation.</summary>
    public enum KeybindSection
    {
        Movement,
        Panels,
        Chat,
        FunctionKeys,
        Actions,
        TextEditing
    }

    /// <summary>One row in the rebind list: the command, its label, and the section it renders under.</summary>
    public readonly record struct KeybindEntry(CommandId Id, string DisplayName, KeybindSection Section);

    /// <summary>Sections in display order.</summary>
    public static readonly IReadOnlyList<KeybindSection> SectionOrder =
    [
        KeybindSection.Movement,
        KeybindSection.Panels,
        KeybindSection.Chat,
        KeybindSection.Actions,
        KeybindSection.FunctionKeys,
        KeybindSection.TextEditing
    ];

    /// <summary>Every rebindable command in display order, grouped by section (see <see cref="SectionOrder" />).</summary>
    public static readonly IReadOnlyList<KeybindEntry> Entries =
    [
        new(CommandId.Move_Up, "Move Up", KeybindSection.Movement),
        new(CommandId.Move_Down, "Move Down", KeybindSection.Movement),
        new(CommandId.Move_Left, "Move Left", KeybindSection.Movement),
        new(CommandId.Move_Right, "Move Right", KeybindSection.Movement),

        new(CommandId.World_TogglePauseMenu, "Menu / Pause", KeybindSection.Panels),
        new(CommandId.World_ToggleBoard, "Bulletin Board", KeybindSection.Panels),
        new(CommandId.World_ToggleWorldList, "World List", KeybindSection.Panels),
        new(CommandId.World_ToggleSocialStatus, "Social Status", KeybindSection.Panels),
        new(CommandId.World_ToggleTabMap, "Map Overlay", KeybindSection.Panels),
        new(CommandId.World_TownMap, "Town Map", KeybindSection.Panels),
        new(CommandId.World_GroupPanel, "Group Panel", KeybindSection.Panels),
        new(CommandId.World_SwapHudLayout, "Swap HUD Layout", KeybindSection.Panels),

        new(CommandId.World_ChatFocus, "Chat", KeybindSection.Chat),
        new(CommandId.World_Shout, "Shout", KeybindSection.Chat),
        new(CommandId.World_Whisper, "Whisper", KeybindSection.Chat),
        new(CommandId.World_ChatScrollUp, "Scroll Chat Up", KeybindSection.Chat),
        new(CommandId.World_ChatScrollDown, "Scroll Chat Down", KeybindSection.Chat),

        new(CommandId.World_Assail, "Assail", KeybindSection.Actions),
        new(CommandId.World_PickupItem, "Pick Up Item", KeybindSection.Actions),
        new(CommandId.World_GroupHighlight, "Highlight Group", KeybindSection.Actions),
        new(CommandId.World_UnequipWeaponShield, "Unequip Weapon/Shield", KeybindSection.Actions),
        new(CommandId.World_CycleWindowSize, "Cycle Window Size", KeybindSection.Actions),
        new(CommandId.World_TabMapZoomIn, "Map Zoom In", KeybindSection.Actions),
        new(CommandId.World_TabMapZoomOut, "Map Zoom Out", KeybindSection.Actions),

        new(CommandId.World_HelpMerchant, "Help", KeybindSection.FunctionKeys),
        new(CommandId.World_MacroMenu, "Macros", KeybindSection.FunctionKeys),
        new(CommandId.World_Settings, "Settings", KeybindSection.FunctionKeys),
        new(CommandId.World_Refresh, "Refresh", KeybindSection.FunctionKeys),
        new(CommandId.World_BoardList, "Board List", KeybindSection.FunctionKeys),
        new(CommandId.World_KeybindMenu, "Keybinds", KeybindSection.FunctionKeys),
        new(CommandId.World_IgnoreList, "Ignore List", KeybindSection.FunctionKeys),
        new(CommandId.World_FriendsList, "Friends List", KeybindSection.FunctionKeys),

        new(CommandId.Editor_SelectAll, "Select All", KeybindSection.TextEditing),
        new(CommandId.Editor_Copy, "Copy", KeybindSection.TextEditing),
        new(CommandId.Editor_Cut, "Cut", KeybindSection.TextEditing),
        new(CommandId.Editor_Paste, "Paste", KeybindSection.TextEditing),
        new(CommandId.Editor_Undo, "Undo", KeybindSection.TextEditing),
        new(CommandId.Editor_Redo, "Redo", KeybindSection.TextEditing),
        new(CommandId.Editor_LineStart, "Line Start", KeybindSection.TextEditing),
        new(CommandId.Editor_LineEnd, "Line End", KeybindSection.TextEditing)
    ];

    private static readonly FrozenDictionary<CommandId, KeybindEntry> ById =
        Entries.ToFrozenDictionary(static e => e.Id);

    /// <summary>The catalog entry for a command, or null if it isn't exposed by the rebinder.</summary>
    public static KeybindEntry? Entry(CommandId id) => ById.TryGetValue(id, out var e) ? e : null;

    /// <summary>Human label for a display section.</summary>
    public static string SectionLabel(KeybindSection section) => section switch
    {
        KeybindSection.Movement     => "Movement",
        KeybindSection.Panels       => "Panels & Windows",
        KeybindSection.Chat         => "Chat",
        KeybindSection.Actions      => "Actions",
        KeybindSection.FunctionKeys => "Function Keys",
        KeybindSection.TextEditing  => "Text Editing",
        _                           => section.ToString()
    };

    /// <summary>
    ///     Formats a binding for the chord button: the primary chord, plus <c>" / "</c> and the secondary when
    ///     the command has an alternate chord (movement's zxcv, redo's Ctrl+Y).
    /// </summary>
    public static string Format(KeyBinding binding) =>
        binding.Secondary is { } secondary ? $"{Format(binding.Primary)} / {Format(secondary)}" : Format(binding.Primary);

    /// <summary>Formats one chord, e.g. <c>Ctrl+Shift+Z</c>, <c>Shift+1</c>, <c>Alt+Enter</c>, <c>Up</c>.</summary>
    public static string Format(KeyChord chord)
    {
        //modifier order matches the platform convention (Ctrl, Shift, Alt); Meta renders as the primary
        //modifier's OS name so a rebind reads the way the user pressed it.
        var parts = new List<string>(4);

        if (chord.HasMeta)
            parts.Add(MetaName);

        if (chord.HasCtrl)
            parts.Add("Ctrl");

        if (chord.HasShift)
            parts.Add("Shift");

        if (chord.HasAlt)
            parts.Add("Alt");

        parts.Add(KeyName(chord.Key));

        return string.Join("+", parts);
    }

    //Meta is the platform primary modifier: Cmd on macOS, Ctrl elsewhere (matches ChordMods.Meta resolution).
    private static string MetaName => OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl";

    /// <summary>A readable name for a physical key. Falls back to the enum name for anything unmapped.</summary>
    public static string KeyName(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        Keys.OemMinus             => "-",
        Keys.OemPlus              => "=",
        Keys.OemQuestion          => "/",
        Keys.OemTilde             => "`",
        Keys.OemQuotes            => "'",
        Keys.OemComma             => ",",
        Keys.OemPeriod            => ".",
        Keys.OemSemicolon         => ";",
        Keys.OemOpenBrackets      => "[",
        Keys.OemCloseBrackets     => "]",
        Keys.OemPipe              => "\\",
        Keys.Up                   => "Up",
        Keys.Down                 => "Down",
        Keys.Left                 => "Left",
        Keys.Right                => "Right",
        Keys.PageUp               => "Page Up",
        Keys.PageDown             => "Page Down",
        Keys.Space                => "Space",
        _                         => key.ToString()
    };
}
