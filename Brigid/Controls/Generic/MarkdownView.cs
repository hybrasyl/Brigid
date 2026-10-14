#region
using Brigid.Controls.Components;
using Brigid.Controls.Scrolling;
using Brigid.Data;
using Brigid.Rendering.Markdown;
using Brigid.Rendering.Utility;
using Brigid.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SkiaSharp;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     Large markdown-rendered notice window (HybrasylMessageType.MarkdownNotice = 0x20, a Hybrasyl extension
///     to the retail SystemMessageType channel byte).
///     Covers the world viewport by default; the title-bar maximize button toggles a full-window (640x480) mode,
///     and that choice is deliberately sticky across notices for the session (a new Show() keeps the current mode).
///     Non-modal: the HUD stays interactive, but the panel consumes clicks over itself so nothing reaches the
///     world beneath it. Escape or the close button dismisses. The title is the document's leading level-1
///     heading (extracted, not rendered in the body); a new Show() replaces the current content (latest wins).
/// </summary>
public sealed class MarkdownView : UIPanel
{
    private const int CONTENT_PAD = 6;
    private const int FRAME_INSET = DialogFrame.BORDER_SIZE;
    private const int SCROLL_STEP = 36;
    private const string DEFAULT_TITLE = "Notice";

    //butt001.epf frame indices for the close button (same art as TextPopupControl)
    private const int CLOSE_NORMAL = 3;
    private const int CLOSE_PRESSED = 4;

    private static readonly Color TitleColor = LegendColors.Gold;
    private static readonly SKColor FrameBackgroundColor = new(10, 10, 14, 235);

    private readonly UIButton CloseButton;
    private readonly MarkdownBlock Content;
    private readonly UIButton MaximizeButton;
    private readonly ScrollModel Scroll = new();
    private readonly ScrollBarBinder ScrollBinder;
    private readonly ScrollBarControl Scrollbar;
    private readonly UILabel TitleLabel;

    private bool IsMaximized;
    private int TitleBarHeight;
    private Rectangle ViewportBounds;

    public MarkdownView(GraphicsDevice device, Rectangle viewportBounds)
    {
        Name = "MarkdownView";
        Visible = false;
        UsesControlStack = true;
        ViewportBounds = viewportBounds;

        var cache = UiRenderer.Instance!;
        var closeNormal = cache.GetEpfTexture("butt001.epf", CLOSE_NORMAL);
        var closePressed = cache.GetEpfTexture("butt001.epf", CLOSE_PRESSED);
        TitleBarHeight = Math.Max(TextRenderer.CHAR_HEIGHT + 2, closeNormal.Height + 2);

        TitleLabel = new UILabel
        {
            Name = "Title",
            Height = TextRenderer.CHAR_HEIGHT,
            ForegroundColor = TitleColor,
            ShadowStyle = ShadowStyle.BottomRight,
            IsHitTestVisible = false
        };
        AddChild(TitleLabel);

        CloseButton = new UIButton
        {
            Name = "Close",
            Width = closeNormal.Width,
            Height = closeNormal.Height,
            NormalTexture = closeNormal,
            PressedTexture = closePressed
        };
        CloseButton.Clicked += Hide;
        AddChild(CloseButton);

        //owned by the button — UIButton.Dispose disposes its textures
        var glyphSize = Math.Max(9, closeNormal.Height - 6);

        MaximizeButton = new UIButton
        {
            Name = "Maximize",
            Width = glyphSize,
            Height = glyphSize,
            NormalTexture = ImageUtil.BuildFilledBorder(device, glyphSize, 1, LegendColors.Silver),
            HoverTexture = ImageUtil.BuildFilledBorder(device, glyphSize, 1, LegendColors.White)
        };
        MaximizeButton.Clicked += ToggleMaximize;
        AddChild(MaximizeButton);

        Content = new MarkdownBlock
        {
            Name = "MarkdownContent",
            ExtractTitle = true,
            IsHitTestVisible = false
        };
        Content.LaidOut += OnContentLaidOut;
        AddChild(Content);

        Scrollbar = new ScrollBarControl { Visible = false };
        AddChild(Scrollbar);

        //the layout is pixel-positioned, so the model works in SCROLL_STEP-px chunks ("lines") — the bar's ±1
        //arrow step and each wheel notch then move one chunk, not one pixel. ScrollY derives the pixel offset.
        ScrollBinder = new ScrollBarBinder(Scroll, Scrollbar);
        Scroll.Changed += _ => Content.ScrollY = ScrollY;

        UpdateBounds();
    }

    public event CloseHandler? OnClose;

    /// <summary>Shows the window with new markdown content, replacing whatever is currently displayed.</summary>
    public void Show(string markdown)
    {
        Scroll.ScrollToStart();
        UpdateBounds();
        Content.SetMarkdown(markdown);
        Visible = true;
        InputDispatcher.Instance?.PushControl(this);
    }

    public void Hide()
    {
        if (!Visible)
            return;

        InputDispatcher.Instance?.RemoveControl(this);
        Visible = false;
        OnClose?.Invoke();
    }

    /// <summary>Tracks the active HUD's viewport rect (the non-maximized window bounds).</summary>
    public void SetViewportBounds(Rectangle viewportBounds)
    {
        if (ViewportBounds == viewportBounds)
            return;

        ViewportBounds = viewportBounds;

        if (Visible && !IsMaximized)
        {
            UpdateBounds();
            Content.EnsureLayout();
            SyncScroll();
        }
    }

    private void ToggleMaximize()
    {
        IsMaximized = !IsMaximized;
        UpdateBounds();
        Content.EnsureLayout();
        SyncScroll();
    }

    /// <summary>Applies the current mode's bounds, rebuilds the frame background, and repositions children.</summary>
    private void UpdateBounds()
    {
        var bounds = IsMaximized
            ? new Rectangle(0, 0, ChaosGame.VIRTUAL_WIDTH, ChaosGame.VIRTUAL_HEIGHT)
            : ViewportBounds;

        var sizeChanged = (Width != bounds.Width) || (Height != bounds.Height);
        X = bounds.X;
        Y = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;

        if (sizeChanged || (Background is null))
            RebuildFrameBackground();

        //title bar row, inset past the frame border
        TitleLabel.X = FRAME_INSET;
        TitleLabel.Y = FRAME_INSET + (TitleBarHeight - TitleLabel.Height) / 2;

        CloseButton.X = Width - FRAME_INSET - CloseButton.Width;
        CloseButton.Y = FRAME_INSET + (TitleBarHeight - CloseButton.Height) / 2;

        MaximizeButton.X = CloseButton.X - MaximizeButton.Width - 4;
        MaximizeButton.Y = FRAME_INSET + (TitleBarHeight - MaximizeButton.Height) / 2;

        TitleLabel.Width = MaximizeButton.X - TitleLabel.X - 4;

        //content region below the title bar, scrollbar on the right
        Content.X = FRAME_INSET + CONTENT_PAD;
        Content.Y = FRAME_INSET + TitleBarHeight + CONTENT_PAD;
        Content.Width = Width - FRAME_INSET * 2 - CONTENT_PAD * 2 - ScrollBarControl.DEFAULT_WIDTH;
        Content.Height = Height - Content.Y - FRAME_INSET - CONTENT_PAD;

        Scrollbar.X = Width - FRAME_INSET - ScrollBarControl.DEFAULT_WIDTH;
        Scrollbar.Y = Content.Y;
        Scrollbar.Height = Content.Height;
    }

    private void RebuildFrameBackground()
    {
        var old = Background;
        Background = null;
        old?.Dispose();

        using var composite = DialogFrame.Composite(FrameBackgroundColor, Width, Height);

        if (composite is not null)
            Background = TextureConverter.ToTexture2D(composite);
    }

    //each new layout (new text, a new width, a font or window-scale change) refreshes the title and the scroll range
    private void OnContentLaidOut()
    {
        TitleLabel.Text = Content.Layout?.Title ?? DEFAULT_TITLE;
        SyncScroll();
    }

    private int MaxScroll => Math.Max(0, Content.ContentHeight - Content.Height);

    //pixel offset the renderer uses — derived, so it can't drift from the model's chunk offset
    private int ScrollY => Math.Min(Scroll.Offset * SCROLL_STEP, MaxScroll);

    //feed the model chunk-unit metrics derived from the pixel range. extent = viewport + scrollable-chunks so
    //Max lands exactly on the last (partial) chunk and CanScroll ⇔ MaxScroll > 0; SetMetrics re-clamps the
    //offset and the binder refreshes the bar. Called only when metrics can change (every such path funnels
    //through a new layout), not per frame.
    private void SyncScroll()
    {
        var viewportUnits = Math.Max(1, Content.Height / SCROLL_STEP);
        var maxUnits = (MaxScroll + SCROLL_STEP - 1) / SCROLL_STEP;

        Scroll.SetMetrics(viewportUnits + maxUnits, viewportUnits);
        Scrollbar.Visible = Scroll.CanScroll;
        Content.ScrollY = ScrollY;
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case Keys.Escape:
                Hide();

                break;

            case Keys.PageDown:
                Scroll.PageBy(1);

                break;

            case Keys.PageUp:
                Scroll.PageBy(-1);

                break;

            case Keys.Home:
                Scroll.ScrollToStart();

                break;

            case Keys.End:
                Scroll.ScrollToEnd();

                break;

            default:
                return;
        }

        e.Handled = true;
    }

    public override void OnClick(ClickEvent e)
    {
        //consume clicks so they don't pass through to the world; HUD outside the panel stays interactive
        e.Handled = true;
    }

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        //consume even a no-op notch (content fits): unhandled, the wheel would bubble to the root handler
        //and scroll the chat panel underneath this opaque, click-consuming window — same rule as ScrollView
        if (MaxScroll > 0)
            Scroll.WheelBy(e.Delta);

        e.Handled = true;
    }
}
