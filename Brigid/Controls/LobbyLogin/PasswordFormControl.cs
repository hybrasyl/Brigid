#region
using Brigid.Controls.Components;
#endregion

namespace Brigid.Controls.LobbyLogin;

/// <summary>
///     The password-change form as a primitive modal (BRIG-119). It replaces the prefab <see cref="PasswordChangeControl" />
///     and keeps its flow: the screen reads the name, the current password and the new password for the password-change
///     packet, and checks the confirm field.
///     <para>
///         Enter changes the password, and Escape cancels. While the screen waits for the server, Change and Cancel are
///         disabled, and Escape does nothing. A failure shows in the status line. The legacy form let the player cancel while
///         it waited; a reply that arrived after the cancel then went down the login path.
///     </para>
/// </summary>
public sealed class PasswordFormControl : LobbyFormPanel
{
    private const int PANEL_W = 300;
    private const int PANEL_H = 210;

    private readonly InputSlotPane Account;

    public UITextBox NameField { get; }
    public UITextBox CurrentPasswordField { get; }
    public UITextBox NewPasswordField { get; }
    public UITextBox ConfirmPasswordField { get; }

    public PasswordFormControl()
        : base(
            name: "password",
            title: "Change Password",
            submitText: "Change",
            submitName: "change",
            busyText: "Changing the password...",
            width: PANEL_W,
            height: PANEL_H)
    {
        Account = new InputSlotPane { Name = "account" };
        AddFieldLabel(Account, "name_label", "Name", 0);
        NameField = AddField(Account, "name", 0, NAME_MAX_LENGTH, false);
        AddFieldLabel(Account, "current_label", "Current", 1);
        CurrentPasswordField = AddField(Account, "current", 1, PASSWORD_MAX_LENGTH, true);
        AddFieldLabel(Account, "new_label", "New", 2);
        NewPasswordField = AddField(Account, "new", 2, PASSWORD_MAX_LENGTH, true);
        AddFieldLabel(Account, "confirm_label", "Confirm", 3);
        ConfirmPasswordField = AddField(Account, "confirm", 3, PASSWORD_MAX_LENGTH, true);
        AddChild(Account);

        LayoutContent();
    }

    //the fields at the top of the content and the status line at its bottom
    protected override void LayoutContent()
    {
        var content = ContentBounds;
        PlaceStatus(content);

        Account.X = content.X;
        Account.Y = content.Y + PADDING;
        Account.Width = content.Width;
        Account.Height = RowsHeight(4);
        SetFieldWidths(content.Width);
    }

    /// <summary>Clears the new password and the confirm fields, and puts the cursor in the new password field.</summary>
    public void ClearNewPasswords()
    {
        ConfirmPasswordField.Text = string.Empty;
        ClearAndFocus(NewPasswordField);
    }
}
