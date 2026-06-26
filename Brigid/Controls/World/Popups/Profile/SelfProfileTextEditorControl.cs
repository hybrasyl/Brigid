#region
using Brigid.Controls.Components;
using Brigid.Data;
using Brigid.Extensions;
using Brigid.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Profile;

/// <summary>
///     Screen-centered popup editor for the player's profile text. Uses DialogFrame compositing for the border/background.
///     Contains a multiline UITextBox for editing and OK/Cancel buttons. OK saves and fires OnSave; Cancel discards
///     changes.
/// </summary>
public sealed class SelfProfileTextEditorControl : UIPanel
{
    private const int TOTAL_WIDTH = 200;
    private const int TOTAL_HEIGHT = 200;
    private const int INSET_LEFT = 13;
    private const int INSET_RIGHT = 13;
    private const int INSET_TOP = 9;
    private const int INSET_BOTTOM = 6;

    //butt001.epf frame indices — 2 frames per button (normal/pressed)
    private const int OK_NORMAL = 15;
    private const int OK_PRESSED = 16;
    private const int CANCEL_NORMAL = 21;
    private const int CANCEL_PRESSED = 22;

    private const int BUTTON_GAP = 2;

    private readonly UIButton CancelButton;
    private readonly UIButton OkButton;
    private readonly UITextBox TextBox;

    public SelfProfileTextEditorControl()
    {
        Name = "ProfileTextEditor";
        Visible = false;
        UsesControlStack = true;
        IsModal = true;
        Width = TOTAL_WIDTH;
        Height = TOTAL_HEIGHT;

        //composite background with dialogframe border
        using var bgTile = DataContext.UserControls.GetSpfImage("DlgBack2.spf");

        if (bgTile is not null)
        {
            using var composite = DialogFrame.Composite(bgTile, TOTAL_WIDTH, TOTAL_HEIGHT);

            if (composite is not null)
                Background = TextureConverter.ToTexture2D(composite);
        }

        //button textures from butt001.epf
        var cache = UiRenderer.Instance!;
        var okNormalTex = cache.GetEpfTexture("butt001.epf", OK_NORMAL);
        var okPressedTex = cache.GetEpfTexture("butt001.epf", OK_PRESSED);
        var cancelNormalTex = cache.GetEpfTexture("butt001.epf", CANCEL_NORMAL);
        var cancelPressedTex = cache.GetEpfTexture("butt001.epf", CANCEL_PRESSED);

        var btnHeight = okNormalTex.Height;
        var buttonY = TOTAL_HEIGHT - INSET_BOTTOM - btnHeight;

        //ok button — bottom-left
        OkButton = new UIButton
        {
            Name = "OK",
            X = INSET_LEFT,
            Y = buttonY,
            Width = okNormalTex.Width,
            Height = btnHeight,
            NormalTexture = okNormalTex,
            PressedTexture = okPressedTex
        };
        OkButton.Clicked += Confirm;
        AddChild(OkButton);

        //cancel button — bottom-right
        CancelButton = new UIButton
        {
            Name = "Cancel",
            X = TOTAL_WIDTH - INSET_RIGHT - cancelNormalTex.Width,
            Y = buttonY,
            Width = cancelNormalTex.Width,
            Height = cancelNormalTex.Height,
            NormalTexture = cancelNormalTex,
            PressedTexture = cancelPressedTex
        };
        CancelButton.Clicked += Cancel;
        AddChild(CancelButton);

        //multiline textbox fills the area between top inset and buttons
        var textBoxHeight = buttonY - BUTTON_GAP - INSET_TOP;

        TextBox = new UITextBox
        {
            Name = "Editor",
            X = INSET_LEFT,
            Y = INSET_TOP,
            Width = TOTAL_WIDTH - INSET_LEFT - INSET_RIGHT,
            Height = textBoxHeight,
            IsMultiLine = true,
            ClampToVisibleArea = true,
            BackgroundColor = Color.Black,
            FocusedBackgroundColor = Color.Black,
            ForegroundColor = Color.White,
            MaxLength = 256
        };
        AddChild(TextBox);

        this.CenterOnScreen();
    }

    private void Cancel() => Hide();

    private void Confirm()
    {
        OnSave?.Invoke(TextBox.Text);
        Hide();
    }

    public override void Dispose()
    {
        Background?.Dispose();
        Background = null;

        base.Dispose();
    }

    public void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        TextBox.IsFocused = false;
        Visible = false;
    }

    public event ProfileTextSavedHandler? OnSave;

    public void Show(string text)
    {
        TextBox.Text = text;
        TextBox.CursorPosition = 0;
        TextBox.ScrollOffset = 0;
        TextBox.IsFocused = true;
        this.CenterOnScreen();
        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }
}