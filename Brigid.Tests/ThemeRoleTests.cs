#region
using Brigid.Controls.Components;
using Brigid.Data.AssetPacks;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-120 theme roles of the primitives: a themed control draws in the theme colour of its role, an unthemed
///     control or a theme with no such colour gives the fallback, and setting the colour directly clears the role. A
///     focused text box takes its fill from the theme, and leaves slot art uncovered.
/// </summary>
[Collection("FontEngine")]
public sealed class ThemeRoleTests : IDisposable
{
    private static readonly Color Fallback = new(226, 202, 120);
    private static readonly Color ThemeColour = new(16, 32, 48);
    private static readonly Color FocusFill = new(38, 40, 48);

    private readonly IAssetPack? PreviousTheme = AssetPackRegistry.GetUiThemePack();
    private UiThemePack? Theme;

    public ThemeRoleTests() => FontEngine.Initialize(0);

    public void Dispose()
    {
        AssetPackRegistry.SetPackForTests("ui_theme", PreviousTheme);
        Theme?.Dispose();
    }

    [Fact]
    public void AThemedLabelDrawsInTheThemeColourOfItsRole()
    {
        InstallTheme();
        var label = InPanel(new UILabel { Text = "Title" }, themed: true);
        label.SetForegroundRole(ThemeRoles.TextTitle, Fallback);

        label.ApplyForegroundRole();

        Assert.Equal(ThemeColour, label.DrawnForegroundColor);
        Assert.Equal(Fallback, label.ForegroundColor);
    }

    [Fact]
    public void AnUnthemedLabelDrawsInTheFallback()
    {
        InstallTheme();
        var label = InPanel(new UILabel { Text = "Title" }, themed: false);
        label.SetForegroundRole(ThemeRoles.TextTitle, Fallback);

        label.ApplyForegroundRole();

        Assert.Equal(Fallback, label.DrawnForegroundColor);
    }

    [Fact]
    public void ARoleThatTheThemeDoesNotHaveDrawsInTheFallback()
    {
        InstallTheme();
        var label = InPanel(new UILabel { Text = "Title" }, themed: true);
        label.SetForegroundRole(ThemeRoles.TextSelected, Fallback);

        label.ApplyForegroundRole();

        Assert.Equal(Fallback, label.DrawnForegroundColor);
    }

    [Fact]
    public void SettingTheLabelColourClearsTheRole()
    {
        InstallTheme();
        var label = InPanel(new UILabel { Text = "Title" }, themed: true);
        label.SetForegroundRole(ThemeRoles.TextTitle, Fallback);
        label.ApplyForegroundRole();

        label.ForegroundColor = Color.White;
        label.ApplyForegroundRole();

        Assert.Equal(Color.White, label.DrawnForegroundColor);
    }

    [Fact]
    public void ATextBoxRoleFollowsTheThemeAndTheColourClearsIt()
    {
        InstallTheme();
        var box = InPanel(new UITextBox(), themed: true);

        box.ForegroundColor = Color.White;
        Assert.Equal(Color.White, box.DrawnForegroundColor);

        box.SetForegroundRole(ThemeRoles.InputText, Color.White);
        Assert.Equal(ThemeColour, box.DrawnForegroundColor);
        Assert.Equal(Color.White, box.ForegroundColor);

        box.ForegroundColor = Color.Gray;
        Assert.Equal(Color.Gray, box.DrawnForegroundColor);
    }

    [Fact]
    public void AnUnthemedTextBoxRoleGivesTheFallback()
    {
        InstallTheme();
        var box = InPanel(new UITextBox(), themed: false);
        box.SetForegroundRole(ThemeRoles.InputText, Color.White);

        Assert.Equal(Color.White, box.DrawnForegroundColor);
    }

    [Fact]
    public void AFocusedTextBoxTakesTheThemeFocusColour()
    {
        InstallTheme(""" "input.focus": "#102030" """);
        var box = FocusedBox(new UIPanel { UseTheme = true });

        Assert.Equal(ThemeColour, box.DrawnBackgroundColor);

        box.IsFocused = false;
    }

    [Fact]
    public void AFocusedTextBoxOnSlotArtWithNoFocusColourDrawsNoFill()
    {
        InstallTheme(parts: """ "input.slot": { "file": "slot.png" } """);
        var box = FocusedBox(new InputSlotPane { UseTheme = true });

        Assert.Null(box.DrawnBackgroundColor);

        box.IsFocused = false;
    }

    [Fact]
    public void AFocusedTextBoxWithNoThemeOrNoSlotArtKeepsItsFocusFill()
    {
        InstallTheme();
        var unthemed = FocusedBox(new InputSlotPane());
        Assert.Equal(FocusFill, unthemed.DrawnBackgroundColor);
        unthemed.IsFocused = false;

        var flatSlot = FocusedBox(new InputSlotPane { UseTheme = true });
        Assert.Equal(FocusFill, flatSlot.DrawnBackgroundColor);
        flatSlot.IsFocused = false;
    }

    private static UITextBox FocusedBox(UIPanel parent)
    {
        var box = new UITextBox { FocusedBackgroundColor = FocusFill };
        parent.AddChild(box);
        box.IsFocused = true;

        return box;
    }

    private void InstallTheme(string extraColours = "", string parts = "")
    {
        var colours = """ "text.title": "#102030", "input.text": "#102030" """ + (extraColours.Length > 0 ? "," + extraColours : "");
        var json = "{ \"colors\": {" + colours + "}" + (parts.Length > 0 ? ", \"parts\": {" + parts + "}" : "") + " }";

        Theme = parts.Length > 0
            ? UiThemeTests.BuildPack(json, ("slot.png", UiThemeTests.Png(8, 8)))
            : UiThemeTests.BuildPack(json);
        AssetPackRegistry.SetPackForTests("ui_theme", Theme);
    }

    private static T InPanel<T>(T child, bool themed) where T : UIElement
    {
        var panel = new UIPanel { UseTheme = themed };
        panel.AddChild(child);

        return child;
    }
}
