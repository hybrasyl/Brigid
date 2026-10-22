#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.LobbyLogin;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-119 login form: every child is addressable by a lowercase name, the fields keep the legacy limits, Enter in
///     the name field moves to the password field, and the form can be cancelled but not submitted while the server
///     answers.
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
    public void WhileBusyTheFormCannotBeSubmitted_ButCanBeCancelled()
    {
        //the screen ends the wait of a cancelled form: it closes the connection, so the late answer does not arrive
        var form = new LoginFormControl();
        var oks = 0;
        var cancels = 0;
        form.OnOk += () => oks++;
        form.OnCancel += () => cancels++;

        form.SetBusy(true);
        form.OnKeyDown(new KeyDownEvent { Key = Keys.Enter });
        form.SubmitButton.OnClick(LeftClick());

        Assert.Equal(0, oks);
        Assert.False(form.SubmitButton.Enabled);
        Assert.True(form.CancelButton.Enabled);
        Assert.Equal("Logging in...", form.StatusText);

        form.OnKeyDown(new KeyDownEvent { Key = Keys.Escape });
        form.CancelButton.OnClick(LeftClick());

        Assert.Equal(2, cancels);
    }

    //BRIG-143: a key pressed just after OK changed the password that the form showed after a refused login
    [Fact]
    public void WhileBusyTheFieldsTakeNoText_AndTakeItAgainAtTheirCursorAfterTheWait()
    {
        var form = new LoginFormControl();
        form.PasswordField.Text = "secret";
        form.PasswordField.IsFocused = true;
        form.PasswordField.CursorPosition = 3;
        form.PasswordField.ClearSelection();

        try
        {
            form.SetBusy(true);
            form.PasswordField.OnTextInput(new TextInputEvent { Character = 'x' });
            form.PasswordField.OnKeyDown(new KeyDownEvent { Key = Keys.Back });

            Assert.True(form.NameField.IsReadOnly);
            Assert.Equal("secret", form.PasswordField.Text);

            form.SetBusy(false);
            form.PasswordField.OnTextInput(new TextInputEvent { Character = 'x' });

            Assert.False(form.NameField.IsReadOnly);
            Assert.Equal("secxret", form.PasswordField.Text);
        } finally
        {
            form.PasswordField.IsFocused = false;
        }
    }

    [Fact]
    public void BusyMsCountsTheWait_AndStartsAgainWithTheNextWait()
    {
        var form = new LoginFormControl();
        var second = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1));

        form.Update(second);
        Assert.Equal(0, form.BusyMs);

        form.SetBusy(true);
        form.Update(second);
        form.SetBusy(true);
        form.Update(second);
        Assert.Equal(2000, form.BusyMs);

        form.SetBusy(false);
        Assert.Equal(0, form.BusyMs);
    }

    [Fact]
    public void HideEndsTheWait()
    {
        var form = new LoginFormControl();
        form.Show();
        form.SetBusy(true);

        form.Hide();

        Assert.False(form.IsBusy);
        Assert.True(form.SubmitButton.Enabled);
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
