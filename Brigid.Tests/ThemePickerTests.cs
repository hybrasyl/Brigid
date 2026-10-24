#region
using System.IO.Compression;
using System.Text;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Popups.Options;
using Brigid.Data.AssetPacks;
using Brigid.Rendering;
using Brigid.Systems;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The theme choice in Options (BRIG-124): Automatic, each installed theme by its display name, and None. A choice is
///     raised to the host, which saves it; it applies at the next start, so the hint shows while the chosen theme is not
///     the active one. The row grows with the open list, so the list draws and takes clicks.
/// </summary>
[Collection("AssetPackRegistry")]
public sealed class ThemePickerTests : IDisposable
{
    private const int ROW_H = 14;

    private static readonly UiThemeChoices.Entry[] Entries =
    [
        new(null, "Automatic"),
        new("stone", "Stone Hall"),
        new("wood", "wood"),
        new(AssetPackRegistry.NO_UI_THEME, "None")
    ];

    public ThemePickerTests()
    {
        FontEngine.Initialize(0);
        AssetPackRegistry.ResetForTest();
    }

    public void Dispose() => AssetPackRegistry.ResetForTest();

    private static void RegisterTheme(string packId, int priority, string? name)
    {
        var stream = new MemoryStream();

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var entry = zip.CreateEntry(UiThemePack.THEME_ENTRY_NAME).Open())
            entry.Write(Encoding.UTF8.GetBytes("{}"));

        stream.Position = 0;

        Assert.True(AssetPackRegistry.RegisterForTest(
            new ZipArchive(stream, ZipArchiveMode.Read),
            new AssetPackManifest { SchemaVersion = 1, PackId = packId, ContentType = "ui_theme", Priority = priority, Name = name }));
    }

    private static ThemePicker NewPicker(string? stored, string? active = null)
        => new(380, ROW_H, Entries, stored, value => string.Equals(value ?? "stone", active ?? "stone", StringComparison.OrdinalIgnoreCase));

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("WOOD", 2)]
    [InlineData(" none ", 3)]
    //a stored theme that is not installed selects the first entry, Automatic
    [InlineData("goth", 0)]
    public void TheStoredValueSelectsItsEntry(string? stored, int expected)
        => Assert.Equal(expected, NewPicker(stored).Choice.SelectedIndex);

    [Fact]
    public void AChoiceRaisesItsValue()
    {
        var picker = NewPicker(null);
        var chosen = new List<string?>();
        picker.ThemeChosen += chosen.Add;

        picker.Choose(2);
        picker.Choose(0);
        picker.Choose(3);

        Assert.Equal(["wood", null, AssetPackRegistry.NO_UI_THEME], chosen);
    }

    [Fact]
    public void TheHintShows_WhileTheChosenThemeIsNotTheActiveOne()
    {
        //stone is active, through Automatic or by its name
        var picker = NewPicker(null);
        Assert.False(picker.ShowsHint);

        picker.Choose(2);
        Assert.True(picker.ShowsHint);

        picker.Choose(1);
        Assert.False(picker.ShowsHint);

        picker.Choose(3);
        Assert.True(picker.ShowsHint);
    }

    [Fact]
    public void TheRowGrowsWithTheOpenList_SoTheListDrawsAndTakesClicks()
    {
        var pane = new UIPanel { Width = 380, Height = 275 };
        var picker = NewPicker(null);
        pane.AddChild(picker);

        //the row grows when the list opens, with no Update between: a click on the list in the same frame must reach it
        picker.Choice.Open();

        Assert.Equal(ROW_H * (Entries.Length + 1), picker.Height);

        //a draw updates the clip rects from the root down; the hit test reads them
        LayClips(pane);

        //a point on the last list row reaches the drop-down
        var lastRowY = picker.Choice.ScreenY + ROW_H * Entries.Length + ROW_H / 2;
        Assert.Same(picker.Choice, InputDispatcher.HitTest(pane, picker.Choice.ScreenX + 4, lastRowY));

        picker.Choice.Close();

        Assert.Equal(ROW_H, picker.Height);
    }

    private static readonly System.Reflection.MethodInfo UpdateClipRect =
        typeof(UIElement).GetMethod("UpdateClipRect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    private static void LayClips(UIElement element)
    {
        UpdateClipRect.Invoke(element, null);

        if (element is UIPanel panel)
            foreach (var child in panel.Children)
                LayClips(child);
    }

    [Fact]
    public void TheOptionsEntriesAreAutomatic_EachThemeByItsName_AndNone()
    {
        RegisterTheme("wood", 100, null);
        RegisterTheme("stone", 200, "Stone Hall");

        Assert.Equal(
            [new(null, "Automatic"), new("stone", "Stone Hall"), new("wood", "wood"), new(AssetPackRegistry.NO_UI_THEME, "None")],
            OptionsModalControl.ThemeEntries());
    }

    [Fact]
    public void TwoThemesWithOneName_ShowTheirPackIds()
    {
        RegisterTheme("stone-a", 100, "Stone");
        RegisterTheme("stone-b", 100, "stone");

        Assert.Equal(["Automatic", "Stone (stone-a)", "stone (stone-b)", "None"], OptionsModalControl.ThemeEntries().Select(entry => entry.Label));
    }

    [Fact]
    public void NoneMakesNoThemeActive_AndLogsNoWarning()
    {
        RegisterTheme("stone", 100, null);
        var warnings = new List<string>();
        void Collect(string line) => warnings.Add(line);

        AssetPackRegistry.WarningLogged += Collect;

        try
        {
            AssetPackRegistry.SelectUiTheme("None");
        }
        finally
        {
            AssetPackRegistry.WarningLogged -= Collect;
        }

        Assert.Null(AssetPackRegistry.GetUiThemePack());
        Assert.Null(AssetPackRegistry.ResolveUiTheme(AssetPackRegistry.NO_UI_THEME));
        Assert.Empty(warnings);
    }
}
