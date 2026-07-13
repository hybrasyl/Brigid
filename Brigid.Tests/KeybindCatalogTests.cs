using Brigid.Systems.Keybinds;
using Microsoft.Xna.Framework.Input;
using Xunit;
using static Brigid.Systems.Keybinds.KeybindCatalog;

namespace Brigid.Tests;

/// <summary>
///     Track C support: the rebind catalog must stay in lock-step with the resolver's WorldHud defaults
///     (no command silently missing from the UI, no orphan entry), and chord formatting must read the way
///     the user pressed it.
/// </summary>
public sealed class KeybindCatalogTests
{
    private static HashSet<CommandId> WorldHudDefaults() =>
        Keybinds.Defaults.Where(kv => kv.Value.Context == KeybindContext.WorldHud)
                .Select(kv => kv.Key)
                .ToHashSet();

    [Fact]
    public void Catalog_CoversEveryWorldHudCommand()
    {
        //every rebindable WorldHud command has exactly one catalog entry, and every entry is a real WorldHud
        //command — the guard that a new B3/B4 command can't be dropped from the rebinder (or listed twice).
        var worldHud = WorldHudDefaults();
        var catalog = Entries.Select(e => e.Id).ToList();

        foreach (var id in worldHud)
            Assert.Contains(id, catalog);

        foreach (var id in catalog)
            Assert.Contains(id, worldHud);

        Assert.Equal(catalog.Count, catalog.Distinct().Count());
        Assert.Equal(worldHud.Count, catalog.Count);
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
