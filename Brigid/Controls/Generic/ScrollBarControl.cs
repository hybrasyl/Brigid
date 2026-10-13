#region
using Brigid.Controls.Components;
using Brigid.Rendering;
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
///     <para>
///         A themed vertical bar draws the <c>ui_theme</c> parts <c>scrollbar.track</c>, <c>scrollbar.thumb</c> (with
///         <c>.hover</c> and <c>.pressed</c>), <c>scrollbar.up</c> and <c>scrollbar.down</c> (with <c>.pressed</c>). Each
///         piece that the theme does not have keeps its own look. A horizontal bar is not themed.
///     </para>
/// </summary>
public sealed class ScrollBarControl : UIElement
{
    public const int DEFAULT_WIDTH = 16;

    private const int ARROW_SIZE = 16;
    private const int MIN_THUMB = 16;
    private const int ARROW_PAD = 4;

    //the themed thumb sits inside the track frame, this far from each long edge
    private const int THEMED_THUMB_INSET = 2;

    //two-phase auto-repeat: a long initial delay before the fast repeat interval kicks in
    private const float REPEAT_INITIAL_MS = 400f;
    private const float REPEAT_INTERVAL_MS = 50f;

    //── modern-flat palette (tune during run-through) ──
    //── wood palette, sourced from the HUD wood tone (#966946 base) — no cool greys ──
    private static readonly Color TrackColor = new(32, 22, 16, 235);        //#201610 — dark warm recess
    private static readonly Color TrackBorderColor = new(150, 105, 70, 255); //#966946 — wood outline
    private static readonly Color ThumbIdleColor = new(128, 90, 60, 255);   //#805A3C
    private static readonly Color ThumbHoverColor = new(176, 133, 90, 255);  //#B0855A — brightens on hover
    private static readonly Color ThumbPressColor = new(107, 72, 48, 255);   //#6B4830 — darker, pressed/inset
    private static readonly Color ArrowIdleColor = new(200, 164, 120, 255);  //#C8A478 (primitive-fallback glyph)
    private static readonly Color ArrowHoverColor = new(224, 195, 152, 255); //#E0C398
    private static readonly Color ArrowPressColor = new(150, 105, 70, 255);  //#966946
    private static readonly Color DisabledColor = new(92, 65, 48, 255);      //#5C4130 — muted dark wood

    //── legacy sprite cover (temporary) ──
    //Draw real scroll.epf art over the primitive scaffold until the primitive look is finalized. Flip UseSprites to
    //false to see the pure-primitive rendering. scroll.epf frame order: left(0,1), right(2,3), up(4,5), down(6,7),
    //thumb(8), track(9) — each arrow pair is [normal, depressed]. The track stays primitive; the thumb sprite is
    //stretched to the proportional thumb size.
    private static readonly bool UseSprites = true;
    private const string SCROLL_EPF = "scroll.epf";
    private const int FRAME_LEFT_NORMAL = 0;
    private const int FRAME_LEFT_ACTIVE = 1;
    private const int FRAME_RIGHT_NORMAL = 2;
    private const int FRAME_RIGHT_ACTIVE = 3;
    private const int FRAME_UP_NORMAL = 4;
    private const int FRAME_UP_ACTIVE = 5;
    private const int FRAME_DOWN_NORMAL = 6;
    private const int FRAME_DOWN_ACTIVE = 7;

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
            DrawTrack(spriteBatch, MainRect(trackStart, usableTrack));

        //arrow buttons — decrement at the start, increment at the end
        DrawArrowButton(spriteBatch, MainRect(trackStart - ARROW_SIZE, ARROW_SIZE), ScrollZone.DecArrow);
        DrawArrowButton(spriteBatch, MainRect(trackEnd, ARROW_SIZE), ScrollZone.IncArrow);

        //proportional thumb — only when there is something to scroll
        if (Scrollable && (thumbSize > 0))
            DrawThumb(spriteBatch, MainRect(thumbOffset, thumbSize));
    }

    //the theme parts apply to a vertical bar only; the art is drawn upright
    private bool TryDrawPart(SpriteBatch spriteBatch, string role, Rectangle bounds, string? fallbackRole = null)
        => !IsHorizontal && TryDrawThemePart(spriteBatch, role, bounds, fallbackRole);

    private void DrawTrack(SpriteBatch spriteBatch, Rectangle trackRect)
    {
        if (TryDrawPart(spriteBatch, ThemeRoles.ScrollTrack, trackRect))
            return;

        DrawRectClipped(spriteBatch, trackRect, TrackColor);
        DrawBorderClipped(spriteBatch, trackRect, TrackBorderColor);
    }

    private void DrawThumb(SpriteBatch spriteBatch, Rectangle thumbRect)
    {
        var role = (ActiveZone == ScrollZone.Thumb) || Dragging
            ? ThemeRoles.ScrollThumbPressed
            : HoveredZone == ScrollZone.Thumb ? ThemeRoles.ScrollThumbHover : ThemeRoles.ScrollThumb;

        var inset = new Rectangle(thumbRect.X + THEMED_THUMB_INSET, thumbRect.Y, thumbRect.Width - 2 * THEMED_THUMB_INSET, thumbRect.Height);

        if (TryDrawPart(spriteBatch, role, inset, ThemeRoles.ScrollThumb))
            return;

        //primitive proportional bar (a fixed grip sprite can't scale cleanly); shares the wood outline
        DrawRectClipped(spriteBatch, thumbRect, ThumbColor());
        DrawBorderClipped(spriteBatch, thumbRect, TrackBorderColor);
    }

    //depressed art only on an actual press; a disabled (non-scrollable) bar keeps the normal art so it doesn't read as
    //permanently pushed in
    private void DrawArrowButton(SpriteBatch spriteBatch, Rectangle button, ScrollZone zone)
    {
        var decrement = zone == ScrollZone.DecArrow;
        var pressed = ActiveZone == zone;

        var role = decrement
            ? (pressed ? ThemeRoles.ScrollUpPressed : ThemeRoles.ScrollUp)
            : (pressed ? ThemeRoles.ScrollDownPressed : ThemeRoles.ScrollDown);

        if (TryDrawPart(spriteBatch, role, button, decrement ? ThemeRoles.ScrollUp : ThemeRoles.ScrollDown))
            return;

        if (!UseSprites)
        {
            var dir = IsHorizontal ? (decrement ? ArrowDir.Left : ArrowDir.Right) : (decrement ? ArrowDir.Up : ArrowDir.Down);

            DrawArrow(spriteBatch, button, dir, ArrowColor(zone));

            return;
        }

        //legacy scroll.epf arrow over the primitive scaffold
        var frame = (IsHorizontal, decrement) switch
        {
            (true, true) => pressed ? FRAME_LEFT_ACTIVE : FRAME_LEFT_NORMAL,
            (true, false) => pressed ? FRAME_RIGHT_ACTIVE : FRAME_RIGHT_NORMAL,
            (false, true) => pressed ? FRAME_UP_ACTIVE : FRAME_UP_NORMAL,
            (false, false) => pressed ? FRAME_DOWN_ACTIVE : FRAME_DOWN_NORMAL
        };

        DrawTexture(spriteBatch, GetFrame(frame), new Vector2(button.X, button.Y), Color.White);
    }

    private static Texture2D GetFrame(int index) => UiRenderer.Instance!.GetEpfTexture(SCROLL_EPF, index);

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
