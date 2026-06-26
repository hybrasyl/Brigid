#region
using Brigid.Controls.Components;
using Brigid.Data;
using Brigid.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     Popup text window for ScrollWindow, NonScrollWindow, and WoodenBoard ServerMessageTypes.
///     Fixed position at (140,60), size 360x180 matching the original client. ScrollWindow and
///     NonScrollWindow are identical (dialog frame with scrollbar and close button). WoodenBoard
///     uses woodbk.epf wooden plank background from Legend.dat. All styles support mouse wheel scrolling.
/// </summary>
public sealed class TextPopupControl : UIPanel
{
    //original client rect {top,left,bottom,right}: {60, 140, 240, 500}
    //position: x=140, y=60. size: w=360, h=180.
    private const int POPUP_X = 140;
    private const int POPUP_Y = 60;
    private const int POPUP_WIDTH = 360;
    private const int POPUP_HEIGHT = 180;

    //dialog frame text insets (16px dlgframe border)
    private const int FRAME_INSET = DialogFrame.BORDER_SIZE;

    //wooden board text insets (per re: 16px left/right, 12px top, 32px bottom)
    private const int WOOD_INSET_X = 16;
    private const int WOOD_INSET_TOP = 12;
    private const int WOOD_INSET_BOTTOM = 32;

    //butt001.epf frame indices for close button (2nd button: frames 3,4,5)
    private const int CLOSE_NORMAL = 3;
    private const int CLOSE_PRESSED = 4;
    private readonly UIButton CloseButton;
    private readonly ScrollBarControl Scrollbar;

    private readonly UILabel TextLabel;
    private Texture2D? DialogBackground;
    private Texture2D? WoodBackground;

    public TextPopupControl()
    {
        Name = "TextPopup";
        Visible = false;
        UsesControlStack = true;
        X = POPUP_X;
        Y = POPUP_Y;
        Width = POPUP_WIDTH;
        Height = POPUP_HEIGHT;

        TextLabel = new UILabel
        {
            WordWrap = true,
            ForegroundColor = Color.White
        };
        AddChild(TextLabel);

        //close button — bottom-right, under the scrollbar
        var cache = UiRenderer.Instance!;
        var closeNormalTex = cache.GetEpfTexture("butt001.epf", CLOSE_NORMAL);
        var closePressedTex = cache.GetEpfTexture("butt001.epf", CLOSE_PRESSED);
        var btnW = closeNormalTex.Width;
        var btnH = closeNormalTex.Height;

        CloseButton = new UIButton
        {
            Name = "Close",
            X = POPUP_WIDTH - FRAME_INSET - btnW,
            Y = POPUP_HEIGHT - FRAME_INSET - btnH,
            Width = btnW,
            Height = btnH,
            NormalTexture = closeNormalTex,
            PressedTexture = closePressedTex
        };
        CloseButton.Clicked += Hide;
        AddChild(CloseButton);

        //scrollbar — right side, above the close button
        var scrollHeight = CloseButton.Y - FRAME_INSET;

        Scrollbar = new ScrollBarControl
        {
            X = POPUP_WIDTH - FRAME_INSET - ScrollBarControl.DEFAULT_WIDTH,
            Y = FRAME_INSET,
            Height = scrollHeight,
            Visible = false
        };
        Scrollbar.OnValueChanged += value => TextLabel.ScrollOffset = value * TextRenderer.CHAR_HEIGHT;
        AddChild(Scrollbar);

        LoadBackgrounds();
    }

    public void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        Visible = false;
        OnClose?.Invoke();
    }

    private void LoadBackgrounds()
    {
        //dialog frame: dlgback2.spf tiled background + dlgframe.epf 8-piece border
        using var bgTile = DataContext.UserControls.GetSpfImage("DlgBack2.spf");

        if (bgTile is not null)
        {
            using var composite = DialogFrame.Composite(bgTile, POPUP_WIDTH, POPUP_HEIGHT);

            if (composite is not null)
                DialogBackground = TextureConverter.ToTexture2D(composite);
        }

        //wooden board: woodbk.epf from legend.dat (360x180, exact match)
        using var woodImage = DataContext.UserControls.GetLegendEpfImage("woodbk.epf");

        if (woodImage is not null)
            WoodBackground = TextureConverter.ToTexture2D(woodImage);
    }

    public event CloseHandler? OnClose;

    /// <summary>
    ///     Shows a popup with the given text. ScrollWindow and NonScrollWindow are identical (dialog
    ///     frame with scrollbar and close button). WoodenBoard uses wooden plank background.
    /// </summary>
    public void Show(string text, PopupStyle style = PopupStyle.Scroll)
    {
        var contentHeight = Scrollbar.Height;

        if (style == PopupStyle.Wooden)
        {
            Background = WoodBackground;
            TextLabel.X = WOOD_INSET_X;
            TextLabel.Y = WOOD_INSET_TOP;
            TextLabel.Width = POPUP_WIDTH - WOOD_INSET_X * 2;
            contentHeight = POPUP_HEIGHT - WOOD_INSET_TOP - WOOD_INSET_BOTTOM;
            TextLabel.Height = contentHeight;
            Scrollbar.Visible = false;
            CloseButton.Visible = false;
        } else
        {
            //scroll and nonscroll are identical per original client re
            Background = DialogBackground;
            TextLabel.X = FRAME_INSET;
            TextLabel.Y = FRAME_INSET;
            TextLabel.Width = POPUP_WIDTH - FRAME_INSET * 2 - ScrollBarControl.DEFAULT_WIDTH;
            TextLabel.Height = contentHeight;
            Scrollbar.Visible = true;
            CloseButton.Visible = true;
        }

        TextLabel.ScrollOffset = 0;
        TextLabel.Text = text;

        var visibleLines = contentHeight / TextRenderer.CHAR_HEIGHT;
        var totalLines = TextLabel.ContentHeight / TextRenderer.CHAR_HEIGHT;
        var scrollMax = Math.Max(0, totalLines - visibleLines);
        Scrollbar.Value = 0;
        Scrollbar.MaxValue = scrollMax;
        Scrollbar.TotalItems = totalLines;
        Scrollbar.VisibleItems = visibleLines;

        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key is Keys.Escape or Keys.Space or Keys.Enter)
        {
            Hide();
            e.Handled = true;
        }
    }

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        if (TextLabel.ContentHeight <= TextLabel.Height)
            return;

        var visibleLines = TextLabel.Height / TextRenderer.CHAR_HEIGHT;
        var totalLines = TextLabel.ContentHeight / TextRenderer.CHAR_HEIGHT;
        var maxScroll = Math.Max(0, totalLines - visibleLines);
        var newValue = Math.Clamp(Scrollbar.Value - e.Delta, 0, maxScroll);

        if (newValue == Scrollbar.Value)
            return;

        Scrollbar.Value = newValue;
        TextLabel.ScrollOffset = newValue * TextRenderer.CHAR_HEIGHT;
        e.Handled = true;
    }
}