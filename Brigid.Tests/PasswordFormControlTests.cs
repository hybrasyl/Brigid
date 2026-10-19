#region
using Brigid.Controls.Components;
using Brigid.Controls.LobbyLogin;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-119 password-change form: every child is addressable by a lowercase name, the fields keep the legacy limits,
///     Enter changes the password, and Cancel stays available while the server answers.
/// </summary>
[Collection("FontEngine")]
public class PasswordFormControlTests
{
    public PasswordFormControlTests() => FontEngine.Initialize(0);

    [Fact]
    public void EveryChildHasAUniqueName()
    {
        var form = new PasswordFormControl();

        Assert.Equal("password", form.Name);
        UiAssert.NamedTree(form);
    }

    [Fact]
    public void TheFieldsKeepTheLegacyLimits()
    {
        var form = new PasswordFormControl();

        Assert.Equal(12, form.NameField.MaxLength);
        Assert.False(form.NameField.IsMasked);

        foreach (var field in new[] { form.CurrentPasswordField, form.NewPasswordField, form.ConfirmPasswordField })
        {
            Assert.Equal(8, field.MaxLength);
            Assert.True(field.IsMasked);
        }
    }

    [Fact]
    public void ShowStartsAnEmptyFormWithTheCursorInTheName()
    {
        var form = new PasswordFormControl();
        form.NameField.Text = "Angelique";
        form.NewPasswordField.Text = "secret";
        form.ShowError("New passwords do not match.");

        form.Show();

        Assert.Equal(string.Empty, form.NameField.Text);
        Assert.Equal(string.Empty, form.NewPasswordField.Text);
        Assert.Equal(string.Empty, form.StatusText);
        Assert.True(form.NameField.IsFocused);

        form.Hide();

        Assert.False(form.NameField.IsFocused);
    }

    [Fact]
    public void EnterChangesThePasswordFromAnyField()
    {
        var form = new PasswordFormControl();
        var oks = 0;
        form.OnOk += () => oks++;
        form.NameField.IsFocused = true;

        form.OnKeyDown(new KeyDownEvent { Key = Keys.Enter });

        Assert.Equal(1, oks);

        form.NameField.IsFocused = false;
    }

    [Fact]
    public void WhileBusyTheFormCannotBeSubmittedOrCancelled()
    {
        //a reply that arrives after a cancel would go down the login path
        var form = new PasswordFormControl();
        var oks = 0;
        var cancels = 0;
        form.OnOk += () => oks++;
        form.OnCancel += () => cancels++;

        form.SetBusy(true);
        form.OnKeyDown(new KeyDownEvent { Key = Keys.Enter });
        form.OnKeyDown(new KeyDownEvent { Key = Keys.Escape });

        Assert.Equal(0, oks);
        Assert.Equal(0, cancels);
        Assert.False(form.SubmitButton.Enabled);
        Assert.False(form.CancelButton.Enabled);
        Assert.Equal("Changing the password...", form.StatusText);

        form.SetBusy(false);
        form.OnKeyDown(new KeyDownEvent { Key = Keys.Escape });

        Assert.Equal(1, cancels);
    }

    [Fact]
    public void ClearNewPasswordsEmptiesBothAndFocusesTheNewPassword()
    {
        var form = new PasswordFormControl();
        form.CurrentPasswordField.Text = "old";
        form.NewPasswordField.Text = "one";
        form.ConfirmPasswordField.Text = "two";

        form.ClearNewPasswords();

        Assert.Equal("old", form.CurrentPasswordField.Text);
        Assert.Equal(string.Empty, form.NewPasswordField.Text);
        Assert.Equal(string.Empty, form.ConfirmPasswordField.Text);
        Assert.True(form.NewPasswordField.IsFocused);

        form.NewPasswordField.IsFocused = false;
    }

    [Fact]
    public void TheStatusLineFitsBelowTheFields()
    {
        var form = new PasswordFormControl();
        var account = form.FindChild<UIPanel>("account")!;
        var status = form.FindChild<UILabel>("status")!;

        Assert.True(account.Y + account.Height <= status.Y);
    }
}
