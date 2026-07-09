#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     Modern-flat scrollbar drawn entirely from primitives (no sprite assets). Supports vertical (up/down) and
///     horizontal (left/right) orientations. Fixed 16px arrow buttons at each end, a recessed track groove, and a
///     <b>proportional</b> thumb sized to the visible fraction of the content. 5 hit zones: decrement arrow, page
///     decrement, thumb (draggable), page increment, increment arrow. The public surface (Value/MaxValue/TotalItems/
///     VisibleItems/Orientation/OnValueChanged) is unchanged from the legacy sprite version — Value is item-granular.
/// </summary>
public sealed class ScrollBarControl : UIElement
{
    public const int DEFAULT_WIDTH = 16;

    private const int ARROW_SIZE = 16;
    private const int MIN_THUMB = 16;
    private const int ARROW_PAD = 4;

    //two-phase auto-repeat: a long initial delay before the fast repeat interval kicks in
    private const float REPEAT_INITIAL_MS = 400f;
    private const float REPEAT_INTERVAL_MS = 50f;

    //── modern-flat palette (tune during run-through) ──
    private static readonly Color TrackColor = new(24, 24, 28, 235);
    private static readonly Color TrackBorderColor = new(150, 150, 160, 255);
    private static readonly Color ThumbIdleColor = new(120, 120, 130, 255);
    private static readonly Color ThumbHoverColor = new(150, 150, 160, 255);
    private static readonly Color ThumbPressColor = new(186, 162, 110, 255);
    private static readonly Color ArrowIdleColor = new(200, 200, 210, 255);
    private static readonly Color ArrowHoverColor = new(230, 230, 235, 255);
    private static readonly Color ArrowPressColor = new(186, 162, 110, 255);
    private static readonly Color DisabledColor = new(70, 70, 78, 255);

    private ScrollZone ActiveZone = ScrollZone.None;
    private ScrollZone HoveredZone = ScrollZone.None;

    private bool Dragging;
    private int DragOffset;
    private int DragThumbSize;
    private int RepeatMain;
    private float RepeatTimer;
    private bool RepeatPrimed;

    public int MaxValue { get; set; }
    public ScrollOrientation Orientation { get; set; } = ScrollOrientation.Vertical;
    public int TotalItems { get; set; }
    public int Value { get; set; }
    public int VisibleItems { get; set; }

    public ScrollBarControl() => Width = DEFAULT_WIDTH;

    private bool IsHorizontal => Orientation == ScrollOrientation.Horizontal;

    private bool Scrollable => TotalItems > VisibleItems;

    public event ScrollValueChangedHandler? OnValueChanged;

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        UpdateClipRect();

        if ((ClipRect.Width <= 0) || (ClipRect.Height <= 0))
            return;

        ComputeGeometry(out var trackStart, out var trackEnd, out var usableTrack, out var thumbSize, out var thumbOffset);

        //track groove
        if (usableTrack > 0)
        {
            var trackRect = MainRect(trackStart, usableTrack);

            DrawRectClipped(spriteBatch, trackRect, TrackColor);
            DrawBorderClipped(spriteBatch, trackRect, TrackBorderColor);
        }

        //arrow buttons — decrement at the start, increment at the end
        DrawArrow(spriteBatch, MainRect(trackStart - ARROW_SIZE, ARROW_SIZE), IsHorizontal ? ArrowDir.Left : ArrowDir.Up, ArrowColor(ScrollZone.DecArrow));
        DrawArrow(spriteBatch, MainRect(trackEnd, ARROW_SIZE), IsHorizontal ? ArrowDir.Right : ArrowDir.Down, ArrowColor(ScrollZone.IncArrow));

        //proportional thumb — only when there is something to scroll
        if (Scrollable && (thumbSize > 0))
        {
            var thumbRect = MainRect(thumbOffset, thumbSize);

            DrawRectClipped(spriteBatch, thumbRect, ThumbColor());
            DrawBorderClipped(spriteBatch, thumbRect, TrackBorderColor);
        }
    }

    //builds a bounds rect from a main-axis position/size; the cross axis is the fixed 16px strip
    private Rectangle MainRect(int mainPos, int mainSize)
        => IsHorizontal
            ? new Rectangle(mainPos, ScreenY, mainSize, ARROW_SIZE)
            : new Rectangle(ScreenX, mainPos, ARROW_SIZE, mainSize);

    //single axis-parameterized geometry — the sole source of truth for track/thumb bounds, shared by draw + input
    private void ComputeGeometry(out int trackStart, out int trackEnd, out int usableTrack, out int thumbSize, out int thumbOffset)
    {
        var mainStart = IsHorizontal ? ScreenX : ScreenY;
        var mainSize = IsHorizontal ? Width : Height;

        trackStart = mainStart + ARROW_SIZE;
        trackEnd = mainStart + mainSize - ARROW_SIZE;
        usableTrack = Math.Max(0, trackEnd - trackStart);

        if ((TotalItems > 0) && (usableTrack > 0))
        {
            var raw = (int)((float)VisibleItems / TotalItems * usableTrack);

            thumbSize = Math.Clamp(raw, Math.Min(MIN_THUMB, usableTrack), usableTrack);
        } else
            thumbSize = 0;

        var travel = usableTrack - thumbSize;
        var ratio = MaxValue > 0 ? (float)Value / MaxValue : 0f;

        thumbOffset = trackStart + (int)(ratio * travel);
    }

    private ScrollZone ZoneAt(int m, int trackStart, int trackEnd, int thumbOffset, int thumbSize)
    {
        if (m < trackStart)
            return ScrollZone.DecArrow;

        if (m >= trackEnd)
            return ScrollZone.IncArrow;

        if ((m >= thumbOffset) && (m < (thumbOffset + thumbSize)))
            return ScrollZone.Thumb;

        return m < thumbOffset ? ScrollZone.PageDec : ScrollZone.PageInc;
    }

    private Color ArrowColor(ScrollZone zone)
    {
        if (!Scrollable)
            return DisabledColor;

        if (ActiveZone == zone)
            return ArrowPressColor;

        return HoveredZone == zone ? ArrowHoverColor : ArrowIdleColor;
    }

    private Color ThumbColor()
    {
        if ((ActiveZone == ScrollZone.Thumb) || Dragging)
            return ThumbPressColor;

        return HoveredZone == ScrollZone.Thumb ? ThumbHoverColor : ThumbIdleColor;
    }

    //filled triangle drawn as stacked 1px bars inside a padded glyph box; apex points in the given direction
    private void DrawArrow(SpriteBatch spriteBatch, Rectangle button, ArrowDir dir, Color color)
    {
        var gx = button.X + ARROW_PAD;
        var gy = button.Y + ARROW_PAD;
        var gw = button.Width - (2 * ARROW_PAD);
        var gh = button.Height - (2 * ARROW_PAD);

        if ((gw <= 0) || (gh <= 0))
            return;

        if (dir is ArrowDir.Up or ArrowDir.Down)
        {
            var cx = gx + (gw / 2);

            for (var r = 0; r < gh; r++)
            {
                var t = gh > 1 ? (float)(dir == ArrowDir.Up ? r : gh - 1 - r) / (gh - 1) : 1f;
                var half = (int)(t * (gw / 2f));

                DrawRectClipped(spriteBatch, new Rectangle(cx - half, gy + r, (half * 2) + 1, 1), color);
            }
        } else
        {
            var cy = gy + (gh / 2);

            for (var c = 0; c < gw; c++)
            {
                var t = gw > 1 ? (float)(dir == ArrowDir.Left ? c : gw - 1 - c) / (gw - 1) : 1f;
                var half = (int)(t * (gh / 2f));

                DrawRectClipped(spriteBatch, new Rectangle(gx + c, cy - half, 1, (half * 2) + 1), color);
            }
        }
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (Dragging || ActiveZone is not (ScrollZone.DecArrow or ScrollZone.PageDec or ScrollZone.PageInc or ScrollZone.IncArrow))
            return;

        RepeatTimer += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        var threshold = RepeatPrimed ? REPEAT_INTERVAL_MS : REPEAT_INITIAL_MS;

        if (RepeatTimer < threshold)
            return;

        RepeatTimer -= threshold;
        RepeatPrimed = true;

        //stop a held page-repeat once the thumb has travelled to the cursor (avoids overshooting to the clamp)
        if (ActiveZone is ScrollZone.PageDec or ScrollZone.PageInc && PageRepeatReachedCursor())
            return;

        ApplyZoneStep(ActiveZone);
    }

    private bool PageRepeatReachedCursor()
    {
        ComputeGeometry(out _, out _, out _, out var thumbSize, out var thumbOffset);

        return ActiveZone == ScrollZone.PageDec ? thumbOffset <= RepeatMain : RepeatMain < (thumbOffset + thumbSize);
    }

    private void ApplyZoneStep(ScrollZone zone)
    {
        var page = Math.Max(1, VisibleItems - 1);

        var delta = zone switch
        {
            ScrollZone.DecArrow => -1,
            ScrollZone.IncArrow => 1,
            ScrollZone.PageDec => -page,
            ScrollZone.PageInc => page,
            _ => 0
        };

        if (delta == 0)
            return;

        var newValue = Math.Clamp(Value + delta, 0, MaxValue);

        if (newValue == Value)
            return;

        Value = newValue;
        OnValueChanged?.Invoke(Value);
    }

    public override void OnMouseDown(MouseDownEvent e)
    {
        if ((e.Button != MouseButton.Left) || !Scrollable)
            return;

        ComputeGeometry(out var trackStart, out var trackEnd, out _, out var thumbSize, out var thumbOffset);

        var m = IsHorizontal ? e.ScreenX : e.ScreenY;
        var zone = ZoneAt(m, trackStart, trackEnd, thumbOffset, thumbSize);

        ActiveZone = zone;
        RepeatTimer = 0;
        RepeatPrimed = false;
        RepeatMain = m;

        if (zone == ScrollZone.Thumb)
        {
            Dragging = true;
            DragThumbSize = thumbSize;
            DragOffset = m - thumbOffset;
        } else
            ApplyZoneStep(zone);

        e.Handled = true;
    }

    public override void OnMouseMove(MouseMoveEvent e)
    {
        var m = IsHorizontal ? e.ScreenX : e.ScreenY;

        if (Dragging)
        {
            ComputeGeometry(out var trackStart, out _, out var usableTrack, out _, out _);

            var travel = usableTrack - DragThumbSize;

            if (travel > 0)
            {
                var ratio = Math.Clamp((float)(m - DragOffset - trackStart) / travel, 0f, 1f);
                var newValue = (int)Math.Round(ratio * MaxValue);

                if (newValue != Value)
                {
                    Value = newValue;
                    OnValueChanged?.Invoke(Value);
                }
            }

            return;
        }

        //hover feedback
        ComputeGeometry(out var ts, out var te, out _, out var thumbSize, out var thumbOffset);

        HoveredZone = Scrollable ? ZoneAt(m, ts, te, thumbOffset, thumbSize) : ScrollZone.None;
    }

    public override void OnMouseLeave() => HoveredZone = ScrollZone.None;

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        if (!Scrollable)
            return;

        var newValue = Math.Clamp(Value - e.Delta, 0, MaxValue);

        if (newValue != Value)
        {
            Value = newValue;
            OnValueChanged?.Invoke(Value);
        }

        e.Handled = true;
    }

    public override void OnMouseUp(MouseUpEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        Dragging = false;
        ActiveZone = ScrollZone.None;
        RepeatTimer = 0;
        RepeatPrimed = false;
    }

    //clears transient interaction state when a parent panel hides (fixes a bar hidden mid-drag staying stuck)
    public override void ResetInteractionState()
    {
        Dragging = false;
        ActiveZone = ScrollZone.None;
        HoveredZone = ScrollZone.None;
        RepeatTimer = 0;
        RepeatPrimed = false;
    }

    private enum ArrowDir
    {
        Up,
        Down,
        Left,
        Right
    }

    private enum ScrollZone
    {
        None = -1,
        DecArrow = 0,
        PageDec = 1,
        Thumb = 2,
        PageInc = 3,
        IncArrow = 4
    }
}
