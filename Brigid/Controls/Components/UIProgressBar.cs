#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Components;

/// <summary>
///     A progress bar that displays a 0-1 percentage. Supports three rendering modes:
///     <list type="bullet">
///         <item>Frame-based — selects a frame from a texture array by <see cref="Percent" />. Pass frames to constructor.</item>
///         <item>Texture-clip — reveals a single <see cref="FillTexture" /> left-to-right by <see cref="Percent" />.</item>
///         <item>Color fill — draws a filled rectangle scaled by <see cref="Percent" />. Set <see cref="FillColor" />.</item>
///     </list>
/// </summary>
public sealed class UIProgressBar : UIElement
{
    private readonly Texture2D[] Frames;
    private int CurrentFrame;

    /// <summary>
    ///     The fill color for color-fill mode. Null = no color fill drawn.
    /// </summary>
    public Color? FillColor { get; set; }

    /// <summary>
    ///     A single texture revealed left-to-right by <see cref="Percent" />. Not disposed by this control.
    /// </summary>
    public Texture2D? FillTexture { get; set; }

    /// <summary>
    ///     Fill percentage from 0.0 to 1.0.
    /// </summary>
    public float Percent { get; set; }

    /// <summary>
    ///     Creates a color-fill progress bar (no frames).
    /// </summary>
    public UIProgressBar() => Frames = [];

    /// <summary>
    ///     Creates a frame-based progress bar that selects a frame by percentage.
    /// </summary>
    public UIProgressBar(Texture2D[] frames) => Frames = frames;

    public override void Dispose()
    {
        foreach (var frame in Frames)
            frame.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        //frame-based rendering
        if ((Frames.Length > 0) && (CurrentFrame < Frames.Length))
        {
            DrawTexture(
                spriteBatch,
                Frames[CurrentFrame],
                new Vector2(ScreenX, ScreenY),
                Color.White);

            return;
        }

        //texture-clip rendering — reveals a single texture left-to-right
        if (FillTexture is not null)
        {
            var clipWidth = (int)(FillTexture.Width * Math.Clamp(Percent, 0f, 1f));

            if (clipWidth > 0)
                DrawTexture(
                    spriteBatch,
                    FillTexture,
                    new Vector2(ScreenX, ScreenY),
                    new Rectangle(
                        0,
                        0,
                        clipWidth,
                        FillTexture.Height),
                    Color.White);

            return;
        }

        //color-fill rendering
        var fillWidth = (int)(Width * Math.Clamp(Percent, 0f, 1f));

        if ((fillWidth > 0) && FillColor.HasValue)
            DrawRectClipped(
                spriteBatch,
                new Rectangle(
                    ScreenX,
                    ScreenY,
                    fillWidth,
                    Height),
                FillColor.Value);
    }

    /// <summary>
    ///     Sets <see cref="Percent" /> from a current/max value pair.
    /// </summary>
    public void UpdateValue(int current, int max)
    {
        if (max <= 0)
            return;

        Percent = Math.Clamp((float)current / max, 0f, 1f);

        if (Frames.Length > 0)
            CurrentFrame = (int)(Percent * (Frames.Length - 1));
    }
}