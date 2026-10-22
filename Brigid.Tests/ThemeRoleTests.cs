#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Data.AssetPacks;
using Brigid.Rendering;
using System.Reflection;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-120 theme roles of the primitives: a themed control draws in the theme colour of its role, an unthemed
///     control or a theme with no such colour gives the fallback, and setting the colour directly clears the role. A
///     focused text box takes its fill from the theme, and leaves slot art uncovered. Each colour role has one flat value
///     (BRIG-122 D4), which a role with no fallback and a primitive with no theme colour use.
/// </summary>
[Collection("FontEngine")]
public sealed class ThemeRoleTests : IDisposable
{
    private static readonly Color Fallback = new(226, 202, 120);
    private static readonly Color ThemeColour = new(16, 32, 48);
    private static readonly Color FocusFill = new(38, 40, 48);

    private UiThemePack? Theme;

    public ThemeRoleTests() => FontEngine.Initialize(0);

    public void Dispose()
    {
        AssetPackRegistry.SetUiThemeForTests(null);
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

    [Fact]
    public void AFocusedTextBoxKeepsItsFocusFill_WhenTheSlotArtIsListedButAbsent()
    {
        InstallTheme(parts: """ "input.slot": { "file": "missing.png" } """);
        var box = FocusedBox(new InputSlotPane { UseTheme = true });

        Assert.Equal(FocusFill, box.DrawnBackgroundColor);

        box.IsFocused = false;
    }

    [Fact]
    public void APartIsUsableOnlyWhenItsImageDecodes_AndOnlyThenGivesABorder()
    {
        InstallTheme(parts: """ "input.slot": { "file": "slot.png", "slice": [3, 3, 3, 3] }, "frame.panel": { "file": "missing.png", "slice": [44, 44, 44, 44] } """);

        Assert.True(ThemeArt.HasPart("input.slot"));
        Assert.True(ThemeArt.TryGetSliceBorder("input.slot", out var border));
        Assert.Equal(3, border);

        Assert.False(ThemeArt.HasPart("frame.panel"));
        Assert.False(ThemeArt.TryGetSliceBorder("frame.panel", out _));
    }

    [Fact]
    public void AThemedTextBoxWithNoThemeFocusColourTakesTheFlatFocusColour()
    {
        InstallTheme();
        var themed = FocusedBox(new UIPanel { UseTheme = true }, Color.White);
        Assert.Equal(DialogPalette.InputFocus, themed.DrawnBackgroundColor);
        themed.IsFocused = false;

        var unthemed = FocusedBox(new UIPanel(), Color.White);
        Assert.Equal(Color.White, unthemed.DrawnBackgroundColor);
        unthemed.IsFocused = false;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARoleWithNoFallbackDrawsInTheFlatValueOfTheRole(bool themed)
    {
        //the test theme has no text colour, so a themed label also draws the flat value
        InstallTheme();
        var label = InPanel(new UILabel { Text = "Text" }, themed);
        label.SetForegroundRole(ThemeRoles.Text);

        label.ApplyForegroundRole();

        Assert.Equal(DialogPalette.Text, label.DrawnForegroundColor);
    }

    public static TheoryData<string> ColourRoles() => new(ThemeRoles.Colours);

    [Theory]
    [MemberData(nameof(ColourRoles))]
    public void EveryColourRoleHasAFlatValue(string role) => DialogPalette.ForRole(role);

    [Fact]
    public void EveryOtherRoleIsNotAColourRole()
    {
        var others = typeof(ThemeRoles)
                     .GetFields(BindingFlags.Public | BindingFlags.Static)
                     .Where(field => field.IsLiteral)
                     .Select(field => (string)field.GetRawConstantValue()!)
                     .Except(ThemeRoles.Colours);

        Assert.All(others, role => Assert.Throws<ArgumentOutOfRangeException>(() => DialogPalette.ForRole(role)));
    }

    [Fact]
    public void FlatPrimitivesDrawTheBorderOfTheirRoles()
    {
        var border = DialogPalette.ForRole(ThemeRoles.Border);

        Assert.Equal(border, InPanel(new CheckBox(), themed: true).DrawnBorderColor);
        Assert.Equal(border, InPanel(new FramePanel(), themed: true).DrawnBorderColor);

        var button = InPanel(new TextButton("OK", 40, 16), themed: true);
        Assert.Equal(border, button.DrawnBorderColor);

        button.Enabled = false;
        Assert.Equal(DialogPalette.ForRole(ThemeRoles.Divider), button.DrawnBorderColor);
    }

    [Fact]
    public void AFlatTabDrawsTheBorderAndSelectedColoursOfItsRoles()
    {
        var tab = InPanel(new SelectableTab("Tab", 40, 16), themed: true);
        Assert.Equal(DialogPalette.ForRole(ThemeRoles.Border), tab.DrawnBorderColor);

        tab.IsSelected = true;
        Assert.Equal(DialogPalette.ForRole(ThemeRoles.TextSelected), tab.DrawnBorderColor);
    }

    private static UITextBox FocusedBox(UIPanel parent) => FocusedBox(parent, FocusFill);

    private static UITextBox FocusedBox(UIPanel parent, Color focusFill)
    {
        var box = new UITextBox { FocusedBackgroundColor = focusFill };
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
        AssetPackRegistry.SetUiThemeForTests(Theme);
    }

    private static T InPanel<T>(T child, bool themed) where T : UIElement
    {
        var panel = new UIPanel { UseTheme = themed };
        panel.AddChild(child);

        return child;
    }
}
