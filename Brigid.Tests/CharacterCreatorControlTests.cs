#region
using System.Text.RegularExpressions;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.LobbyLogin;
using Brigid.Definitions;
using Brigid.Rendering;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-116 character creator: every child is addressable by a lowercase name, the form shows each selection, the
///     species list has only Human, and the keys and the status line work as the card says.
/// </summary>
[Collection("FontEngine")]
public class CharacterCreatorControlTests
{
    public CharacterCreatorControlTests() => FontEngine.Initialize(0);

    [Fact]
    public void EveryChildHasAUniqueName()
    {
        var creator = new CharacterCreatorControl();

        Assert.Equal("charcreate", creator.Name);
        AssertNamedTree(creator);
    }

    [Fact]
    public void TheFieldsKeepTheLegacyLimits()
    {
        var creator = new CharacterCreatorControl();

        Assert.Equal(12, creator.NameField.MaxLength);
        Assert.False(creator.NameField.IsMasked);
        Assert.Equal(8, creator.PasswordField.MaxLength);
        Assert.True(creator.PasswordField.IsMasked);
        Assert.Equal(8, creator.PasswordConfirmField.MaxLength);
        Assert.True(creator.PasswordConfirmField.IsMasked);
    }

    [Fact]
    public void TheSpeciesListHasOnlyHuman()
    {
        var creator = new CharacterCreatorControl();

        Assert.Equal(["Human"], creator.Species.Entries);
        Assert.Equal("Human", creator.Species.SelectedText);
    }

    [Fact]
    public void ShowStartsAnEmptyFormWithTheCursorInTheName()
    {
        var creator = new CharacterCreatorControl();
        creator.NameField.Text = "Angelique";
        creator.PasswordField.Text = "secret";
        creator.Appearance.SetGender(Gender.Female);
        creator.ShowError("Passwords do not match.");

        creator.Show();

        Assert.Equal(string.Empty, creator.NameField.Text);
        Assert.Equal(string.Empty, creator.PasswordField.Text);
        Assert.Equal(string.Empty, creator.StatusText);
        Assert.Equal(Gender.Male, creator.SelectedGender);
        Assert.True(creator.NameField.IsFocused);

        creator.Hide();

        Assert.False(creator.NameField.IsFocused);
    }

    [Fact]
    public void TheGenderTabsShowAndChangeTheGender()
    {
        var creator = new CharacterCreatorControl();
        var tabs = creator.FindChild<TabStrip>("gender")!;

        Assert.Equal(0, tabs.SelectedIndex);

        tabs.FindChild<SelectableTab>("tab_1")!.OnClick(LeftClick());

        Assert.Equal(Gender.Female, creator.SelectedGender);
        Assert.Equal(1, tabs.SelectedIndex);
    }

    [Fact]
    public void TheHairStepperShowsTheStyleAndTheLimit()
    {
        var creator = new CharacterCreatorControl();
        var hair = creator.FindChild<UIPanel>("hair")!;
        var value = hair.FindChild<UILabel>("value")!;

        Assert.Equal("Style 1/18", value.Text);

        hair.FindChild<TextButton>("previous")!.OnClick(LeftClick());
        Assert.Equal("Style 18/18", value.Text);

        creator.Appearance.SetGender(Gender.Female);
        Assert.Equal("Style 17/17", value.Text);
        Assert.Equal(17, creator.SelectedHairStyle);
    }

    [Fact]
    public void ASwatchClickSelectsItsColour()
    {
        var creator = new CharacterCreatorControl();
        var grid = creator.FindChild<UIPanel>("haircolor")!;

        Assert.Equal(CreatorAppearance.HairColors.Count, grid.Children.Count);

        grid.FindChild<UIPanel>("navy")!.OnClick(LeftClick());

        Assert.Equal(DisplayColor.Navy, creator.SelectedHairColor);
    }

    [Fact]
    public void EnterIsCreateButAltEnterIsNot()
    {
        var creator = new CharacterCreatorControl();
        var oks = 0;
        creator.OnOk += () => oks++;

        creator.OnKeyDown(new KeyDownEvent { Key = Keys.Enter, Modifiers = KeyModifiers.Alt });
        Assert.Equal(0, oks);

        creator.OnKeyDown(new KeyDownEvent { Key = Keys.Enter });
        Assert.Equal(1, oks);
    }

    [Fact]
    public void EscapeClosesTheSpeciesListBeforeItCancels()
    {
        var creator = new CharacterCreatorControl();
        var cancels = 0;
        creator.OnCancel += () => cancels++;
        creator.Species.Open();

        creator.OnKeyDown(new KeyDownEvent { Key = Keys.Escape });

        Assert.False(creator.Species.IsOpen);
        Assert.Equal(0, cancels);

        creator.OnKeyDown(new KeyDownEvent { Key = Keys.Escape });

        Assert.Equal(1, cancels);
    }

    [Fact]
    public void TheSwatchNamesAreTheLiteralTableInTheColourOrder()
    {
        var creator = new CharacterCreatorControl();
        var grid = creator.FindChild<UIPanel>("haircolor")!;

        Assert.Equal(
            ["teal", "green", "olive", "yellow", "pumpkin", "apple", "violet", "default", "navy", "blue", "gray", "carrot", "brown", "black"],
            grid.Children.Select(swatch => swatch.Name));
    }

    [Fact]
    public void ASecondShowDoesNotClearTheForm()
    {
        var creator = new CharacterCreatorControl();
        creator.Show();
        creator.NameField.Text = "Angelique";
        creator.Appearance.SetGender(Gender.Female);

        creator.Show();

        Assert.Equal("Angelique", creator.NameField.Text);
        Assert.Equal(Gender.Female, creator.SelectedGender);

        creator.Hide();
    }

    [Fact]
    public void WhileBusyCreateIsDisabledAndEnterSendsNothing()
    {
        var creator = new CharacterCreatorControl();
        var oks = 0;
        creator.OnOk += () => oks++;

        creator.SetBusy(true);
        creator.OnKeyDown(new KeyDownEvent { Key = Keys.Enter });
        creator.CreateButton.OnClick(LeftClick());

        Assert.Equal(0, oks);
        Assert.False(creator.CreateButton.Enabled);
        Assert.Equal("Creating the character...", creator.StatusText);

        creator.SetBusy(false);
        creator.OnKeyDown(new KeyDownEvent { Key = Keys.Enter });

        Assert.Equal(1, oks);
        Assert.True(creator.CreateButton.Enabled);
    }

    [Fact]
    public void HideClosesTheSpeciesList()
    {
        var creator = new CharacterCreatorControl();
        creator.Show();
        creator.Species.Open();

        creator.Hide();

        Assert.False(creator.Species.IsOpen);
    }

    [Fact]
    public void ClearPasswordsEmptiesBothAndFocusesTheFirst()
    {
        var creator = new CharacterCreatorControl();
        creator.PasswordField.Text = "one";
        creator.PasswordConfirmField.Text = "two";

        creator.ClearPasswords();

        Assert.Equal(string.Empty, creator.PasswordField.Text);
        Assert.Equal(string.Empty, creator.PasswordConfirmField.Text);
        Assert.True(creator.PasswordField.IsFocused);

        creator.PasswordField.IsFocused = false;
    }

    private static ClickEvent LeftClick() => new() { Button = MouseButton.Left };

    private static void AssertNamedTree(UIPanel panel)
    {
        var names = panel.Children.Select(child => child.Name).ToList();

        //the id rule of the ui_panels layout plan; a dot is the path separator, so it is never part of a name
        Assert.All(names, name => Assert.Matches(new Regex("^[a-z0-9_]+$"), name));
        Assert.Equal(names.Count, names.Distinct().Count());

        //a TextButton or a tab is a panel too; its label is its own detail, not a separate target
        foreach (var child in panel.Children.OfType<UIPanel>().Where(child => child is not (TextButton or SelectableTab)))
            AssertNamedTree(child);
    }
}
