#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Dialog;

/// <summary>
///     Text entry sub-panel using the lnpcd4 prefab inside the shared ornate frame. Displays a prompt (Prolog) above a
///     text input field, optional text below (Epilog), and an OK button. Used for DialogType.TextEntry (4) and
///     DialogType.Speak (5).
/// </summary>
public sealed class DialogTextEntryPanel : FramedDialogPanelBase
{
    private const int BOTTOM_ANCHOR_Y = 372;
    private readonly UILabel? EpilogLabel;
    private readonly UILabel? PrologLabel;
    private readonly UITextBox? TextInput;

    public DialogTextEntryPanel()
        : base("lnpcd4", false)
    {
        Name = "DialogTextEntry";
        Visible = false;


        //right-aligned, bottom-anchored above dialog bar (same position as option menu)
        X = ChaosGame.VIRTUAL_WIDTH - Width;
        Y = BOTTOM_ANCHOR_Y - Height;

        PrologLabel = CreateLabel("Prolog");
        TextInput = CreateTextBox("TextInput", 255);
        EpilogLabel = CreateLabel("Epilog");
        OkButton = CreateButton("Btn1");

        if (OkButton is not null)
            OkButton.Clicked += HandleSubmit;

        if (TextInput is not null)
        {
            TextInput.ForegroundColor = Color.Black;
            TextInput.FocusedBackgroundColor = Color.White;
        }
    }

    private void HandleSubmit()
    {
        if (TextInput is null)
            return;

        var text = TextInput.Text.Trim();

        if (text.Length > 0)
            OnTextSubmit?.Invoke(text);
    }

    public override void Hide()
    {
        TextInput?.IsFocused = false;

        base.Hide();
    }

    public event CloseHandler? OnClose;

    public event TextSubmitHandler? OnTextSubmit;

    public void ShowTextEntry(string prolog, byte maxLength, string epilog)
    {
        PrologLabel?.Text = prolog;

        EpilogLabel?.Text = epilog;

        if (TextInput is not null)
        {
            TextInput.Text = string.Empty;
            TextInput.MaxLength = maxLength > 0 ? maxLength : 255;
            TextInput.CursorPosition = 0;
            TextInput.IsFocused = true;
        }

        Show();
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case Keys.Escape:
                OnClose?.Invoke();
                e.Handled = true;

                break;

            case Keys.Enter:
                HandleSubmit();
                e.Handled = true;

                break;
        }
    }
}