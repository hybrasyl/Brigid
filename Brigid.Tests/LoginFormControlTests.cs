#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.LobbyLogin;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-119 login form: every child is addressable by a lowercase name, the fields keep the legacy limits, Enter in
///     the name field moves to the password field, and the form cannot be cancelled while the server answers.
/// </summary>
[Collection("FontEngine")]
public class LoginFormControlTests
{
    public LoginFormControlTests() => FontEngine.Initialize(0);

    [Fact]
    public void EveryChildHasAUniqueName()
    {
        var form = new LoginFormControl();

        Assert.Equal("login", form.Name);
        UiAssert.NamedTree(form);
    }

    [Fact]
    public void TheFieldsKeepTheLegacyLimits()
    {
        var form = new LoginFormControl();

        Assert.Equal(12, form.NameField.MaxLength);
        Assert.False(form.NameField.IsMasked);
        Assert.Equal(8, form.PasswordField.MaxLength);
        Assert.True(form.PasswordField.IsMasked);
    }

    [Fact]
    public void ShowStartsAnEmptyFormWithTheCursorInTheName()
    {
        var form = new LoginFormControl();
        form.NameField.Text = "Angelique";
        form.PasswordField.Text = "secret";
        form.ShowError("Login failed.");

        form.Show();

        Assert.Equal(string.Empty, form.NameField.Text);
        Assert.Equal(string.Empty, form.PasswordField.Text);
        Assert.Equal(string.Empty, form.StatusText);
        Assert.True(form.NameField.IsFocused);

        form.Hide();

        Assert.False(form.NameField.IsFocused);
    }

    [Fact]
    public void EnterInTheNameMovesToThePasswordAndThenLogsIn()
    {
        var form = new LoginFormControl();
        var oks = 0;
        form.OnOk += () => oks++;
        form.NameField.IsFocused = true;

        form.OnKeyDown(new KeyDownEvent { Key = Keys.Enter });

        Assert.Equal(0, oks);
        Assert.True(form.PasswordField.IsFocused);

        form.OnKeyDown(new KeyDownEvent { Key = Keys.Enter });

        Assert.Equal(1, oks);

        form.PasswordField.IsFocused = false;
    }

    [Fact]
    public void AltEnterDoesNotLogIn()
    {
        var form = new LoginFormControl();
        var oks = 0;
        form.OnOk += () => oks++;

        form.OnKeyDown(new KeyDownEvent { Key = Keys.Enter, Modifiers = KeyModifiers.Alt });

        Assert.Equal(0, oks);
    }

    [Fact]
    public void WhileBusyTheFormCannotBeSubmittedOrCancelled()
    {
        var form = new LoginFormControl();
        var oks = 0;
        var cancels = 0;
        form.OnOk += () => oks++;
        form.OnCancel += () => cancels++;

        form.SetBusy(true);
        form.OnKeyDown(new KeyDownEvent { Key = Keys.Enter });
        form.SubmitButton.OnClick(LeftClick());
        form.OnKeyDown(new KeyDownEvent { Key = Keys.Escape });
        form.CancelButton.OnClick(LeftClick());

        Assert.Equal(0, oks);
        Assert.Equal(0, cancels);
        Assert.False(form.SubmitButton.Enabled);
        Assert.False(form.CancelButton.Enabled);
        Assert.Equal("Logging in...", form.StatusText);

        form.SetBusy(false);
        form.OnKeyDown(new KeyDownEvent { Key = Keys.Escape });

        Assert.Equal(1, cancels);
        Assert.True(form.CancelButton.Enabled);
    }

    [Fact]
    public void ClearPasswordEmptiesItAndFocusesIt()
    {
        var form = new LoginFormControl();
        form.PasswordField.Text = "secret";

        form.ClearPassword();

        Assert.Equal(string.Empty, form.PasswordField.Text);
        Assert.True(form.PasswordField.IsFocused);

        form.PasswordField.IsFocused = false;
    }

    [Fact]
    public void TheStatusLineFitsBelowTheFields()
    {
        var form = new LoginFormControl();
        var account = form.FindChild<UIPanel>("account")!;
        var status = form.FindChild<UILabel>("status")!;

        Assert.True(account.Y + account.Height <= status.Y);
        Assert.True(account.FindChild<UITextBox>("password")!.Width > 0);
    }

    internal static ClickEvent LeftClick() => new() { Button = MouseButton.Left };
}
