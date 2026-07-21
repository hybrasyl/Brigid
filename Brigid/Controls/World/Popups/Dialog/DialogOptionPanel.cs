#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Dialog;

/// <summary>
///     Floating option menu panel for NPC dialog/menu interactions. Uses the shared ornate frame with nd_n00/01/02 3-slice
///     option row stripes. Dynamic width, right-aligned and bottom-anchored above the dialog bar. Used for DialogMenu,
///     CreatureMenu, Menu, and MenuWithArgs.
/// </summary>
public sealed class DialogOptionPanel : FramedDialogPanelBase
{
    private const int MIN_STRIPE_WIDTH = 185;
    private const int ROW_HEIGHT = 13;
    private const int STRIPE_GAP = 5;
    private const int TEXT_PADDING_H = 10;
    private const int BTN_WIDTH = 61;
    private const int BTN_HEIGHT = 22;

    //border thickness from edge tiles (content measured from inside of these)
    private const int BORDER_TOP = 6;
    private const int BORDER_LEFT = 13;
    private const int BORDER_RIGHT = 31;
    private const int BORDER_BOTTOM = 47;

    //content padding from inside of border to stripes
    private const int CONTENT_PADDING_TOP = 2;
    private const int CONTENT_PADDING_BOTTOM = -16;
    private const int CONTENT_PADDING_LEFT = 7;
    private const int CONTENT_PADDING_RIGHT = -11;

    //bottom of panel aligns with top of the dialog bottom bar
    private const int BOTTOM_ANCHOR_Y = 372;

    private readonly List<OptionLabel> OptionLabels = [];

    //non-modal timeout notice (retail's empty-bank withdraw returns no packet at all). Drawn attached to the top
    //of this panel and gated by its Visibility, so it disappears automatically when the bank renders and hides the
    //menu. Auto-fades after NOTICE_DURATION_S and clears on the next action (option click, key, hide, re-show).
    private const float NOTICE_DURATION_S = 5f;
    private const float NOTICE_FADE_S = 1f;
    private string? NoticeText;
    private float NoticeElapsedS;

    //3-slice stripe pieces
    private Texture2D? StripeLeft;
    private Texture2D? StripeLeftOn;
    private Texture2D? StripeMid;
    private Texture2D? StripeMidOn;
    private Texture2D? StripeRight;
    private Texture2D? StripeRightOn;
    private bool StripesLoaded;

    public DialogOptionPanel()
        : base("lnpcd2", false)
    {
        Name = "OptionMenu";
        Visible = false;
        
        OkButton = CreateButton("Btn1");

        OkButton?.Enabled = false;
    }

    private void ClearOptionLabels()
    {
        foreach (var label in OptionLabels)
        {
            Children.Remove(label);
            label.Dispose();
        }

        OptionLabels.Clear();
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (NoticeText is null)
            return;

        NoticeElapsedS += (float)gameTime.ElapsedGameTime.TotalSeconds;

        if (NoticeElapsedS >= NOTICE_DURATION_S)
            NoticeText = null;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        EnsureStripeTextures();

        //frame + children drawn by base
        base.Draw(spriteBatch);

        DrawNotice(spriteBatch);
    }

    //draws the timeout notice as a bordered, wrapped-text box attached just above this panel's top edge. The box is
    //draw-only (no child element / hit target), so it stays non-modal and can't intercept input.
    private void DrawNotice(SpriteBatch spriteBatch)
    {
        if (NoticeText is null)
            return;

        //hold full opacity, then fade over the final NOTICE_FADE_S so it eases out instead of blinking off
        var remaining = NOTICE_DURATION_S - NoticeElapsedS;
        var alpha = remaining >= NOTICE_FADE_S ? 1f : Math.Clamp(remaining / NOTICE_FADE_S, 0f, 1f);

        const int pad = 6;
        const int gap = 4;

        var wrapWidth = Math.Max(Width - 2 * pad, 120);
        var lines = TextRenderer.WrapText(NoticeText, wrapWidth);
        var boxHeight = lines.Count * TextRenderer.CHAR_HEIGHT + 2 * pad;
        var boxX = ScreenX;
        var boxY = Math.Max(2, ScreenY - boxHeight - gap);

        DrawBorderedRect(
            spriteBatch,
            new Rectangle(boxX, boxY, Width, boxHeight),
            new Color(24, 20, 16) * alpha,
            new Color(198, 160, 74) * alpha);

        var textY = boxY + pad;

        foreach (var line in lines)
        {
            TextRenderer.DrawText(spriteBatch, new Vector2(boxX + pad, textY), line, LegendColors.White * alpha);
            textY += TextRenderer.CHAR_HEIGHT;
        }
    }

    private void EnsureStripeTextures()
    {
        if (StripesLoaded)
            return;

        StripesLoaded = true;
        var renderer = UiRenderer.Instance;

        if (renderer is null)
            return;

        StripeLeft = renderer.GetSpfTexture("nd_n00.spf");
        StripeMid = renderer.GetSpfTexture("nd_n01.spf");
        StripeRight = renderer.GetSpfTexture("nd_n02.spf");
        StripeLeftOn = renderer.GetSpfTexture("nd_n00on.spf");
        StripeMidOn = renderer.GetSpfTexture("nd_n01on.spf");
        StripeRightOn = renderer.GetSpfTexture("nd_n02on.spf");
    }

    public int OptionCount => OptionLabels.Count;

    public ushort GetOptionPursuitId(int index)
    {
        if ((index < 0) || (index >= OptionLabels.Count))
            return 0;

        return OptionLabels[index].PursuitId;
    }

    /// <summary>The display text of the option at <paramref name="index" />, or null when out of range.</summary>
    public string? GetOptionText(int index) =>
        (index >= 0) && (index < OptionLabels.Count) ? OptionLabels[index].Text : null;

    /// <summary>Shows a soft, non-modal notice attached to the top of the menu; auto-fades and clears on next action.</summary>
    public void ShowNotice(string text)
    {
        NoticeText = text;
        NoticeElapsedS = 0f;
    }

    public void ClearNotice() => NoticeText = null;

    public override void Hide()
    {
        ClearOptionLabels();
        ClearNotice();
        base.Hide();
    }

    public event CloseHandler? OnClose;

    public event OptionSelectedHandler? OnOptionSelected;

    public void ShowOptions(IReadOnlyList<(string Text, ushort Pursuit)> options)
    {
        ClearOptionLabels();
        ClearNotice();
        EnsureStripeTextures();

        //dynamic width from longest option text
        var maxTextWidth = 0;

        foreach ((var text, _) in options)
        {
            var textWidth = TextRenderer.MeasureWidth(text);

            if (textWidth > maxTextWidth)
                maxTextWidth = textWidth;
        }

        var stripeWidth = maxTextWidth + TEXT_PADDING_H * 2;
        stripeWidth = Math.Max(stripeWidth, MIN_STRIPE_WIDTH);

        var stripeLeft = BORDER_LEFT + CONTENT_PADDING_LEFT;
        var stripeRight = BORDER_RIGHT + CONTENT_PADDING_RIGHT;

        Width = stripeWidth + stripeLeft + stripeRight;

        //dynamic height: top border + padding + stripes + padding + bottom border
        var stripesHeight = options.Count * (ROW_HEIGHT + STRIPE_GAP) - STRIPE_GAP;
        Height = BORDER_TOP + CONTENT_PADDING_TOP + stripesHeight + CONTENT_PADDING_BOTTOM + BORDER_BOTTOM;

        //right-aligned, bottom-anchored above dialog bar
        X = ChaosGame.VIRTUAL_WIDTH - Width;
        Y = BOTTOM_ANCHOR_Y - Height;

        //create option label children
        for (var i = 0; i < options.Count; i++)
        {
            (var text, var pursuit) = options[i];
            var index = i;

            var label = new OptionLabel(
                text,
                pursuit,
                StripeLeft,
                StripeMid,
                StripeRight,
                StripeLeftOn,
                StripeMidOn,
                StripeRightOn)
            {
                Name = $"Option_{i}",
                X = BORDER_LEFT + CONTENT_PADDING_LEFT,
                Y = BORDER_TOP + CONTENT_PADDING_TOP + i * (ROW_HEIGHT + STRIPE_GAP),
                Width = stripeWidth,
                Height = ROW_HEIGHT
            };

            label.Clicked += () =>
            {
                ClearNotice();
                OnOptionSelected?.Invoke(index);
            };
            OptionLabels.Add(label);
            AddChild(label);
        }

        //ok button: 20px from right outer edge, 4px from bottom outer edge, shown as disabled
        if (OkButton is not null)
        {
            OkButton.X = Width - BTN_WIDTH - 20;
            OkButton.Y = Height - BTN_HEIGHT - 3;
        }

        Visible = true;
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        ClearNotice();

        if (e.Key == Keys.Escape)
        {
            Hide();
            OnClose?.Invoke();
            e.Handled = true;
        }
    }

    /// <summary>
    ///     A single clickable text option. Draws a 3-slice dark stripe background (nd_n00 left + nd_n01 tiled + nd_n02 right)
    ///     with centered text.
    /// </summary>
    private sealed class OptionLabel : UIElement
    {
        private readonly Texture2D? StripeLeft;
        private readonly Texture2D? StripeLeftOn;
        private readonly Texture2D? StripeMid;
        private readonly Texture2D? StripeMidOn;
        private readonly Texture2D? StripeRight;
        private readonly Texture2D? StripeRightOn;
        private readonly TextElement TextCache = new();
        private readonly int TextWidth;
        private bool IsHovered;
        private bool IsPressed;
        public ushort PursuitId { get; }
        public string Text { get; }

        public OptionLabel(
            string text,
            ushort pursuitId,
            Texture2D? stripeLeft,
            Texture2D? stripeMid,
            Texture2D? stripeRight,
            Texture2D? stripeLeftOn,
            Texture2D? stripeMidOn,
            Texture2D? stripeRightOn)
        {
            Text = text;
            PursuitId = pursuitId;
            StripeLeft = stripeLeft;
            StripeMid = stripeMid;
            StripeRight = stripeRight;
            StripeLeftOn = stripeLeftOn;
            StripeMidOn = stripeMidOn;
            StripeRightOn = stripeRightOn;
            TextWidth = TextRenderer.MeasureWidth(text);
            TextCache.Update(text, LegendColors.Silver);
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible)
                return;

            base.Draw(spriteBatch);

            //pressed state shifts down 1px and contracts 1px on each side. expand ClipRect
            //downward so the stripe textures' bottom border row (now at sy+textureHeight)
            //isn't clipped by the OptionLabel's nominal bounds — otherwise the BL/BR ends
            //of the bottom border vanish (StripeMid uses TileTexture which bypasses ClipRect,
            //but DrawTexture for left/right pieces clips to ClipRect).
            if (IsPressed)
                ClipRect.Height += 1;

            var sx = ScreenX + (IsPressed ? 1 : 0);
            var sy = ScreenY + (IsPressed ? 1 : 0);
            var drawWidth = Width - (IsPressed ? 2 : 0);

            //draw 3-slice stripe background
            var left = IsHovered ? StripeLeftOn ?? StripeLeft : StripeLeft;
            var mid = IsHovered ? StripeMidOn ?? StripeMid : StripeMid;
            var right = IsHovered ? StripeRightOn ?? StripeRight : StripeRight;

            var leftW = left?.Width ?? 0;
            var rightW = right?.Width ?? 0;
            var midWidth = drawWidth - leftW - rightW;

            if (left is not null)
                DrawTexture(
                    spriteBatch,
                    left,
                    new Vector2(sx, sy),
                    Color.White);

            if (mid is not null)
                TileTexture(
                    spriteBatch,
                    mid,
                    sx + leftW,
                    sy,
                    midWidth,
                    mid.Height);

            if (right is not null)
                DrawTexture(
                    spriteBatch,
                    right,
                    new Vector2(sx + drawWidth - rightW, sy),
                    Color.White);

            //centered text on top — center against full Width (not drawWidth) so the press shift
            //carries through; otherwise the contracted drawWidth cancels out the sx+1 offset.
            TextCache.Update(Text, Color.White);

            var textX = sx + (Width - TextWidth) / 2;
            var textY = sy + (Height - TextRenderer.CHAR_HEIGHT) / 2 + 1;

            TextCache.Draw(spriteBatch, new Vector2(textX, textY));
        }

        public event ClickedHandler? Clicked;

        public override void OnMouseEnter()
        {
            IsHovered = true;
        }

        public override void OnMouseLeave()
        {
            IsHovered = false;
            IsPressed = false;
        }

        public override void OnMouseDown(MouseDownEvent e)
        {
            if (e.Button == MouseButton.Left)
            {
                IsPressed = true;
                e.Handled = true;
            }
        }

        public override void OnMouseUp(MouseUpEvent e)
        {
            if (e.Button == MouseButton.Left)
                IsPressed = false;
        }

        public override void OnClick(ClickEvent e)
        {
            Clicked?.Invoke();
            e.Handled = true;
        }
    }
}