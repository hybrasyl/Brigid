#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.LobbyLogin;

public sealed class LoginControl : PrefabPanel
{
    public UIButton? CancelButton { get; }
    public UIButton? OkButton { get; }
    public UITextBox? PasswordField { get; }
    public UITextBox? UsernameField { get; }

    public LoginControl()
        : base("_nlogin")
    {
        Name = "LoginDialog";
        Visible = false;
        UsesControlStack = true;

        //buttons
        OkButton = CreateButton("OK");
        CancelButton = CreateButton("Cancel");

        //text fields — type 7 with 0 images, must be created manually
        UsernameField = CreateTextBox("Name");
        PasswordField = CreateTextBox("Password", 8);

        UsernameField?.ForegroundColor = LegendColors.White;
        UsernameField?.IsTabStop = true;
        
        PasswordField?.ForegroundColor = LegendColors.White;
        PasswordField?.IsMasked = true;
        PasswordField?.IsTabStop = true;

        if (UsernameField is not null)
            UsernameField.OnFocused += OnTextBoxFocused;

        if (PasswordField is not null)
            PasswordField.OnFocused += OnTextBoxFocused;
    }

    public override void Hide()
    {
        if (UsernameField is not null)
        {
            UsernameField.IsFocused = false;
            UsernameField.Text = string.Empty;
        }

        if (PasswordField is not null)
        {
            PasswordField.IsFocused = false;
            PasswordField.Text = string.Empty;
        }

        base.Hide();
    }

    private void OnTextBoxFocused(UITextBox focused)
    {
        if (UsernameField is not null && (focused != UsernameField))
            UsernameField.IsFocused = false;

        if (PasswordField is not null && (focused != PasswordField))
            PasswordField.IsFocused = false;
    }

    public override void Show()
    {
        if (UsernameField is not null)
        {
            UsernameField.Text = string.Empty;
            UsernameField.IsFocused = true;
        }

        if (PasswordField is not null)
        {
            PasswordField.Text = string.Empty;
            PasswordField.IsFocused = false;
        }

        base.Show();
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case Keys.Tab:
                if (UsernameField?.IsFocused == true)
                {
                    UsernameField.IsFocused = false;
                    PasswordField?.IsFocused = true;
                } else
                {
                    PasswordField?.IsFocused = false;
                    UsernameField?.IsFocused = true;
                }

                e.Handled = true;

                break;

            case Keys.Enter:
                //enter in username → move to password; enter in password → login
                if (UsernameField?.IsFocused == true)
                {
                    UsernameField.IsFocused = false;
                    PasswordField?.IsFocused = true;
                } else
                    OkButton?.PerformClick();

                e.Handled = true;

                break;

            case Keys.Escape:
                CancelButton?.PerformClick();
                e.Handled = true;

                break;
        }
    }
}