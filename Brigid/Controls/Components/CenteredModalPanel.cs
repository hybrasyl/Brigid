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
public abstract class CenteredModalPanel : FramePanel
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

    //bottom-bar buttons in creation order (Close first), so RelayoutBottomBar can re-pack them.
    private readonly List<TextButton> BottomBarButtons = [];

    //the viewport the panel centers within; defaults to the full virtual screen until SetViewportBounds runs.
    private Rectangle Viewport = new(0, 0, ChaosGame.VIRTUAL_WIDTH, ChaosGame.VIRTUAL_HEIGHT);

    //the inset the title, the dividers and the bottom bar were last placed for; the constructor places them flat
    private int LaidOutInset = PADDING;

    /// <summary>
    ///     The distance from the panel edge to the content: <see cref="PADDING" />, or more inside a themed frame whose border
    ///     art is wider. The title band and the bottom bar move in by the same extra amount.
    /// </summary>
    protected int Inset => InsetFor(PADDING);

    //how far the title band and the bottom bar sit from the top and bottom edges: 0 for the flat frame
    private int ChromeOffset => Inset - PADDING;

    /// <summary>Top of the content area (below the title band and its divider).</summary>
    protected int ContentTop => ChromeOffset + TITLE_H + 1;

    /// <summary>Top of the bottom action bar.</summary>
    protected int BottomBarTop => Height - ChromeOffset - BOTTOM_H;

    /// <summary>The content region between the title band and the bottom bar, in panel-local coordinates.</summary>
    protected Rectangle ContentBounds => new(Inset, ContentTop, Width - 2 * Inset, BottomBarTop - ContentTop);

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
        CenterInViewport();

        TitleLabel = new UILabel
        {
            Name = "title",
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

        CloseButton = AddBottomBarButton("Close", CLOSE_W, Hide, "close");
    }

    /// <summary>
    ///     Adds a button to the bottom action bar, stacked right-to-left after Close. Returns it so subclasses
    ///     can keep a reference (e.g. to enable/disable per tab). Give a <paramref name="name" /> of <c>[a-z0-9_]</c> so
    ///     that a later <c>ui_panels</c> pack can address the button.
    /// </summary>
    protected TextButton AddBottomBarButton(string text, int width, ClickedHandler onClick, string? name = null)
    {
        var button = new TextButton(text, width, BUTTON_H)
        {
            Name = name ?? string.Empty,
            Y = BottomBarTop + (BOTTOM_H - BUTTON_H) / 2
        };
        button.Clicked += onClick;
        BottomBarButtons.Add(button);
        AddChild(button);
        RelayoutBottomBar();

        return button;
    }

    /// <summary>
    ///     Declares which optional actions the bar shows right now: every button in <paramref name="visible" /> is
    ///     shown, every other subclass-added button is hidden, and the bar is re-packed. Close is always kept.
    ///     Subclasses whose action set varies per view call this on each view change, so the bar can never end up
    ///     with a hole where a hidden button used to sit.
    /// </summary>
    protected void SetBottomBarActions(params TextButton[] visible)
    {
        foreach (var button in BottomBarButtons)
            if (!ReferenceEquals(button, CloseButton))
                button.Visible = Array.IndexOf(visible, button) >= 0;

        RelayoutBottomBar();
    }

    /// <summary>
    ///     Re-packs the <b>visible</b> bottom-bar buttons right-to-left in creation order (Close rightmost). Called
    ///     for you by <see cref="SetBottomBarActions" /> and <see cref="AddBottomBarButton" />; call it directly
    ///     only after changing a button's <c>Width</c>.
    /// </summary>
    protected void RelayoutBottomBar()
    {
        //the bar shares the bottom row with the frame's corner art, which is wider than its edge, so it moves in by the
        //same extra amount as the title band moves down (nothing for the flat frame)
        var right = Width - Inset - ChromeOffset;

        foreach (var button in BottomBarButtons)
        {
            if (!button.Visible)
                continue;

            button.X = right - button.Width;
            right = button.X - BUTTON_GAP;
        }
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

    /// <summary>
    ///     Places the title, the bottom bar and the content again when <see cref="Inset" /> changed: a panel becomes themed
    ///     when it is added under a themed parent, after its constructor laid it out flat.
    /// </summary>
    private void EnsureFrameLayout()
    {
        if (Inset == LaidOutInset)
            return;

        LaidOutInset = Inset;
        TitleLabel.Y = ChromeOffset + (TITLE_H - TextRenderer.CHAR_HEIGHT) / 2;

        foreach (var button in BottomBarButtons)
            button.Y = BottomBarTop + (BOTTOM_H - BUTTON_H) / 2;

        RelayoutBottomBar();
        LayoutContent();
    }

    /// <summary>
    ///     Called when <see cref="ContentBounds" /> changed after the constructor, because the frame border changed. A
    ///     subclass whose content is placed from <see cref="ContentBounds" /> places it again here.
    /// </summary>
    protected virtual void LayoutContent() { }

    public override void Update(GameTime gameTime)
    {
        if (Visible)
            EnsureFrameLayout();

        base.Update(gameTime);
    }

    /// <summary>Pushes the panel onto the control stack and reveals it. Idempotent.</summary>
    public virtual void Show()
    {
        if (Visible)
            return;

        EnsureFrameLayout();
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
        TitleLabel.ForegroundColor = ThemeColor(ThemeRoles.TextTitle, DialogPalette.Title);

        base.Draw(spriteBatch);

        if (!Visible)
            return;

        //dividers framing the title band and the bottom action bar (drawn over the fill, inset by PADDING like
        //the sibling modals).
        DrawDivider(spriteBatch, ChromeOffset + TITLE_H);
        DrawDivider(spriteBatch, BottomBarTop);
    }

    private void DrawDivider(SpriteBatch spriteBatch, int localY)
        => DrawRectClipped(
            spriteBatch,
            new Rectangle(ScreenX + Inset, ScreenY + localY, Width - 2 * Inset, 1),
            ThemeColor(ThemeRoles.Divider, DialogPalette.Divider));

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
