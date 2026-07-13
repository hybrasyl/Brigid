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
///     (discrete key events) or <see cref="IsHeld" /> (poll-time movement) instead of testing literal
///     <see cref="Keys" /> values, so every bound action honours user overrides and the per-OS
///     <see cref="ChordMods.Meta" /> resolution.
///     <para>
///         Defaults live in code (<see cref="Defaults" />); <see cref="Load" /> merges overrides-only from
///         <c>keybinds.json</c>. Nothing consumes the resolver in the B1 foundation — migrations wire it in
///         later phases.
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
        [CommandId.Read_SelectAll]   = new(new KeyChord(Keys.A, ChordMods.Meta), KeybindContext.ReadView)
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
    ///     Poll-time held check for movement/hold commands (B4): a bound chord's key is down and the live
    ///     modifier state matches.
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
        } catch (Exception ex) when (ex is IOException or JsonException)
        {
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
