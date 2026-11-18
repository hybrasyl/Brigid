#region
using Brigid.Controls.Components;
using Brigid.Controls.Scrolling;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     The slim scroll bar of the chat box: a track and a thumb, with no arrows. It has its own theme parts
///     (<see cref="ThemeRoles.HudChatScrollTrack" />, <see cref="ThemeRoles.HudChatScrollThumb" />), so a theme can draw it
///     apart from the scroll bars of the windows; with no part it has a flat look. It shows a <see cref="ScrollModel" />
///     whose offset counts from the bottom (<see cref="ScrollModel.Inverted" />), so the thumb is at the bottom at the
///     newest entry. A drag of the thumb sets the offset, and a click on the track above or below it moves one page. The
///     bar does not take the wheel: the view under it scrolls by lines.
/// </summary>
internal sealed class ChatScrollBar : UIPanel
{
    private const int MIN_THUMB = 8;

    //the flat look: a faint track and a thumb in the frame colour
    private static readonly Color FlatTrack = Color.Black * 0.35f;
    private static readonly Color FlatThumb = DialogPalette.FrameBorder * 0.8f;

    private readonly ScrollModel Model;
    private readonly ThumbPanel Thumb;
    private int DragOffset;
    private bool Dragging;

    public ChatScrollBar(ScrollModel model, Rectangle bounds)
    {
        Name = "scroll_bar";
        Model = model;
        SetBounds(bounds);
        BackgroundColor = FlatTrack;

        Thumb = new ThumbPanel(this) { Name = "thumb" };
        AddChild(Thumb);

        Model.Changed += _ => Place();
        Model.MetricsChanged += Place;
        Place();
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);

        if (Thumb is not null)
            Place();
    }

    /// <summary>The thumb's place in the bar: its top and its height. Internal for the tests.</summary>
    internal (int Top, int Height) ThumbPlace => (Thumb.Y, Thumb.Height);

    public override void OnMouseDown(MouseDownEvent e)
    {
        if ((e.Button != MouseButton.Left) || !Model.CanScroll)
            return;

        //a page to older entries above the thumb, to newer ones below it
        Model.ScrollBy(e.ScreenY < ScreenY + Thumb.Y ? Model.Viewport : -Model.Viewport);
        e.Handled = true;
    }

    public override void ResetInteractionState() => Dragging = false;

    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if (TryDrawThemePart(spriteBatch, ThemeRoles.HudChatScrollTrack, bounds))
            return;

        base.DrawChrome(spriteBatch, bounds);
    }

    //the thumb fills the share of the page in the extent; offset 0 (the newest entry) is at the bottom
    private void Place()
    {
        Thumb.Visible = Model.CanScroll;

        if (!Model.CanScroll || (Model.Extent <= 0))
            return;

        var height = Math.Clamp(Height * Model.Viewport / Model.Extent, MIN_THUMB, Height);
        var travel = Height - height;
        var fromTop = Model.Max == 0 ? travel : travel - travel * Model.Offset / Model.Max;

        Thumb.SetBounds(new Rectangle(0, fromTop, Width, height));
    }

    private void StartDrag(int screenY)
    {
        Dragging = true;
        DragOffset = screenY - (ScreenY + Thumb.Y);
    }

    private void Drag(int screenY)
    {
        if (!Dragging)
            return;

        var travel = Height - Thumb.Height;

        if (travel <= 0)
            return;

        var fromTop = Math.Clamp(screenY - DragOffset - ScreenY, 0, travel);
        Model.ScrollTo((int)Math.Round((1f - (float)fromTop / travel) * Model.Max));
    }

    private sealed class ThumbPanel : UIPanel
    {
        private readonly ChatScrollBar Bar;

        public ThumbPanel(ChatScrollBar bar)
        {
            Bar = bar;
            BackgroundColor = FlatThumb;
        }

        public override void OnMouseDown(MouseDownEvent e)
        {
            if (e.Button != MouseButton.Left)
                return;

            Bar.StartDrag(e.ScreenY);
            e.Handled = true;
        }

        public override void OnMouseMove(MouseMoveEvent e) => Bar.Drag(e.ScreenY);

        public override void OnMouseUp(MouseUpEvent e) => Bar.Dragging = false;

        protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (TryDrawThemePart(spriteBatch, ThemeRoles.HudChatScrollThumb, bounds))
                return;

            base.DrawChrome(spriteBatch, bounds);
        }
    }
}
