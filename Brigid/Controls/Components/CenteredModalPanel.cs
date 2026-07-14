#region
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.Components;

/// <summary>
///     Base for from-scratch (asset-free) centered modals in the modernized-dialog family: a
///     <see cref="DialogPalette" />-framed panel, centered on the viewport, with a title band, a bottom
///     action bar (a <see cref="TextButton" /> Close plus any subclass buttons), Escape-to-dismiss,
///     control-stack participation, and click/wheel absorption so input never leaks to the world behind it.
///     Non-modal in the visual sense (no dimmed backdrop) — it matches the existing
///     <c>WorldListControl</c>/<c>BankShopPanel</c> look, absorbing input via the control stack rather than a
///     modal veil.
///     <para>
///         Subclasses lay their content out inside <see cref="ContentBounds" /> (add it with <c>AddChild</c>),
///         add bottom-bar actions with <see cref="AddBottomBarButton" />, and hang any pre-close teardown off
///         <see cref="OnClose" /> (which also covers the Escape path). Override <see cref="Show" /> to accept
///         data (call <c>base.Show()</c> to push and reveal), <see cref="Hide" /> for teardown, and
///         <see cref="OnMouseScroll" /> to route the wheel to a scroll model.
///     </para>
/// </summary>
public abstract class CenteredModalPanel : UIPanel
{
    protected const int PADDING = 6;

    /// <summary>Height of the title band.</summary>
    protected const int TITLE_H = 20;

    /// <summary>Height of the bottom action bar.</summary>
    protected const int BOTTOM_H = 24;

    private const int BUTTON_H = 18;
    private const int BUTTON_GAP = 6;
    private const int CLOSE_W = 52;

    private readonly UILabel TitleLabel;

    //running right edge for the next bottom-bar button; buttons stack right-to-left from Close.
    private int NextButtonRight;

    //the viewport the panel centers within; defaults to the full virtual screen until SetViewportBounds runs.
    private Rectangle Viewport = new(0, 0, ChaosGame.VIRTUAL_WIDTH, ChaosGame.VIRTUAL_HEIGHT);

    /// <summary>Top of the content area (below the title band and its divider).</summary>
    protected int ContentTop => TITLE_H + 1;

    /// <summary>Top of the bottom action bar.</summary>
    protected int BottomBarTop => Height - BOTTOM_H;

    /// <summary>The content region between the title band and the bottom bar, in panel-local coordinates.</summary>
    protected Rectangle ContentBounds => new(PADDING, ContentTop, Width - 2 * PADDING, BottomBarTop - ContentTop);

    /// <summary>The Close button. Hang teardown off <see cref="OnClose" /> (not this) so Escape is covered too.</summary>
    protected TextButton CloseButton { get; }

    public event CloseHandler? OnClose;

    protected CenteredModalPanel(string title, int width, int height)
    {
        Name = GetType().Name;
        Visible = false;
        UsesControlStack = true;
        Width = width;
        Height = height;
        BackgroundColor = DialogPalette.FrameFill;
        BorderColor = DialogPalette.FrameBorder;
        CenterInViewport();

        TitleLabel = new UILabel
        {
            X = 0,
            Y = (TITLE_H - TextRenderer.CHAR_HEIGHT) / 2,
            Width = width,
            Height = TextRenderer.CHAR_HEIGHT,
            HorizontalAlignment = HorizontalAlignment.Center,
            ForegroundColor = DialogPalette.Title,
            Text = title,
            IsHitTestVisible = false
        };
        AddChild(TitleLabel);

        NextButtonRight = width - PADDING;
        CloseButton = AddBottomBarButton("Close", CLOSE_W, Hide);
    }

    /// <summary>
    ///     Adds a button to the bottom action bar, stacked right-to-left after Close. Returns it so subclasses
    ///     can keep a reference (e.g. to enable/disable per tab).
    /// </summary>
    protected TextButton AddBottomBarButton(string text, int width, ClickedHandler onClick)
    {
        var button = new TextButton(text, width, BUTTON_H)
        {
            X = NextButtonRight - width,
            Y = BottomBarTop + (BOTTOM_H - BUTTON_H) / 2
        };
        button.Clicked += onClick;
        AddChild(button);
        NextButtonRight = button.X - BUTTON_GAP;

        return button;
    }

    protected void SetTitle(string title) => TitleLabel.Text = title;

    /// <summary>Re-centers the panel within an arbitrary viewport rect (typically the HUD's play area).</summary>
    public void SetViewportBounds(Rectangle viewport)
    {
        Viewport = viewport;
        CenterInViewport();
    }

    private void CenterInViewport()
    {
        X = Viewport.X + (Viewport.Width - Width) / 2;
        Y = Viewport.Y + (Viewport.Height - Height) / 2;
    }

    /// <summary>Pushes the panel onto the control stack and reveals it. Idempotent.</summary>
    public virtual void Show()
    {
        if (Visible)
            return;

        CenterInViewport();
        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
    }

    /// <summary>Removes the panel from the control stack, hides it, and raises <see cref="OnClose" />. Idempotent.</summary>
    public virtual void Hide()
    {
        if (!Visible)
            return;

        Visible = false;
        InputDispatcher.Instance?.RemoveControl(this);
        OnClose?.Invoke();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);

        if (!Visible)
            return;

        //dividers framing the title band and the bottom action bar (drawn over the fill, inset by PADDING like
        //the sibling modals).
        DrawDivider(spriteBatch, TITLE_H);
        DrawDivider(spriteBatch, BottomBarTop);
    }

    private void DrawDivider(SpriteBatch spriteBatch, int localY)
        => DrawRectClipped(spriteBatch, new Rectangle(ScreenX + PADDING, ScreenY + localY, Width - 2 * PADDING, 1), DialogPalette.Divider);

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            Hide();
            e.Handled = true;

            return;
        }

        //Tab traverses child textboxes (Macros/Friends tabs).
        base.OnKeyDown(e);

        //a modal captures input: swallow every remaining key so world hotkeys that sit above the
        //control-stack guard (Q/W/E/R panel toggles, Enter chat focus, …) don't fire underneath while it's
        //open. Focused textboxes still receive their keys — explicit-focus (phase 1) dispatch runs before
        //this phase-2 handler. Matches retail, where the options popups blocked other actions.
        e.Handled = true;
    }

    //absorb the wheel so it never scrolls the world behind the modal; subclasses with a scroll list override
    //to forward e.Delta to their scroll model before (or instead of) absorbing.
    public override void OnMouseScroll(MouseScrollEvent e) => e.Handled = true;
}
