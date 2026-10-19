#region
using Brigid.Controls.Components;
#endregion

namespace Brigid.Controls.LobbyLogin;

/// <summary>
///     The login form as a primitive modal (BRIG-119). It replaces the prefab <see cref="LoginControl" /> and keeps its
///     flow: the screen reads the name and the password for the login packet.
///     <para>
///         Enter in the name field moves the cursor to the password field, as in the legacy form. Enter anywhere else logs
///         in, and Escape cancels. While the screen waits for the server, the form stays shown, Log In and Cancel are
///         disabled, and Escape does nothing. A failure shows in the status line.
///     </para>
///     <para>
///         The legacy form hid itself while it waited, so a login could not be cancelled. A cancel that the server does not
///         see would leave its answer to go down the wrong path: an accepted login would still enter the world. So the form
///         keeps the <see cref="LobbyFormPanel" /> default and cannot cancel while it is busy.
///     </para>
/// </summary>
public sealed class LoginFormControl : LobbyFormPanel
{
    private const int PANEL_W = 300;
    private const int PANEL_H = 170;

    private readonly InputSlotPane Account;

    public UITextBox NameField { get; }
    public UITextBox PasswordField { get; }

    public LoginFormControl()
        : base(
            name: "login",
            title: "Log In",
            submitText: "Log In",
            submitName: "log_in",
            busyText: "Logging in...",
            width: PANEL_W,
            height: PANEL_H)
    {
        Account = new InputSlotPane { Name = "account" };
        AddFieldLabel(Account, "name_label", "Name", 0);
        NameField = AddField(Account, "name", 0, NAME_MAX_LENGTH, false);
        AddFieldLabel(Account, "password_label", "Password", 1);
        PasswordField = AddField(Account, "password", 1, PASSWORD_MAX_LENGTH, true);
        AddChild(Account);

        LayoutContent();
    }

    //the fields at the top of the content and the status line at its bottom; a themed frame makes the content area smaller
    protected override void LayoutContent()
    {
        var content = ContentBounds;
        PlaceStatus(content);

        Account.X = content.X;
        Account.Y = content.Y + PADDING;
        Account.Width = content.Width;
        Account.Height = RowsHeight(2);
        SetFieldWidths(content.Width);
    }

    /// <summary>Clears the password field and puts the cursor in it.</summary>
    public void ClearPassword() => ClearAndFocus(PasswordField);

    protected override void HandleEnter()
    {
        if (NameField.IsFocused)
        {
            NameField.IsFocused = false;
            PasswordField.IsFocused = true;

            return;
        }

        Submit();
    }
}
