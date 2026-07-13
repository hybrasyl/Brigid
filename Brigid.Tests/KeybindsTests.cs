using Brigid;
using Brigid.Definitions;
using Brigid.Systems.Keybinds;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     B1 foundation tests: the default bindings must resolve byte-for-byte to the client's current
///     Windows chords, the per-OS <see cref="ChordMods.Meta" /> split must behave, and overrides must
///     round-trip through <c>keybinds.json</c>. Every test calls <see cref="Keybinds.ResetAll" /> first
///     because the registry is process-global static.
/// </summary>
public sealed class KeybindsTests
{
    //simulates a Windows/Linux key event: pressing Ctrl sets BOTH Ctrl and Meta (same physical key),
    //mirroring InputBuffer.TranslateSdlMods on non-macOS.
    private static KeyDownEvent WinKey(Keys key, bool ctrl = false, bool shift = false, bool alt = false)
    {
        var mods = KeyModifiers.None;

        if (ctrl)
            mods |= KeyModifiers.Ctrl | KeyModifiers.Meta;

        if (shift)
            mods |= KeyModifiers.Shift;

        if (alt)
            mods |= KeyModifiers.Alt;

        return new KeyDownEvent { Key = key, Modifiers = mods };
    }

    //simulates a macOS key event: Cmd sets Meta only, physical Ctrl sets Ctrl only — they are distinct keys.
    private static KeyDownEvent MacKey(Keys key, bool cmd = false, bool ctrl = false, bool shift = false)
    {
        var mods = KeyModifiers.None;

        if (cmd)
            mods |= KeyModifiers.Meta;

        if (ctrl)
            mods |= KeyModifiers.Ctrl;

        if (shift)
            mods |= KeyModifiers.Shift;

        return new KeyDownEvent { Key = key, Modifiers = mods };
    }

    [Theory]
    [InlineData(Keys.C, false, CommandId.Editor_Copy)]
    [InlineData(Keys.X, false, CommandId.Editor_Cut)]
    [InlineData(Keys.V, false, CommandId.Editor_Paste)]
    [InlineData(Keys.Z, false, CommandId.Editor_Undo)]
    [InlineData(Keys.Z, true, CommandId.Editor_Redo)]
    [InlineData(Keys.A, true, CommandId.Editor_SelectAll)]
    public void Windows_EditorChords_ResolveToCurrentDefaults(Keys key, bool shift, CommandId expected)
    {
        Keybinds.ResetAll();

        Assert.Equal(expected, Keybinds.Resolve(WinKey(key, ctrl: true, shift: shift), KeybindContext.TextEditing));
    }

    [Fact]
    public void Windows_CtrlA_IsLineStart_NotSelectAll()
    {
        Keybinds.ResetAll();

        //the readline carve-out: bare Ctrl+A must stay line-start; Ctrl+Shift+A is select-all.
        Assert.Equal(CommandId.Editor_LineStart, Keybinds.Resolve(WinKey(Keys.A, ctrl: true), KeybindContext.TextEditing));
        Assert.Equal(CommandId.Editor_SelectAll, Keybinds.Resolve(WinKey(Keys.A, ctrl: true, shift: true), KeybindContext.TextEditing));
    }

    [Fact]
    public void Windows_CtrlE_IsLineEnd()
    {
        Keybinds.ResetAll();

        Assert.Equal(CommandId.Editor_LineEnd, Keybinds.Resolve(WinKey(Keys.E, ctrl: true), KeybindContext.TextEditing));
    }

    [Fact]
    public void Windows_CtrlZ_IsUndo_CtrlShiftZ_IsRedo()
    {
        Keybinds.ResetAll();

        Assert.Equal(CommandId.Editor_Undo, Keybinds.Resolve(WinKey(Keys.Z, ctrl: true), KeybindContext.TextEditing));
        Assert.Equal(CommandId.Editor_Redo, Keybinds.Resolve(WinKey(Keys.Z, ctrl: true, shift: true), KeybindContext.TextEditing));
    }

    [Fact]
    public void ExtraModifier_DoesNotMatch()
    {
        Keybinds.ResetAll();

        //Ctrl+Shift+C is not bound to anything — Copy has no Shift, so it must not fire.
        Assert.Null(Keybinds.Resolve(WinKey(Keys.C, ctrl: true, shift: true), KeybindContext.TextEditing));
    }

    [Fact]
    public void NoPrimaryModifier_DoesNotMatchMetaChord()
    {
        Keybinds.ResetAll();

        //plain C (no Ctrl) must not match Meta+C.
        Assert.Null(Keybinds.Resolve(WinKey(Keys.C), KeybindContext.TextEditing));
        Assert.False(Keybinds.Matches(WinKey(Keys.C), CommandId.Editor_Copy));
    }

    [Fact]
    public void ContextIsolation_ReadViewOnlyResolvesReadCommands()
    {
        Keybinds.ResetAll();

        //Ctrl+C in a read-only view resolves to the ReadView copy, not the editor copy.
        Assert.Equal(CommandId.Read_Copy, Keybinds.Resolve(WinKey(Keys.C, ctrl: true), KeybindContext.ReadView));

        //plain Ctrl+A is select-all in a read-only view (no line-start binding there), but line-start in the
        //editor — the same physical chord, disambiguated by context.
        Assert.Equal(CommandId.Read_SelectAll, Keybinds.Resolve(WinKey(Keys.A, ctrl: true), KeybindContext.ReadView));
        Assert.Equal(CommandId.Editor_LineStart, Keybinds.Resolve(WinKey(Keys.A, ctrl: true), KeybindContext.TextEditing));

        //editor-only commands (cut/paste/undo) have no ReadView binding.
        Assert.Null(Keybinds.Resolve(WinKey(Keys.X, ctrl: true), KeybindContext.ReadView));
    }

    [Fact]
    public void MacOS_Cmd_DrivesPrimaryActions_CtrlStaysLiteral()
    {
        Keybinds.ResetAll();

        //Cmd+C/A copy & select-all; physical Ctrl+A is still line-start (native macOS behavior).
        Assert.Equal(CommandId.Editor_Copy, Keybinds.Resolve(MacKey(Keys.C, cmd: true), KeybindContext.TextEditing));
        Assert.Equal(CommandId.Editor_SelectAll, Keybinds.Resolve(MacKey(Keys.A, cmd: true, shift: true), KeybindContext.TextEditing));
        Assert.Equal(CommandId.Editor_LineStart, Keybinds.Resolve(MacKey(Keys.A, ctrl: true), KeybindContext.TextEditing));

        //physical Ctrl+C on macOS is NOT copy (copy is Cmd+C).
        Assert.Null(Keybinds.Resolve(MacKey(Keys.C, ctrl: true), KeybindContext.TextEditing));
    }

    [Fact]
    public void Override_TakesPrecedenceOverDefault()
    {
        Keybinds.ResetAll();

        try
        {
            Keybinds.SetBinding(CommandId.Editor_Copy, new KeyChord(Keys.Insert, ChordMods.Meta));

            Assert.Equal(new KeyChord(Keys.Insert, ChordMods.Meta), Keybinds.Effective(CommandId.Editor_Copy).Primary);
            Assert.Equal(CommandId.Editor_Copy, Keybinds.Resolve(WinKey(Keys.Insert, ctrl: true), KeybindContext.TextEditing));

            //the former default chord no longer resolves to Copy.
            Assert.Null(Keybinds.Resolve(WinKey(Keys.C, ctrl: true), KeybindContext.TextEditing));
        } finally
        {
            Keybinds.ResetAll();
        }
    }

    [Fact]
    public void Persistence_RoundTripsOverridesOnly()
    {
        Keybinds.ResetAll();
        var previousDataPath = GlobalSettings.DataPath;
        var tempDir = Path.Combine(Path.GetTempPath(), "brigid-keybinds-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        GlobalSettings.DataPath = tempDir;

        try
        {
            var custom = new KeyChord(Keys.F12, ChordMods.Meta | ChordMods.Shift);
            Keybinds.SetBinding(CommandId.Editor_Paste, custom);
            Keybinds.Save();

            //command names — not ordinals — are the JSON keys, so reordering the enum can't corrupt saved
            //files. Assert the name is on disk (a numeric-key regression would still round-trip and hide this).
            Assert.Contains("\"Editor_Paste\"", File.ReadAllText(Path.Combine(tempDir, Keybinds.FILE_NAME)));

            //wipe in-memory overrides, then reload from disk.
            Keybinds.ResetAll();
            Assert.Equal(Keybinds.Defaults[CommandId.Editor_Paste].Chord, Keybinds.Effective(CommandId.Editor_Paste).Primary);

            Keybinds.Load();
            Assert.Equal(custom, Keybinds.Effective(CommandId.Editor_Paste).Primary);

            //untouched commands remain at their defaults (overrides-only file).
            Assert.Equal(Keybinds.Defaults[CommandId.Editor_Copy].Chord, Keybinds.Effective(CommandId.Editor_Copy).Primary);
        } finally
        {
            Keybinds.ResetAll();
            GlobalSettings.DataPath = previousDataPath;
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Redo_ResolvesFromBothChords()
    {
        Keybinds.ResetAll();

        //redo's two historical chords must both resolve — the single-chord model would silently drop one.
        Assert.Equal(CommandId.Editor_Redo, Keybinds.Resolve(WinKey(Keys.Z, ctrl: true, shift: true), KeybindContext.TextEditing));
        Assert.Equal(CommandId.Editor_Redo, Keybinds.Resolve(WinKey(Keys.Y, ctrl: true), KeybindContext.TextEditing));
    }

    [Theory]
    [InlineData(Keys.F1, CommandId.World_HelpMerchant)]
    [InlineData(Keys.F3, CommandId.World_MacroMenu)]
    [InlineData(Keys.F4, CommandId.World_Settings)]
    [InlineData(Keys.F5, CommandId.World_Refresh)]
    [InlineData(Keys.F7, CommandId.World_BoardList)]
    [InlineData(Keys.F9, CommandId.World_IgnoreList)]
    [InlineData(Keys.F10, CommandId.World_FriendsList)]
    [InlineData(Keys.Q, CommandId.World_TogglePauseMenu)]
    [InlineData(Keys.Space, CommandId.World_Assail)]
    [InlineData(Keys.T, CommandId.World_TownMap)]
    //Oem/Page keys — the most likely spot for a parity typo, so pin them explicitly.
    [InlineData(Keys.PageUp, CommandId.World_TabMapZoomIn)]
    [InlineData(Keys.PageDown, CommandId.World_TabMapZoomOut)]
    [InlineData(Keys.OemQuestion, CommandId.World_SwapHudLayout)]
    [InlineData(Keys.OemTilde, CommandId.World_UnequipWeaponShield)]
    public void WorldHud_PlainHotkeys_ResolveToCurrentDefaults(Keys key, CommandId expected)
    {
        Keybinds.ResetAll();

        Assert.Equal(expected, Keybinds.Resolve(WinKey(key), KeybindContext.WorldHud));
    }

    [Fact]
    public void WorldHud_ShiftChords_ResolveToCurrentDefaults()
    {
        Keybinds.ResetAll();

        //the migrated Shift chords: Shift is part of the chord (byte-for-byte the old handler's checks).
        Assert.Equal(CommandId.World_Whisper, Keybinds.Resolve(WinKey(Keys.OemQuotes, shift: true), KeybindContext.WorldHud));
        Assert.Equal(CommandId.World_ChatScrollUp, Keybinds.Resolve(WinKey(Keys.Up, shift: true), KeybindContext.WorldHud));
        Assert.Equal(CommandId.World_ChatScrollDown, Keybinds.Resolve(WinKey(Keys.Down, shift: true), KeybindContext.WorldHud));
    }

    [Fact]
    public void WorldHud_Defaults_AvoidReservedLiteralKeys()
    {
        //A/S/D/F/G/H (HUD tab switch) and Z/X/C/V (letter movement) are handled by modifier-agnostic literal
        //switches in OnRootKeyDown that run/return regardless of a resolver Matches check, so a WorldHud
        //command bound to one would be silently shadowed. The migration keeps the catalog clear of them; this
        //fails if a future command lands on one. Arrow/number-row keys are deliberately NOT reserved: their
        //literal movement/slot/emote handlers sit after the resolver checks that use them and Shift
        //disambiguates — hence Shout=Shift+1 and ChatScroll=Shift+Arrow are legitimate.
        var reserved = new HashSet<Keys>
        {
            Keys.A, Keys.S, Keys.D, Keys.F, Keys.G, Keys.H,
            Keys.Z, Keys.X, Keys.C, Keys.V
        };

        foreach (var (_, def) in Keybinds.Defaults)
        {
            if (def.Context != KeybindContext.WorldHud)
                continue;

            Assert.DoesNotContain(def.Binding.Primary.Key, reserved);

            if (def.Binding.Secondary is { } secondary)
                Assert.DoesNotContain(secondary.Key, reserved);
        }
    }

    [Fact]
    public void WorldHud_AltEnter_IsCycleWindow_BareEnter_IsChatFocus()
    {
        Keybinds.ResetAll();

        //the two Enter chords are disambiguated purely by Alt — the live handler checks Alt+Enter first.
        Assert.Equal(CommandId.World_CycleWindowSize, Keybinds.Resolve(WinKey(Keys.Enter, alt: true), KeybindContext.WorldHud));
        Assert.Equal(CommandId.World_ChatFocus, Keybinds.Resolve(WinKey(Keys.Enter), KeybindContext.WorldHud));
    }

    [Fact]
    public void WorldHud_Shout_IsShiftOne_NotBareOne()
    {
        Keybinds.ResetAll();

        //shout is Shift+1; bare 1 is a slot hotkey handled literally (never entered the resolver catalog).
        Assert.Equal(CommandId.World_Shout, Keybinds.Resolve(WinKey(Keys.D1, shift: true), KeybindContext.WorldHud));
        Assert.Null(Keybinds.Resolve(WinKey(Keys.D1), KeybindContext.WorldHud));
    }

    [Fact]
    public void WorldHud_PlainHotkey_RequiresExactModifiers()
    {
        Keybinds.ResetAll();

        //the migration tightens each plain hotkey to its exact modifier set: Ctrl+F5 (or Shift+F5) no longer
        //resolves to Refresh, where the old bare e.Key check would have fired regardless of held modifiers.
        Assert.Null(Keybinds.Resolve(WinKey(Keys.F5, ctrl: true), KeybindContext.WorldHud));
        Assert.Null(Keybinds.Resolve(WinKey(Keys.F5, shift: true), KeybindContext.WorldHud));
    }

    [Fact]
    public void EveryCommand_HasADefault()
    {
        foreach (CommandId id in Enum.GetValues<CommandId>())
            Assert.True(Keybinds.Defaults.ContainsKey(id), $"{id} has no default binding");
    }

    [Fact]
    public void NoTwoDefaults_CollideWithinAContext()
    {
        Keybinds.ResetAll();

        //every default chord (primary and secondary) must resolve back to its own command. If two same-context
        //commands shared a chord, one would shadow the other and this fails — guards the B3 catalog expansion.
        foreach (var (id, def) in Keybinds.Defaults)
        {
            Assert.Equal(id, Keybinds.Resolve(WinEventFor(def.Binding.Primary), def.Context));

            if (def.Binding.Secondary is { } secondary)
                Assert.Equal(id, Keybinds.Resolve(WinEventFor(secondary), def.Context));
        }
    }

    //builds the Windows-style event a chord would produce (Ctrl and Meta share the physical key, so a Meta
    //or literal-Ctrl chord sets both bits).
    private static KeyDownEvent WinEventFor(KeyChord chord)
    {
        var mods = KeyModifiers.None;

        if (chord.HasMeta || chord.HasCtrl)
            mods |= KeyModifiers.Ctrl | KeyModifiers.Meta;

        if (chord.HasShift)
            mods |= KeyModifiers.Shift;

        if (chord.HasAlt)
            mods |= KeyModifiers.Alt;

        return new KeyDownEvent { Key = chord.Key, Modifiers = mods };
    }
}
