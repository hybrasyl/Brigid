#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Dialog;

/// <summary>
///     Text entry sub-panel for MenuType.TextEntry (2) and MenuType.TextEntryWithArgs (3). Uses lnpcd2 template with
///     9-slice ornate frame. Shows an "Input:" label, a text input field, and an OK button. No prolog or epilog.
/// </summary>
public sealed class MenuTextEntryPanel : FramedDialogPanelBase
{
    private const int PANEL_WIDTH = 426;
    private const int PANEL_HEIGHT = 74;
    private const int BTN_WIDTH = 61;
    private const int BTN_HEIGHT = 22;
    private const int BOTTOM_ANCHOR_Y = 372;

    //content layout
    private const int LABEL_X = 22;
    private const int LABEL_Y = 19;
    private const int INPUT_X = 72;
    private const int INPUT_Y = 18;
    private const int INPUT_WIDTH = 332;
    private const int INPUT_HEIGHT = 14;

    private readonly UITextBox TextInput;

    /// <summary>
    ///     Previous args string carried from a MenuWithArgs/TextEntryWithArgs interaction. Sent back to the server alongside
    ///     the user's input text.
    /// </summary>
    public string? PreviousArgs { get; private set; }

    public MenuTextEntryPanel()
        : base("lnpcd2", false)
    {
        Name = "MenuTextEntry";
        Visible = false;


        //compact panel — dynamic 9-slice wraps just the text entry content
        Width = PANEL_WIDTH;
        Height = PANEL_HEIGHT;

        //right-aligned, bottom-anchored above dialog bar
        X = ChaosGame.VIRTUAL_WIDTH - Width;
        Y = BOTTOM_ANCHOR_Y - Height;

        //"input:" label
        var inputLabel = new UILabel
        {
            Name = "InputLabel",
            X = LABEL_X,
            Y = LABEL_Y,
            Width = 40,
            Height = 16,
            Text = "Enter:",
            ForegroundColor = Color.White
        };

        AddChild(inputLabel);

        //text input field
        TextInput = new UITextBox
        {
            Name = "TextInput",
            X = INPUT_X,
            Y = INPUT_Y,
            Width = INPUT_WIDTH,
            Height = INPUT_HEIGHT,
            MaxLength = 255,
            ForegroundColor = Color.Black,
            FocusedBackgroundColor = Color.White
        };

        AddChild(TextInput);

        //ok button from prefab
        OkButton = CreateButton("Btn1");

        if (OkButton is not null)
        {
            OkButton.X = Width - BTN_WIDTH - 20;
            OkButton.Y = Height - BTN_HEIGHT - 3;
            OkButton.Clicked += HandleSubmit;
        }
    }

    private void HandleSubmit()
    {
        var text = TextInput.Text.Trim();

        if (text.Length > 0)
            OnTextSubmit?.Invoke(text);
    }

    public override void Hide()
    {
        TextInput.IsFocused = false;
        base.Hide();
    }

    public event CloseHandler? OnClose;

    public event TextSubmitHandler? OnTextSubmit;

    public void ShowTextEntry(string? previousArgs)
    {
        PreviousArgs = previousArgs;

        TextInput.Text = string.Empty;
        TextInput.CursorPosition = 0;
        TextInput.IsFocused = true;

        Visible = true;
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