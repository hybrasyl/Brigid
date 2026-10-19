#region
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
        UiAssert.NamedTree(creator);
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
    public void TheSpeciesListEnablesOnlyHuman()
    {
        var creator = new CharacterCreatorControl();

        Assert.Equal(["Human", "Lyrien", "Dwarf", "Sylari", "Kobold", "Triton"], creator.Species.Entries);
        Assert.Equal("Human", creator.Species.SelectedText);
        AssertOnlyTheFirstEntryIsEnabled(creator.Species);
    }

    [Fact]
    public void TheBodyFrameListEnablesOnlyTypeA()
    {
        var creator = new CharacterCreatorControl();

        Assert.Equal(["Type A - Average", "Type B - Brawny", "Type L - Lean", "Type S - Stout"], creator.BodyFrame.Entries);
        Assert.Equal("Type A - Average", creator.BodyFrame.SelectedText);
        AssertOnlyTheFirstEntryIsEnabled(creator.BodyFrame);
    }

    private static void AssertOnlyTheFirstEntryIsEnabled(DropDown dropDown)
    {
        Assert.True(dropDown.IsEntryEnabled(0));

        for (var i = 1; i < dropDown.Entries.Count; i++)
            Assert.False(dropDown.IsEntryEnabled(i));
    }

    [Fact]
    public void EachOpenListDrawsAboveTheRowsItCovers()
    {
        var creator = new CharacterCreatorControl();
        var look = creator.FindChild<UIPanel>("look")!;
        var gender = look.FindChild<TabStrip>("gender")!;

        Assert.True(creator.Species.ZIndex > creator.BodyFrame.ZIndex);
        Assert.True(creator.BodyFrame.ZIndex > gender.ZIndex);

        //the open list is clipped to the look group, so the whole list must fit in it
        foreach (var dropDown in new[] { creator.Species, creator.BodyFrame })
        {
            dropDown.Open();
            Assert.True(dropDown.Y + dropDown.Height <= look.Height);
            dropDown.Close();
        }
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

        creator.BodyFrame.Open();
        creator.OnKeyDown(new KeyDownEvent { Key = Keys.Escape });

        Assert.False(creator.BodyFrame.IsOpen);
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
    public void WhileBusyTheCreatorCanStillBeCancelled()
    {
        var creator = new CharacterCreatorControl();
        var cancels = 0;
        creator.OnCancel += () => cancels++;

        creator.SetBusy(true);
        creator.OnKeyDown(new KeyDownEvent { Key = Keys.Escape });

        Assert.Equal(1, cancels);
        Assert.True(creator.CancelButton.Enabled);
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
}
