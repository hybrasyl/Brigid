#region
using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Brigid.Definitions;
using Brigid.Networking;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Systems.Keybinds;

/// <summary>
///     Central keybinding registry and resolver. Handlers ask <see cref="Matches" />/<see cref="Resolve" />
///     (discrete key events) instead of testing literal <see cref="Keys" /> values, so every bound action
///     honours user overrides and the per-OS <see cref="ChordMods.Meta" /> resolution. <see cref="IsHeld" />
///     is a poll-time variant reserved for a future fluid-movement model (no consumer yet).
///     <para>
///         Defaults live in code (<see cref="Defaults" />); <see cref="Load" /> merges overrides-only from
///         <c>keybinds.json</c>. The WorldHud and editor hotkeys resolve through <see cref="Matches" /> (Phases
///         B3/B4); the rebinding UI (Track C) will edit overrides via <see cref="SetBinding" />.
///     </para>
/// </summary>
public static class Keybinds
{
    public const string FILE_NAME = "keybinds.json";

    private static readonly Lock Gate = new();

    //user overrides (command → binding); empty = pure defaults. Guarded by Gate.
    private static readonly Dictionary<CommandId, KeyBinding> Overrides = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Default binding + context for every command. The single source of truth for out-of-box chords.</summary>
    public static readonly FrozenDictionary<CommandId, KeybindDefault> Defaults = new Dictionary<CommandId, KeybindDefault>
    {
        // ── TextEditing (UITextBox) ── primary-modifier actions bind to Meta (Ctrl on Win/Linux, Cmd on
        // macOS). On Windows Meta≡Ctrl, so these are byte-for-byte the current chords.
        [CommandId.Editor_Copy]      = new(new KeyChord(Keys.C, ChordMods.Meta), KeybindContext.TextEditing),
        [CommandId.Editor_Cut]       = new(new KeyChord(Keys.X, ChordMods.Meta), KeybindContext.TextEditing),
        [CommandId.Editor_Paste]     = new(new KeyChord(Keys.V, ChordMods.Meta), KeybindContext.TextEditing),
        [CommandId.Editor_Undo]      = new(new KeyChord(Keys.Z, ChordMods.Meta), KeybindContext.TextEditing),

        //redo has two historical chords on the same OS: Ctrl+Shift+Z (primary, → Cmd+Shift+Z on macOS) and
        //literal Ctrl+Y (secondary). Both must resolve to redo for parity with the live editor handler.
        [CommandId.Editor_Redo]      = new(new KeyChord(Keys.Z, ChordMods.Meta | ChordMods.Shift), new KeyChord(Keys.Y, ChordMods.Ctrl), KeybindContext.TextEditing),

        //select-all in the editor is Meta+Shift+A: on Windows that is the current Ctrl+Shift+A; Meta+A is
        //deliberately avoided in this context because literal Ctrl+A is line-start (below), and folding
        //select-all onto Meta+A would collide with it on Windows.
        [CommandId.Editor_SelectAll] = new(new KeyChord(Keys.A, ChordMods.Meta | ChordMods.Shift), KeybindContext.TextEditing),

        //readline carve-out: literal Ctrl on every OS (also native macOS text-field behavior).
        [CommandId.Editor_LineStart] = new(new KeyChord(Keys.A, ChordMods.Ctrl), KeybindContext.TextEditing),
        [CommandId.Editor_LineEnd]   = new(new KeyChord(Keys.E, ChordMods.Ctrl), KeybindContext.TextEditing),

        // ── ReadView (UILabel, read-only) ── no line-start binding here, so Ctrl+A is free for select-all
        // (matches the live UILabel handler: select-all on plain Ctrl+A, → Cmd+A on macOS).
        [CommandId.Read_Copy]        = new(new KeyChord(Keys.C, ChordMods.Meta), KeybindContext.ReadView),
        [CommandId.Read_SelectAll]   = new(new KeyChord(Keys.A, ChordMods.Meta), KeybindContext.ReadView),

        // ── WorldHud (WorldScreen.OnRootKeyDown) ── plain physical chords (no primary modifier unless the
        // live handler already required one). These bind byte-for-byte to the current hotkeys; the handler
        // resolves them via Matches, so extra modifiers no longer fall through (a deliberate tightening — the
        // affected combos have no intended function). Shift is a real part of the shout/whisper/scroll chords.
        [CommandId.World_CycleWindowSize]     = new(new KeyChord(Keys.Enter, ChordMods.Alt), KeybindContext.WorldHud),
        [CommandId.World_ChatFocus]           = new(new KeyChord(Keys.Enter), KeybindContext.WorldHud),
        [CommandId.World_TogglePauseMenu]     = new(new KeyChord(Keys.Q), KeybindContext.WorldHud),
        [CommandId.World_ToggleBoard]         = new(new KeyChord(Keys.W), KeybindContext.WorldHud),
        [CommandId.World_ToggleWorldList]     = new(new KeyChord(Keys.E), KeybindContext.WorldHud),
        [CommandId.World_ToggleSocialStatus]  = new(new KeyChord(Keys.R), KeybindContext.WorldHud),
        [CommandId.World_Assail]              = new(new KeyChord(Keys.Space), KeybindContext.WorldHud),
        [CommandId.World_Shout]               = new(new KeyChord(Keys.D1, ChordMods.Shift), KeybindContext.WorldHud),
        [CommandId.World_Whisper]             = new(new KeyChord(Keys.OemQuotes, ChordMods.Shift), KeybindContext.WorldHud),
        [CommandId.World_ToggleTabMap]        = new(new KeyChord(Keys.Tab), KeybindContext.WorldHud),
        [CommandId.World_TabMapZoomIn]        = new(new KeyChord(Keys.PageUp), KeybindContext.WorldHud),
        [CommandId.World_TabMapZoomOut]       = new(new KeyChord(Keys.PageDown), KeybindContext.WorldHud),
        [CommandId.World_HelpMerchant]        = new(new KeyChord(Keys.F1), KeybindContext.WorldHud),
        [CommandId.World_MacroMenu]           = new(new KeyChord(Keys.F3), KeybindContext.WorldHud),
        [CommandId.World_Settings]            = new(new KeyChord(Keys.F4), KeybindContext.WorldHud),
        [CommandId.World_Refresh]             = new(new KeyChord(Keys.F5), KeybindContext.WorldHud),
        [CommandId.World_BoardList]           = new(new KeyChord(Keys.F7), KeybindContext.WorldHud),
        [CommandId.World_IgnoreList]          = new(new KeyChord(Keys.F9), KeybindContext.WorldHud),
        [CommandId.World_FriendsList]         = new(new KeyChord(Keys.F10), KeybindContext.WorldHud),
        [CommandId.World_SwapHudLayout]       = new(new KeyChord(Keys.OemQuestion), KeybindContext.WorldHud),
        [CommandId.World_UnequipWeaponShield] = new(new KeyChord(Keys.OemTilde), KeybindContext.WorldHud),
        [CommandId.World_GroupHighlight]      = new(new KeyChord(Keys.J), KeybindContext.WorldHud),
        [CommandId.World_PickupItem]          = new(new KeyChord(Keys.B), KeybindContext.WorldHud),
        [CommandId.World_TownMap]             = new(new KeyChord(Keys.T), KeybindContext.WorldHud),
        [CommandId.World_GroupPanel]          = new(new KeyChord(Keys.Y), KeybindContext.WorldHud),
        [CommandId.World_ChatScrollUp]        = new(new KeyChord(Keys.Up, ChordMods.Shift), KeybindContext.WorldHud),
        [CommandId.World_ChatScrollDown]      = new(new KeyChord(Keys.Down, ChordMods.Shift), KeybindContext.WorldHud),

        // ── Movement ── arrow (primary) + Z/X/C/V (secondary), both bare — byte-for-byte the old direction
        // switch (Up/C, Right/V, Down/X, Left/Z), now exact-chord and rebindable. The bare arrows carry no
        // Shift, so they never collide with the Shift-arrow chat-scroll chords above.
        [CommandId.Move_Up]    = new(new KeyChord(Keys.Up), new KeyChord(Keys.C), KeybindContext.WorldHud),
        [CommandId.Move_Right] = new(new KeyChord(Keys.Right), new KeyChord(Keys.V), KeybindContext.WorldHud),
        [CommandId.Move_Down]  = new(new KeyChord(Keys.Down), new KeyChord(Keys.X), KeybindContext.WorldHud),
        [CommandId.Move_Left]  = new(new KeyChord(Keys.Left), new KeyChord(Keys.Z), KeybindContext.WorldHud)
    }.ToFrozenDictionary();

    /// <summary>The binding currently bound to <paramref name="id" /> (override if set, else default).</summary>
    public static KeyBinding Effective(CommandId id)
    {
        using var scope = Gate.EnterScope();

        return Overrides.TryGetValue(id, out var binding) ? binding : Defaults[id].Binding;
    }

    /// <summary>True if <paramref name="e" /> hits either chord of the binding for <paramref name="id" />.</summary>
    public static bool Matches(KeyEvent e, CommandId id)
    {
        var binding = Effective(id);

        return ChordMatches(binding.Primary, e) || (binding.Secondary is { } secondary && ChordMatches(secondary, e));
    }

    /// <summary>
    ///     The command bound to <paramref name="e" /> within <paramref name="context" />, or null. Bindings
    ///     never overlap within a context by design (enforced by test), so match order is irrelevant.
    /// </summary>
    public static CommandId? Resolve(KeyEvent e, KeybindContext context)
    {
        foreach (var (id, def) in Defaults)
        {
            if (def.Context != context)
                continue;

            if (Matches(e, id))
                return id;
        }

        return null;
    }

    /// <summary>
    ///     Poll-time held check for a bound chord: its key is down and the live modifier state matches.
    ///     Reserved for a future polled / fluid-movement model — B4 kept movement keydown-driven and resolves
    ///     it via <see cref="Matches" />, so nothing consumes this yet.
    /// </summary>
    public static bool IsHeld(CommandId id)
    {
        var binding = Effective(id);

        return ChordHeld(binding.Primary) || (binding.Secondary is { } secondary && ChordHeld(secondary));
    }

    private static bool ChordMatches(KeyChord chord, KeyEvent e) =>
        e.Key == chord.Key && ModsMatch(chord, e.Shift, e.Alt, e.Ctrl, e.Meta);

    private static bool ChordHeld(KeyChord chord)
    {
        if (!InputBuffer.IsKeyHeld(chord.Key))
            return false;

        var mods = InputBuffer.CurrentModifiers;

        return ModsMatch(
            chord,
            (mods & KeyModifiers.Shift) != 0,
            (mods & KeyModifiers.Alt) != 0,
            (mods & KeyModifiers.Ctrl) != 0,
            (mods & KeyModifiers.Meta) != 0);
    }

    /// <summary>
    ///     Modifier comparison shared by <see cref="ChordMatches" /> and <see cref="ChordHeld" />. Shift/Alt
    ///     match exactly. The primary modifier has two channels: a <see cref="ChordMods.Meta" /> chord matches
    ///     when Meta is down (Ctrl on Win/Linux, Cmd on macOS); a literal <see cref="ChordMods.Ctrl" /> chord
    ///     matches when Ctrl is down; a chord with neither requires no primary modifier at all.
    ///     <para>
    ///         On Windows Ctrl and Meta are the same physical key (both bits set together), so a Meta chord
    ///         must ignore the shadow Ctrl bit rather than demand an exact set. The same leniency is applied on
    ///         macOS where the keys are distinct — intentional asymmetry: an obscure Cmd+Ctrl+C still counts as
    ///         Copy. That's harmless, and keeping one branch keeps the Windows entanglement correct.
    ///     </para>
    /// </summary>
    private static bool ModsMatch(KeyChord chord, bool shift, bool alt, bool ctrl, bool meta)
    {
        if (chord.HasShift != shift || chord.HasAlt != alt)
            return false;

        if (chord.HasMeta)
            return meta;

        if (chord.HasCtrl)
            return ctrl;

        return !meta && !ctrl;
    }

    //keys handled by a modifier-agnostic literal switch in WorldScreen.OnRootKeyDown (the HUD tab switch,
    //A/S/D/F/G/H) that fires regardless of modifiers and returns before most resolver checks — so a WorldHud
    //command rebound onto one would be silently shadowed. The rebinder rejects these via IsShadowed. (This is
    //deliberately conservative: a couple of commands whose Matches sits above the tab switch could technically
    //use them, but blocking the whole set keeps the rule simple and can never produce a dead binding.)
    private static readonly FrozenSet<Keys> WorldHudShadowKeys =
        new[] { Keys.A, Keys.S, Keys.D, Keys.F, Keys.G, Keys.H }.ToFrozenSet();

    /// <summary>
    ///     Keys the WorldHud rebinder refuses (see <see cref="IsShadowed" />). A hand-maintained mirror of
    ///     <c>WorldScreen.HudTabHotkeys</c> — a test cross-asserts the two so they can't silently drift.
    /// </summary>
    public static IReadOnlySet<Keys> ReservedWorldHudKeys => WorldHudShadowKeys;

    /// <summary>
    ///     True if binding <paramref name="chord" /> in <paramref name="context" /> would be swallowed by a
    ///     literal (non-resolver) handler that runs first, making the binding dead. The rebinding UI uses this
    ///     to refuse such a chord. Currently only the WorldHud tab-switch keys are reserved; the softer
    ///     slot/emote number-row case is flagged by <see cref="UsesContestedNumberRow" /> instead.
    /// </summary>
    public static bool IsShadowed(KeybindContext context, KeyChord chord) =>
        context == KeybindContext.WorldHud && WorldHudShadowKeys.Contains(chord.Key);

    //the number row is scanned by two literal handlers in OnRootKeyDown — the slot hotkeys (1-9/0/-/=, any
    //modifier) and the emote hotkeys (Ctrl/Alt + number) — both of which dispatch before the movement and
    //chat-scroll resolver checks. A command routed after them bound onto a number-row key is therefore
    //overridden while a HUD panel is open. This is conditional (depends on HUD state and the command's dispatch
    //position — an earlier-dispatched command like Shout=Shift+1 still works), so it warns rather than blocks.
    private static readonly FrozenSet<Keys> WorldHudNumberRowKeys = new[]
    {
        Keys.D0, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9,
        Keys.OemMinus, Keys.OemPlus
    }.ToFrozenSet();

    /// <summary>
    ///     True if <paramref name="chord" /> uses a number-row key contested by the WorldHud slot/emote hotkeys
    ///     (see <see cref="WorldHudNumberRowKeys" />). Unlike <see cref="IsShadowed" /> this is a soft signal:
    ///     the collision only bites while a HUD panel is open and only for commands dispatched after those
    ///     handlers, so the rebinder warns and still allows the bind rather than refusing it.
    /// </summary>
    public static bool UsesContestedNumberRow(KeybindContext context, KeyChord chord) =>
        context == KeybindContext.WorldHud && WorldHudNumberRowKeys.Contains(chord.Key);

    //bare modifier keys — never a chord on their own; the rebinder ignores them and waits for a real key.
    private static readonly FrozenSet<Keys> ModifierKeys = new[]
    {
        Keys.LeftShift, Keys.RightShift, Keys.LeftControl, Keys.RightControl,
        Keys.LeftAlt, Keys.RightAlt, Keys.LeftWindows, Keys.RightWindows
    }.ToFrozenSet();

    /// <summary>
    ///     Converts a captured key event into the chord it represents, or null when the key is a bare modifier
    ///     (the rebinder ignores pure-modifier presses and waits for a real key). Normalises the primary
    ///     modifier: a press reporting <see cref="KeyModifiers.Meta" /> (Windows Ctrl, macOS Cmd) records the
    ///     portable <see cref="ChordMods.Meta" />, while a macOS physical-Ctrl press (Ctrl without Meta) records
    ///     literal <see cref="ChordMods.Ctrl" />. The decode counterpart to <see cref="EventForChord" />.
    /// </summary>
    public static KeyChord? ChordFromEvent(KeyEvent e)
    {
        if (e.Key == Keys.None || ModifierKeys.Contains(e.Key))
            return null;

        var mods = ChordMods.None;

        if (e.Shift)
            mods |= ChordMods.Shift;

        if (e.Alt)
            mods |= ChordMods.Alt;

        if (e.Meta)
            mods |= ChordMods.Meta;
        else if (e.Ctrl)
            mods |= ChordMods.Ctrl;

        return new KeyChord(e.Key, mods);
    }

    /// <summary>
    ///     Commands in <paramref name="context" /> — other than <paramref name="excluding" /> — whose effective
    ///     binding fires on the same physical keystroke as <paramref name="candidate" />. Built by synthesising
    ///     the event <paramref name="candidate" /> produces on the current OS (mirroring
    ///     <c>InputBuffer.TranslateSdlMods</c>), so the Windows <see cref="ChordMods.Meta" />≡Ctrl overlap is
    ///     honoured: a Meta chord and a literal-Ctrl chord on the same key conflict there but not on macOS.
    /// </summary>
    public static IReadOnlyList<CommandId> FindConflicts(CommandId excluding, KeyChord candidate, KeybindContext context)
    {
        var probe = EventForChord(candidate);
        var conflicts = new List<CommandId>();

        foreach (var (id, def) in Defaults)
        {
            if (id == excluding || def.Context != context)
                continue;

            if (Matches(probe, id))
                conflicts.Add(id);
        }

        return conflicts;
    }

    //the KeyDownEvent a chord would produce on the current OS. Non-macOS collapses Meta and Ctrl onto the same
    //physical key (both bits set), matching how InputBuffer reports a Ctrl press.
    private static KeyEvent EventForChord(KeyChord chord)
    {
        var mods = KeyModifiers.None;

        if (chord.HasShift)
            mods |= KeyModifiers.Shift;

        if (chord.HasAlt)
            mods |= KeyModifiers.Alt;

        if (OperatingSystem.IsMacOS())
        {
            if (chord.HasMeta)
                mods |= KeyModifiers.Meta;

            if (chord.HasCtrl)
                mods |= KeyModifiers.Ctrl;
        } else if (chord.HasMeta || chord.HasCtrl)
            mods |= KeyModifiers.Ctrl | KeyModifiers.Meta;

        return new KeyDownEvent { Key = chord.Key, Modifiers = mods };
    }

    /// <summary>True if <paramref name="id" />'s default binding has a secondary chord (so the rebinder shows a
    ///     second field for it). Movement commands do; the single-chord world hotkeys don't.</summary>
    public static bool SupportsSecondary(CommandId id) => Defaults[id].Binding.Secondary is not null;

    /// <summary>
    ///     Rebinds one slot of <paramref name="id" /> to <paramref name="chord" />, preserving the other slot.
    ///     Unlike <see cref="SetBinding(CommandId,KeyChord)" /> (which drops the secondary), this edits a single
    ///     field of a two-chord binding in place. Persist separately via <see cref="Save" />.
    /// </summary>
    public static void SetChord(CommandId id, ChordSlot slot, KeyChord chord)
    {
        using var scope = Gate.EnterScope();

        var current = Overrides.TryGetValue(id, out var existing) ? existing : Defaults[id].Binding;

        Overrides[id] = slot == ChordSlot.Primary
            ? current with { Primary = chord }
            : current with { Secondary = chord };
    }

    /// <summary>Sets (or replaces) a user override to a single-chord binding. Persist via <see cref="Save" />.</summary>
    public static void SetBinding(CommandId id, KeyChord primary) => SetBinding(id, new KeyBinding(primary));

    /// <summary>Sets (or replaces) a user override. Persist separately via <see cref="Save" />.</summary>
    public static void SetBinding(CommandId id, KeyBinding binding)
    {
        using var scope = Gate.EnterScope();
        Overrides[id] = binding;
    }

    /// <summary>Clears the override for one command, restoring its default.</summary>
    public static void ResetToDefault(CommandId id)
    {
        using var scope = Gate.EnterScope();
        Overrides.Remove(id);
    }

    /// <summary>Clears all overrides, restoring every default.</summary>
    public static void ResetAll()
    {
        using var scope = Gate.EnterScope();
        Overrides.Clear();
    }

    /// <summary>
    ///     Loads overrides-only from <c>keybinds.json</c>. Missing/corrupt file → pure defaults. Unknown
    ///     command names (a renamed/removed <see cref="CommandId" /> or a hand-edited typo) are skipped
    ///     per-entry so one stale key doesn't discard every valid override.
    /// </summary>
    public static void Load()
    {
        var path = Path.Combine(GlobalSettings.DataPath, FILE_NAME);

        if (!File.Exists(path))
            return;

        //string keys so an unknown command name can't fault the whole deserialize.
        Dictionary<string, KeyBinding>? loaded;

        try
        {
            loaded = JsonSerializer.Deserialize<Dictionary<string, KeyBinding>>(File.ReadAllText(path), JsonOptions);
        } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            //Load() runs during startup (FinishAssetInitialization) with no enclosing handler, so a locked or
            //permission-denied file must degrade to defaults rather than abort the launch.
            NoticeDebugLog.Write($"[Keybinds] failed to load {FILE_NAME}: {ex.Message}");

            return;
        }

        if (loaded is null)
            return;

        var skipped = 0;

        using var scope = Gate.EnterScope();
        Overrides.Clear();

        foreach (var (name, binding) in loaded)
        {
            if (Enum.TryParse<CommandId>(name, out var id))
                Overrides[id] = binding;
            else
                skipped++;
        }

        if (skipped > 0)
            NoticeDebugLog.Write($"[Keybinds] skipped {skipped} unknown command(s) in {FILE_NAME}");
    }

    /// <summary>Writes overrides-only to <c>keybinds.json</c>. Defaults are never serialized.</summary>
    public static void Save()
    {
        Dictionary<CommandId, KeyBinding> snapshot;

        using (Gate.EnterScope())
            snapshot = new Dictionary<CommandId, KeyBinding>(Overrides);

        var path = Path.Combine(GlobalSettings.DataPath, FILE_NAME);

        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonOptions));
        } catch (IOException ex)
        {
            NoticeDebugLog.Write($"[Keybinds] failed to save {FILE_NAME}: {ex.Message}");
        }
    }
}

/// <summary>A command's out-of-box binding and the context it resolves in.</summary>
public readonly record struct KeybindDefault(KeyBinding Binding, KeybindContext Context)
{
    public KeybindDefault(KeyChord primary, KeybindContext context) : this(new KeyBinding(primary), context) { }

    public KeybindDefault(KeyChord primary, KeyChord secondary, KeybindContext context)
        : this(new KeyBinding(primary, secondary), context) { }

    /// <summary>The primary default chord (convenience for callers that don't care about the secondary).</summary>
    public KeyChord Chord => Binding.Primary;
}
