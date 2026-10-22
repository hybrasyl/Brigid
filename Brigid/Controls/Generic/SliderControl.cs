#region
using Brigid.Controls.Components;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A primitive, asset-free slider for a value from <see cref="MIN_VALUE" /> to <see cref="MAX_VALUE" /> (BRIG-55): a
///     recessed track, a fill from the start of the track to the thumb, and a thumb that a drag or a click moves. The
///     control is as wide as the track plus one thumb, so the thumb stays inside it at both ends.
///     <para>
///         A themed slider draws the <see cref="ThemeRoles.SliderTrack" />, <see cref="ThemeRoles.SliderFill" /> and
///         <see cref="ThemeRoles.SliderThumb" /> parts. A piece with no part has the flat look: a dark groove with its
///         outline in the <see cref="ThemeRoles.Border" /> colour, a fill in the
///         <see cref="ThemeRoles.TextSelected" /> colour, and a flat thumb.
///     </para>
///     <para>
///         <see cref="ValueChanged" /> is raised only when the player changes the value. <see cref="SetValue" /> is silent,
///         so that a host can show a stored value. A disabled slider takes no input.
///     </para>
/// </summary>
public sealed class SliderControl : UIPanel
{
    public const int MIN_VALUE = 0;
    public const int MAX_VALUE = 10;

    /// <summary>The width of the thumb; the track is inset half of it at each end.</summary>
    public const int THUMB_W = 8;

    private const int GROOVE_H = 4;

    private static readonly Color GrooveColor = new(18, 18, 22, 235);

    private bool Dragging;

    /// <summary>The value, from <see cref="MIN_VALUE" /> to <see cref="MAX_VALUE" />.</summary>
    public int Value { get; private set; } = MAX_VALUE;

    /// <summary>Raised when the player changes the value with a drag or a click.</summary>
    public event Action<int>? ValueChanged;

    /// <param name="width">The width of the control: the track plus one <see cref="THUMB_W" />.</param>
    /// <param name="height">The height of the control and of the thumb.</param>
    public SliderControl(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Sets the value, clamped to the range, and raises no event.</summary>
    public void SetValue(int value) => Value = Math.Clamp(value, MIN_VALUE, MAX_VALUE);

    /// <summary>
    ///     The value under a point <paramref name="localX" /> px from the left edge of a slider <paramref name="width" /> px
    ///     wide: the nearest step on the track, clamped to the range.
    /// </summary>
    internal static int ValueAt(int localX, int width)
    {
        var trackWidth = Math.Max(1, width - THUMB_W);
        var ratio = (float)(localX - THUMB_W / 2) / trackWidth;

        return Math.Clamp((int)MathF.Round(ratio * MAX_VALUE), MIN_VALUE, MAX_VALUE);
    }

    /// <summary>The left edge of the thumb, in px from the left edge of a slider <paramref name="width" /> px wide.</summary>
    internal static int ThumbX(int value, int width) => (int)((float)value / MAX_VALUE * (width - THUMB_W));

    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        var trackWidth = bounds.Width - THUMB_W;
        var track = new Rectangle(bounds.X + THUMB_W / 2, bounds.Y + (bounds.Height - GROOVE_H) / 2, trackWidth, GROOVE_H);
        var thumbX = bounds.X + ThumbX(Value, bounds.Width);
        var fill = new Rectangle(track.X, track.Y, thumbX + THUMB_W / 2 - track.X, GROOVE_H);
        var thumb = new Rectangle(thumbX, bounds.Y, THUMB_W, bounds.Height);

        //a themed track and fill take the whole height of the control, so their art can be taller than the flat groove
        var themedTrack = new Rectangle(track.X, bounds.Y, track.Width, bounds.Height);
        var themedFill = new Rectangle(track.X, bounds.Y, fill.Width, bounds.Height);

        if (!TryDrawThemePart(spriteBatch, ThemeRoles.SliderTrack, themedTrack))
        {
            DrawRectClipped(spriteBatch, track, GrooveColor);
            DrawBorderClipped(spriteBatch, track, ThemeColor(ThemeRoles.Border));
        }

        if ((fill.Width > 0) && !TryDrawThemePart(spriteBatch, ThemeRoles.SliderFill, themedFill))
            DrawRectClipped(spriteBatch, fill, ThemeColor(ThemeRoles.TextSelected));

        if (!TryDrawThemePart(spriteBatch, ThemeRoles.SliderThumb, thumb))
        {
            DrawRectClipped(spriteBatch, thumb, DialogPalette.ButtonHoverFill);
            DrawBorderClipped(spriteBatch, thumb, ThemeColor(ThemeRoles.Border));
        }
    }

    public override void OnMouseDown(MouseDownEvent e)
    {
        if ((e.Button != MouseButton.Left) || !Enabled)
            return;

        Dragging = true;
        MoveTo(e.ScreenX);
        e.Handled = true;
    }

    public override void OnMouseMove(MouseMoveEvent e)
    {
        if (!Dragging)
            return;

        MoveTo(e.ScreenX);
        e.Handled = true;
    }

    public override void OnMouseUp(MouseUpEvent e)
    {
        if (e.Button == MouseButton.Left)
            Dragging = false;
    }

    public override void ResetInteractionState() => Dragging = false;

    private void MoveTo(int screenX)
    {
        if (!Enabled)
        {
            Dragging = false;

            return;
        }

        var value = ValueAt(screenX - ScreenX, Width);

        if (value == Value)
            return;

        Value = value;
        ValueChanged?.Invoke(value);
    }
}
