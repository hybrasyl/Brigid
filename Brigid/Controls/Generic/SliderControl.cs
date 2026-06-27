#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A slider control with a draggable thumb. The panel bounds encompass both the track
///     and any thumb overflow so hit-testing works when clicking the thumb outside the track.
/// </summary>
public sealed class SliderControl : UIPanel
{
    private const int VOLUME_MIN = 0;
    private const int VOLUME_MAX = 10;

    //track groove geometry/colors — a thin recessed bar with an amber "filled level" up to the thumb
    private const int GROOVE_HEIGHT = 4;
    private static readonly Color GrooveColor = new(18, 18, 22, 235);
    private static readonly Color FillColor = new(186, 162, 110, 255);
    private static readonly Color GrooveBorder = new(150, 150, 160, 255);

    private readonly Rectangle TrackRect;
    private readonly Texture2D? ThumbTexture;
    private readonly int ThumbWidth;
    private readonly int ThumbHeight;
    private bool Dragging;

    /// <summary>
    ///     Current value in [0, 10].
    /// </summary>
    public int Value { get; private set; } = 10;

    /// <summary>
    ///     Fired when the value changes due to user interaction.
    /// </summary>
    public event Action<int>? ValueChanged;

    /// <summary>
    ///     Creates a slider control.
    /// </summary>
    /// <param name="trackRect">Track bounds relative to the parent panel (from prefab rect).</param>
    /// <param name="thumbTexture">Thumb/tick texture.</param>
    public SliderControl(Rectangle trackRect, Texture2D? thumbTexture)
    {
        TrackRect = trackRect;
        ThumbTexture = thumbTexture;
        ThumbWidth = thumbTexture?.Width ?? 12;
        ThumbHeight = thumbTexture?.Height ?? 12;

        //position at the track location, but expand bounds to include thumb overflow
        var overflowY = (ThumbHeight - trackRect.Height) / 2;

        X = trackRect.X - ThumbWidth / 2;
        Y = trackRect.Y - overflowY;
        Width = trackRect.Width + ThumbWidth;
        Height = Math.Max(trackRect.Height, ThumbHeight);
    }

    public void SetValue(int value) => Value = Math.Clamp(value, VOLUME_MIN, VOLUME_MAX);

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        DrawTrack(spriteBatch);

        if (ThumbTexture is null)
            return;

        var thumbX = ScreenX + ThumbWidth / 2 + (int)((float)Value / VOLUME_MAX * TrackRect.Width) - ThumbWidth / 2;
        var thumbY = ScreenY + (Height - ThumbHeight) / 2;

        DrawTexture(spriteBatch, ThumbTexture, new Vector2(thumbX, thumbY), Color.White);
    }

    //draws the recessed groove, an amber fill up to the current value, and a light border
    private void DrawTrack(SpriteBatch spriteBatch)
    {
        var trackScreenX = ScreenX + ThumbWidth / 2;
        var grooveY = ScreenY + (Height - GROOVE_HEIGHT) / 2;
        var groove = new Rectangle(trackScreenX, grooveY, TrackRect.Width, GROOVE_HEIGHT);

        DrawRect(spriteBatch, groove, GrooveColor);

        var filledWidth = (int)((float)Value / VOLUME_MAX * TrackRect.Width);

        if (filledWidth > 0)
            DrawRect(spriteBatch, new Rectangle(trackScreenX, grooveY, filledWidth, GROOVE_HEIGHT), FillColor);

        DrawBorder(spriteBatch, groove, GrooveBorder);
    }

    public override void OnMouseDown(MouseDownEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        Dragging = true;
        UpdateValueFromMouse(e.ScreenX);
        e.Handled = true;
    }

    public override void OnMouseMove(MouseMoveEvent e)
    {
        if (!Dragging)
            return;

        UpdateValueFromMouse(e.ScreenX);
        e.Handled = true;
    }

    public override void OnMouseUp(MouseUpEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        Dragging = false;
    }

    private void UpdateValueFromMouse(int screenX)
    {
        var trackScreenX = ScreenX + ThumbWidth / 2;
        var ratio = (float)(screenX - trackScreenX) / TrackRect.Width;
        var volume = (int)Math.Round(ratio * VOLUME_MAX);
        volume = Math.Clamp(volume, VOLUME_MIN, VOLUME_MAX);

        if (volume == Value)
            return;

        Value = volume;
        ValueChanged?.Invoke(volume);
    }
}