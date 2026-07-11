#region
using Brigid.Controls.Components;
using Brigid.Data;
using Brigid.Extensions;
using Brigid.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A popup message dialog with DlgBack2.spf tiled background (clipped to interior height), dlgframe.epf 16×16 border,
///     and butt001.epf OK (+ optional Cancel) button.
/// </summary>
public sealed class OkPopupMessageControl : UIPanel
{
    private const int TILES_WIDE = 4;
    private const int INTERIOR_HEIGHT = 54;
    private const int CONTENT_PADDING = 6;
    private const int BUTTON_MARGIN = 1;

    //butt001.epf frame indices — 2 frames per button (normal/pressed)
    private const int OK_NORMAL = 15;
    private const int OK_PRESSED = 16;
    private const int CANCEL_NORMAL = 21;
    private const int CANCEL_PRESSED = 22;

    private readonly int ContentHeight;
    private readonly int ContentWidth;
    private readonly int ContentX;
    private readonly int ContentY;

    private readonly UILabel MessageLabel;
    private UIElement? PreviousFocus;

    public UIButton? CancelButton { get; }
    public UIButton OkButton { get; }

    public OkPopupMessageControl(bool showCancel = false)
    {
        Visible = false;
        UsesControlStack = true;

        //load background tile
        using var bgTile = DataContext.UserControls.GetSpfImage("DlgBack2.spf");

        if (bgTile is null)
            throw new InvalidOperationException("Failed to load DlgBack2.spf");

        var borderSize = DialogFrame.BORDER_SIZE;
        var interiorWidth = bgTile.Width * TILES_WIDE - 23;
        var totalWidth = borderSize + interiorWidth + borderSize;
        var totalHeight = borderSize + INTERIOR_HEIGHT + borderSize;

        //composite tiled background with border
        using var composite = DialogFrame.Composite(bgTile, totalWidth, totalHeight);

        if (composite is null)
            throw new InvalidOperationException("Failed to composite dialog background");

        Width = totalWidth;
        Height = totalHeight;
        this.CenterOnScreen();
        Background = TextureConverter.ToTexture2D(composite);

        //content area — text fills above the button row
        ContentX = borderSize + CONTENT_PADDING;
        ContentY = borderSize + CONTENT_PADDING;
        ContentWidth = interiorWidth - CONTENT_PADDING * 2;
        ContentHeight = INTERIOR_HEIGHT - CONTENT_PADDING * 2;

        //button textures
        var cache = UiRenderer.Instance!;
        var okNormalTex = cache.GetEpfTexture("butt001.epf", OK_NORMAL);
        var okPressedTex = cache.GetEpfTexture("butt001.epf", OK_PRESSED);

        var okWidth = okNormalTex.Width;
        var okHeight = okNormalTex.Height;
        var buttonY = totalHeight - borderSize - okHeight - BUTTON_MARGIN + 5;
        var rightButtonX = totalWidth - borderSize - BUTTON_MARGIN + 4;
        int okX;

        //cancel button (optional) — positioned on the right
        if (showCancel)
        {
            var cancelNormalTex = cache.GetEpfTexture("butt001.epf", CANCEL_NORMAL);
            var cancelPressedTex = cache.GetEpfTexture("butt001.epf", CANCEL_PRESSED);

            var cancelWidth = cancelNormalTex.Width;

            CancelButton = new UIButton
            {
                Name = "Cancel",
                X = rightButtonX - cancelWidth,
                Y = buttonY,
                Width = cancelWidth,
                Height = cancelNormalTex.Height,
                NormalTexture = cancelNormalTex,
                PressedTexture = cancelPressedTex
            };
            CancelButton.Clicked += () => OnCancel?.Invoke();
            AddChild(CancelButton);

            okX = CancelButton.X - okWidth - BUTTON_MARGIN;
        } else
            okX = rightButtonX - okWidth;

        //ok button — left of cancel, or slides right when cancel absent
        OkButton = new UIButton
        {
            Name = "OK",
            X = okX,
            Y = buttonY,
            Width = okWidth,
            Height = okHeight,
            NormalTexture = okNormalTex,
            PressedTexture = okPressedTex
        };
        OkButton.Clicked += () => OnOk?.Invoke();
        AddChild(OkButton);

        MessageLabel = new UILabel
        {
            X = ContentX - 9,
            Y = ContentY - 10,
            Width = ContentWidth,
            Height = ContentHeight,
            WordWrap = true,
            ForegroundColor = Color.White,
            VerticalAlignment = VerticalAlignment.Top
        };
        AddChild(MessageLabel);
    }

    public void Hide()
    {
        InputDispatcher.Instance!.RemoveControl(this);
        Visible = false;
        MessageLabel.Text = string.Empty;

        //restore focus to whatever had it before the popup stole it (e.g. the login password box)
        if (PreviousFocus is not null)
        {
            InputDispatcher.Instance!.SetExplicitFocus(PreviousFocus);
            PreviousFocus = null;
        }
    }

    public event CancelHandler? OnCancel;

    public event OkHandler? OnOk;

    public void Show(string message)
    {
        MessageLabel.Text = message;

        //a second Show while already up (back-to-back server messages) must not re-stash:
        //explicit focus is already cleared at that point, so stashing again would overwrite
        //the real PreviousFocus with null and lose the focus restore on dismiss
        if (Visible)
            return;

        //remember who had focus so we can restore it on dismiss, then clear focus so
        //keyboard input routes to this popup via the control stack instead of being
        //eaten by the previously-focused textbox.
        PreviousFocus = InputDispatcher.Instance!.ExplicitFocus;
        InputDispatcher.Instance!.ClearExplicitFocus();
        InputDispatcher.Instance!.PushControl(this);
        Visible = true;
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key is Keys.Enter or Keys.Space)
        {
            OkButton.PerformClick();
            e.Handled = true;
        } else if (e.Key == Keys.Escape)
        {
            if (CancelButton is not null)
                CancelButton.PerformClick();
            else
                OkButton.PerformClick();

            e.Handled = true;
        }
    }
}