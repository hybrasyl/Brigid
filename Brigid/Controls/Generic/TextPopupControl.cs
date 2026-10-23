#region
using Brigid.Controls.Components;
using Brigid.Controls.Scrolling;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     The text popup for the server's ScrollWindow, NonScrollWindow and WoodenBoard messages (BRIG-122). A primitive
///     <see cref="CenteredModalPanel" /> that takes the theme, centered in its viewport. The flat frame and the sign have
///     the retail size (360 by 180); a themed frame grows around the same content area.
///     <para>
///         ScrollWindow and NonScrollWindow are the same, as in the retail client: the "Message" title band, the text in a
///         scroll view, and Close on the bottom bar. WoodenBoard is a wooden sign: no title band, no scroll bar and no
///         Close. A themed sign draws the <see cref="ThemeRoles.FrameSign" /> part; with no part, which includes no theme,
///         it draws the flat frame (the fill and the 1 px border) and keeps the sign's text margins (BRIG-138). The sign
///         reads no legacy art. The mouse wheel scrolls the text in every style. Escape, Space and Enter close the popup.
///     </para>
/// </summary>
public sealed class TextPopupControl : CenteredModalPanel
{
    private const int PANEL_W = 360;
    private const int PANEL_H = 180;

    //the wooden sign's text insets, from the retail client: 16 px left and right, 12 px top, 32 px bottom
    private const int WOOD_INSET_X = 16;
    private const int WOOD_INSET_TOP = 12;
    private const int WOOD_INSET_BOTTOM = 32;

    private readonly UILabel TextLabel;
    private readonly ScrollView TextScroll;

    //the panel size in the framed look; a themed frame makes it larger than the sign. The theme is fixed for the life of
    //the panel, so the size is read once
    private readonly int FramedWidth;
    private readonly int FramedHeight;

    private bool Wooden;

    //the FontEngine generation the scroll range was read at; a new face or window size wraps the text again
    private readonly FontGenerationStamp Wrapped = new();

    public TextPopupControl()
        : base("Message", PANEL_W, PANEL_H, useTheme: true)
    {
        Name = "textpopup";
        IsModal = true;
        FramedWidth = Width;
        FramedHeight = Height;

        TextScroll = new ScrollView { Name = "text" };

        TextLabel = new UILabel
        {
            Name = "message",
            WordWrap = true,
            IsHitTestVisible = false
        };
        TextLabel.SetForegroundRole(ThemeRoles.Text);

        TextScroll.AddChild(TextLabel);
        AddChild(TextScroll);
        TextScroll.SetSource(new LabelScrollSource(TextLabel));
        LayoutContent();
    }

    /// <summary>
    ///     Shows <paramref name="text" /> in the given style, scrolled to the top. When the popup is open, the text and the
    ///     style change and the popup stays open.
    /// </summary>
    public void Show(string text, PopupStyle style = PopupStyle.Scroll)
    {
        Wooden = style == PopupStyle.Wooden;
        ShowsTitleBand = !Wooden;
        CloseButton.Visible = !Wooden;
        TextScroll.Bar.Visible = !Wooden;
        Width = Wooden ? PANEL_W : FramedWidth;
        Height = Wooden ? PANEL_H : FramedHeight;
        LayoutContent();

        TextLabel.Text = text;
        TextScroll.Sync();
        TextScroll.ScrollToStart();
        Wrapped.Mark();

        if (Visible)
        {
            //a second message comes back to the top of the control stack, so that the keys come to it again
            PlaceInViewport();
            InputDispatcher.Instance?.PushControl(this);
        } else
            base.Show();
    }

    //the text fills the content area, or the sign's text area
    protected override void LayoutContent()
    {
        if (Wooden)
        {
            TextScroll.X = WOOD_INSET_X;
            TextScroll.Y = WOOD_INSET_TOP;
            TextScroll.Width = PANEL_W - 2 * WOOD_INSET_X;
            TextScroll.Height = PANEL_H - WOOD_INSET_TOP - WOOD_INSET_BOTTOM;
        } else
        {
            var content = ContentBounds;

            TextScroll.X = content.X;
            TextScroll.Y = content.Y + PADDING;
            TextScroll.Width = content.Width;
            TextScroll.Height = content.Height - 2 * PADDING;
        }

        //the label fills the scroll view, beside the bar when the bar shows
        TextLabel.X = 0;
        TextLabel.Y = 0;
        TextLabel.Width = TextScroll.ContentWidth;
        TextLabel.Height = TextScroll.ContentHeight;

        //a new width wraps the text again, and the scroll range follows
        TextLabel.Text = TextLabel.Text;
        TextScroll.Sync();
    }

    public override void Update(GameTime gameTime)
    {
        //a new wrap changes the number of lines, so the scroll range is read again
        if (Visible && Wrapped.Changed)
        {
            TextLabel.Text = TextLabel.Text;
            TextScroll.Sync();
            Wrapped.Mark();
        }

        base.Update(gameTime);
    }

    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if (!Wooden)
        {
            base.DrawChrome(spriteBatch, bounds);

            return;
        }

        if (TryDrawThemePart(spriteBatch, ThemeRoles.FrameSign, bounds))
            return;

        //with no sign part, the flat frame: not the theme's frame.panel, which has its own insets and title band
        DrawFlatChrome(spriteBatch, bounds);
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        //Alt+Enter changes the window size; it must not close the popup
        if (e.Key is Keys.Escape or Keys.Space || ((e.Key == Keys.Enter) && !e.Alt))
        {
            Hide();
            e.Handled = true;

            return;
        }

        //the base takes every other key
        base.OnKeyDown(e);
    }

    //the wheel scrolls the text from anywhere on the popup, not only over the text
    public override void OnMouseScroll(MouseScrollEvent e)
    {
        TextScroll.OnMouseScroll(e);
        e.Handled = true;
    }
}
