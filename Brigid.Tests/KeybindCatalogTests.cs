using Brigid.Systems.Keybinds;
using Microsoft.Xna.Framework.Input;
using Xunit;
using static Brigid.Systems.Keybinds.KeybindCatalog;

namespace Brigid.Tests;

/// <summary>
///     Track C support: the rebind catalog must stay in lock-step with the resolver's exposed defaults — every
///     WorldHud command plus the TextEditing (editor) commands, but NOT the ReadView commands, which are
///     consumption-only (routed for macOS + conventional defaults, not independently rebindable). No command
///     silently missing from the UI, no orphan entry; and chord formatting must read the way the user pressed it.
/// </summary>
public sealed class KeybindCatalogTests
{
    //the rebindable surface: WorldHud (world hotkeys + movement) and TextEditing (the editor family). ReadView
    //(Read_Copy/Read_SelectAll) is deliberately excluded — it is consumed but not exposed as its own rows.
    private static HashSet<CommandId> ExposedDefaults() =>
        Keybinds.Defaults.Where(kv => kv.Value.Context is KeybindContext.WorldHud or KeybindContext.TextEditing)
                .Select(kv => kv.Key)
                .ToHashSet();

    //commands consumed by the resolver but deliberately NOT surfaced in the rebinder. A new command must land
    //here or in the catalog — EveryDefault_IsExposedOrIntentionallyUnexposed fails otherwise, so the choice is
    //never silent. ReadView copy/select-all mirror the editor conventions and follow the editor's chords.
    private static readonly HashSet<CommandId> IntentionallyUnexposed =
    [
        CommandId.Read_Copy,
        CommandId.Read_SelectAll
    ];

    [Fact]
    public void EveryDefault_IsExposedOrIntentionallyUnexposed()
    {
        //forces every resolver command to be a deliberate decision: either a rebinder row or an explicit
        //opt-out. Prevents a new ReadView/Modal command from silently shipping un-rebindable with no signal.
        var catalog = Entries.Select(e => e.Id).ToHashSet();

        foreach (var id in Keybinds.Defaults.Keys)
            Assert.True(catalog.Contains(id) || IntentionallyUnexposed.Contains(id),
                $"{id} is neither in the rebind catalog nor the IntentionallyUnexposed allowlist — decide which.");

        //the allowlist can't overlap the catalog (a command is exposed xor hidden, never both).
        foreach (var id in IntentionallyUnexposed)
            Assert.DoesNotContain(id, catalog);
    }

    [Fact]
    public void Catalog_CoversEveryExposedCommand()
    {
        //every rebindable command has exactly one catalog entry, and every entry is a real exposed command — the
        //guard that a new command can't be dropped from the rebinder (or listed twice), and that ReadView stays
        //out of the catalog.
        var exposed = ExposedDefaults();
        var catalog = Entries.Select(e => e.Id).ToList();

        foreach (var id in exposed)
            Assert.Contains(id, catalog);

        foreach (var id in catalog)
            Assert.Contains(id, exposed);

        Assert.Equal(catalog.Count, catalog.Distinct().Count());
        Assert.Equal(exposed.Count, catalog.Count);
    }

    [Fact]
    public void TabCommands_DispatchEveryHudTabCatalogCommand()
    {
        //WorldScreen.TabCommands is a fourth list (beside the enum, Defaults, and the catalog) that the enum↔
        //Defaults↔catalog guards don't cover: it decides which World_Tab* commands are actually *dispatched*.
        //Assert it references exactly the HudTabs catalog rows — no command left undispatched (a dead binding),
        //no orphan — and that each row targets a distinct HudTab.
        var dispatchedList = Brigid.Screens.WorldScreen.TabCommands
            .SelectMany(t => t.Alt is { } alt ? new[] { t.Base, alt } : [t.Base])
            .ToList();
        var dispatched = dispatchedList.ToHashSet();
        var catalogHudTabs = Entries.Where(e => e.Section == KeybindSection.HudTabs).Select(e => e.Id).ToHashSet();

        Assert.True(dispatched.SetEquals(catalogHudTabs),
            "WorldScreen.TabCommands and the HudTabs catalog rows must reference exactly the same commands.");
        Assert.Equal(dispatchedList.Count, dispatched.Count); //no command dispatched twice

        var tabs = Brigid.Screens.WorldScreen.TabCommands.Select(t => t.Tab).ToList();
        Assert.Equal(tabs.Count, tabs.Distinct().Count()); //each row targets a distinct HudTab
    }

    [Fact]
    public void EveryEntry_HasANonEmptyLabel()
    {
        foreach (var entry in Entries)
            Assert.False(string.IsNullOrWhiteSpace(entry.DisplayName), $"{entry.Id} has no display name");
    }

    [Fact]
    public void EverySection_IsInTheDisplayOrder()
    {
        //no entry can land in a section the UI won't render.
        foreach (var entry in Entries)
            Assert.Contains(entry.Section, SectionOrder);
    }

    [Theory]
    [InlineData(Keys.F5, ChordMods.None, "F5")]
    [InlineData(Keys.Enter, ChordMods.Alt, "Alt+Enter")]
    [InlineData(Keys.D1, ChordMods.Shift, "Shift+1")]
    [InlineData(Keys.OemQuestion, ChordMods.None, "/")]
    [InlineData(Keys.OemTilde, ChordMods.None, "`")]
    [InlineData(Keys.Up, ChordMods.Shift, "Shift+Up")]
    [InlineData(Keys.PageUp, ChordMods.None, "Page Up")]
    [InlineData(Keys.Space, ChordMods.None, "Space")]
    public void Format_Chord_ReadsLikeThePress(Keys key, ChordMods mods, string expected) =>
        Assert.Equal(expected, Format(new KeyChord(key, mods)));

    [Fact]
    public void Format_MetaChord_UsesPlatformPrimaryModifierName()
    {
        //Meta renders as the OS primary modifier: Cmd on macOS, Ctrl elsewhere.
        var expected = OperatingSystem.IsMacOS() ? "Cmd+C" : "Ctrl+C";
        Assert.Equal(expected, Format(new KeyChord(Keys.C, ChordMods.Meta)));
    }

    [Fact]
    public void Format_TwoChordBinding_ShowsBothSeparatedBySlash()
    {
        //movement is arrow (primary) / zxcv (secondary).
        Assert.Equal("Up / C", Format(Keybinds.Defaults[CommandId.Move_Up].Binding));
    }
}
