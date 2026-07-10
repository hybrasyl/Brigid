#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     Scrollbar using scroll.epf assets from setoa.dat. Supports vertical (up/down) and horizontal (left/right)
///     orientations. Arrow buttons (16x16), tiled track, fixed 16x16 thumb.
///     5 hit zones: decrement arrow, page decrement, thumb (draggable), page increment, increment arrow.
/// </summary>
public sealed class ScrollBarControl : UIElement
{
    public const int DEFAULT_WIDTH = 16;
    private const int BUTTON_SIZE = 16;

    //scroll.epf frame order: left(0,1), right(2,3), up(4,5), down(6,7), thumb(8), track(9)
    //each pair: [normal, active]
    private const int FRAME_LEFT_NORMAL = 0;
    private const int FRAME_LEFT_ACTIVE = 1;
    private const int FRAME_RIGHT_NORMAL = 2;
    private const int FRAME_RIGHT_ACTIVE = 3;
    private const int FRAME_UP_NORMAL = 4;
    private const int FRAME_UP_ACTIVE = 5;
    private const int FRAME_DOWN_NORMAL = 6;
    private const int FRAME_DOWN_ACTIVE = 7;
    private const int FRAME_THUMB = 8;
    private const int FRAME_TRACK = 9;

    private const float REPEAT_DELAY_MS = 50f;
    private const string SCROLL_EPF = "scroll.epf";

    private int ActiveZone = -1;

    private bool Dragging;
    private int DragOffsetX;
    private int DragOffsetY;
    private float RepeatTimer;

    public int MaxValue { get; set; }
    public ScrollOrientation Orientation { get; set; } = ScrollOrientation.Vertical;

    /// <summary>
    ///     How much one arrow click, held-arrow repeat, track-zone click, or wheel notch moves <see cref="Value" />.
    ///     Defaults to 1 for line-unit consumers; pixel-unit consumers (e.g. MarkdownView) set a larger step.
    /// </summary>
    public int Step { get; set; } = 1;
    public int TotalItems { get; set; }
    public int Value { get; set; }
    public int VisibleItems { get; set; }

    public ScrollBarControl() => Width = BUTTON_SIZE;

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        UpdateClipRect();

        if ((ClipRect.Width <= 0) || (ClipRect.Height <= 0))
            return;

        if (Orientation == ScrollOrientation.Horizontal)
        {
            DrawHorizontal(spriteBatch);

            return;
        }

        var sx = ScreenX;
        var sy = ScreenY;
        var trackStart = sy + BUTTON_SIZE;
        var trackEnd = sy + Height - BUTTON_SIZE;
        var scrollable = TotalItems > VisibleItems;

        //tiled track background
        var trackTex = GetFrame(FRAME_TRACK);

        for (var tileY = trackStart; tileY < trackEnd; tileY += BUTTON_SIZE)
        {
            var tileH = Math.Min(BUTTON_SIZE, trackEnd - tileY);

            DrawTexture(
                spriteBatch,
                trackTex,
                new Vector2(sx, tileY),
                new Rectangle(
                    0,
                    0,
                    BUTTON_SIZE,
                    tileH),
                Color.White);
        }

        //up arrow — normal when idle, active when pressed or disabled (active frame doubles as disabled)
        var upFrame = !scrollable || (ActiveZone == 0) ? FRAME_UP_ACTIVE : FRAME_UP_NORMAL;

        DrawTexture(
            spriteBatch,
            GetFrame(upFrame),
            new Vector2(sx, sy),
            Color.White);

        //down arrow — normal when idle, active when pressed or disabled
        var downFrame = !scrollable || (ActiveZone == 4) ? FRAME_DOWN_ACTIVE : FRAME_DOWN_NORMAL;

        DrawTexture(
            spriteBatch,
            GetFrame(downFrame),
            new Vector2(sx, trackEnd),
            Color.White);

        //thumb (only when scrollable)
        if (scrollable)
        {
            var thumbY = GetThumbPosition(trackStart, trackEnd);

            DrawTexture(
                spriteBatch,
                GetFrame(FRAME_THUMB),
                new Vector2(sx, thumbY),
                Color.White);
        }
    }

    private void DrawHorizontal(SpriteBatch spriteBatch)
    {
        var sx = ScreenX;
        var sy = ScreenY;
        var trackStart = sx + BUTTON_SIZE;
        var trackEnd = sx + Width - BUTTON_SIZE;
        var scrollable = TotalItems > VisibleItems;

        //tiled track background
        var trackTex = GetFrame(FRAME_TRACK);

        for (var tileX = trackStart; tileX < trackEnd; tileX += BUTTON_SIZE)
        {
            var tileW = Math.Min(BUTTON_SIZE, trackEnd - tileX);

            DrawTexture(
                spriteBatch,
                trackTex,
                new Vector2(tileX, sy),
                new Rectangle(
                    0,
                    0,
                    tileW,
                    BUTTON_SIZE),
                Color.White);
        }

        //left arrow — normal when idle, active when pressed or disabled
        var leftFrame = !scrollable || (ActiveZone == 0) ? FRAME_LEFT_ACTIVE : FRAME_LEFT_NORMAL;

        DrawTexture(
            spriteBatch,
            GetFrame(leftFrame),
            new Vector2(sx, sy),
            Color.White);

        //right arrow — normal when idle, active when pressed or disabled
        var rightFrame = !scrollable || (ActiveZone == 4) ? FRAME_RIGHT_ACTIVE : FRAME_RIGHT_NORMAL;

        DrawTexture(
            spriteBatch,
            GetFrame(rightFrame),
            new Vector2(trackEnd, sy),
            Color.White);

        //thumb (only when scrollable)
        if (scrollable)
        {
            var thumbX = GetThumbPosition(trackStart, trackEnd);

            DrawTexture(
                spriteBatch,
                GetFrame(FRAME_THUMB),
                new Vector2(thumbX, sy),
                Color.White);
        }
    }

    private static Texture2D GetFrame(int index) => UiRenderer.Instance!.GetEpfTexture(SCROLL_EPF, index);

    private int GetThumbPosition(int trackStart, int trackEnd)
    {
        if (MaxValue <= 0)
            return trackStart;

        var usableTrack = trackEnd - trackStart - BUTTON_SIZE;
        var ratio = (float)Value / MaxValue;

        return trackStart + (int)(ratio * usableTrack);
    }

    public event ScrollValueChangedHandler? OnValueChanged;

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (!Dragging && ActiveZone is 0 or 4)
        {
            RepeatTimer += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

            if (RepeatTimer >= REPEAT_DELAY_MS)
            {
                RepeatTimer -= REPEAT_DELAY_MS;

                if (ActiveZone == 0)
                {
                    Value = Math.Max(0, Value - Step);
                    OnValueChanged?.Invoke(Value);
                } else if (ActiveZone == 4)
                {
                    Value = Math.Min(MaxValue, Value + Step);
                    OnValueChanged?.Invoke(Value);
                }
            }
        }
    }

    public override void OnMouseDown(MouseDownEvent e)
    {
        if ((e.Button != MouseButton.Left) || (TotalItems <= VisibleItems))
            return;

        if (Orientation == ScrollOrientation.Horizontal)
        {
            HandleHorizontalMouseDown(e);

            return;
        }

        var sy = ScreenY;
        var trackStart = sy + BUTTON_SIZE;
        var trackEnd = sy + Height - BUTTON_SIZE;
        var thumbY = GetThumbPosition(trackStart, trackEnd);
        var my = e.ScreenY;

        if (my < trackStart)
        {
            ActiveZone = 0;
            Value = Math.Max(0, Value - Step);
            OnValueChanged?.Invoke(Value);
        } else if (my >= trackEnd)
        {
            ActiveZone = 4;
            Value = Math.Min(MaxValue, Value + Step);
            OnValueChanged?.Invoke(Value);
        } else if ((my >= thumbY) && (my < (thumbY + BUTTON_SIZE)))
        {
            ActiveZone = 2;
            Dragging = true;
            DragOffsetY = my - thumbY;
        } else if (my < thumbY)
        {
            ActiveZone = 1;
            Value = Math.Max(0, Value - Step);
            OnValueChanged?.Invoke(Value);
        } else
        {
            ActiveZone = 3;
            Value = Math.Min(MaxValue, Value + Step);
            OnValueChanged?.Invoke(Value);
        }

        e.Handled = true;
    }

    private void HandleHorizontalMouseDown(MouseDownEvent e)
    {
        var sx = ScreenX;
        var trackStart = sx + BUTTON_SIZE;
        var trackEnd = sx + Width - BUTTON_SIZE;
        var thumbX = GetThumbPosition(trackStart, trackEnd);
        var mx = e.ScreenX;

        if (mx < trackStart)
        {
            ActiveZone = 0;
            Value = Math.Max(0, Value - Step);
            OnValueChanged?.Invoke(Value);
        } else if (mx >= trackEnd)
        {
            ActiveZone = 4;
            Value = Math.Min(MaxValue, Value + Step);
            OnValueChanged?.Invoke(Value);
        } else if ((mx >= thumbX) && (mx < (thumbX + BUTTON_SIZE)))
        {
            ActiveZone = 2;
            Dragging = true;
            DragOffsetX = mx - thumbX;
        } else if (mx < thumbX)
        {
            ActiveZone = 1;
            Value = Math.Max(0, Value - Step);
            OnValueChanged?.Invoke(Value);
        } else
        {
            ActiveZone = 3;
            Value = Math.Min(MaxValue, Value + Step);
            OnValueChanged?.Invoke(Value);
        }

        e.Handled = true;
    }

    public override void OnMouseMove(MouseMoveEvent e)
    {
        if (!Dragging)
            return;

        if (Orientation == ScrollOrientation.Horizontal)
        {
            var sx = ScreenX;
            var trackStart = sx + BUTTON_SIZE;
            var trackEnd = sx + Width - BUTTON_SIZE;
            var usableTrack = trackEnd - trackStart - BUTTON_SIZE;

            if (usableTrack > 0)
            {
                var mouseX = e.ScreenX - DragOffsetX;
                var ratio = Math.Clamp((float)(mouseX - trackStart) / usableTrack, 0f, 1f);
                var newValue = (int)(ratio * MaxValue);

                if (newValue != Value)
                {
                    Value = newValue;
                    OnValueChanged?.Invoke(Value);
                }
            }

            return;
        }

        var sy = ScreenY;
        var trackStartV = sy + BUTTON_SIZE;
        var trackEndV = sy + Height - BUTTON_SIZE;
        var usableTrackV = trackEndV - trackStartV - BUTTON_SIZE;

        if (usableTrackV > 0)
        {
            var mouseY = e.ScreenY - DragOffsetY;
            var ratio = Math.Clamp((float)(mouseY - trackStartV) / usableTrackV, 0f, 1f);
            var newValue = (int)(ratio * MaxValue);

            if (newValue != Value)
            {
                Value = newValue;
                OnValueChanged?.Invoke(Value);
            }
        }
    }

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        if (TotalItems <= VisibleItems)
            return;

        var newValue = Math.Clamp(Value - e.Delta * Step, 0, MaxValue);

        if (newValue != Value)
        {
            Value = newValue;
            OnValueChanged?.Invoke(Value);
        }

        e.Handled = true;
    }

    public override void OnMouseUp(MouseUpEvent e)
    {
        if (e.Button == MouseButton.Left)
        {
            Dragging = false;
            ActiveZone = -1;
            RepeatTimer = 0;
        }
    }
}
