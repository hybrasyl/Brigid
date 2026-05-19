#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

/// <summary>
///     Stores text-draw state and renders it via the font texture atlas. Re-measures only when content, color,
///     <see cref="WrapWidth" />, or <see cref="ShadowStyle" /> changes between successive <see cref="Update" /> calls.
///     No GPU resources are held — the shared font atlas handles all rendering.
/// </summary>
public sealed class TextElement
{
    private int LastWrapWidth;
    private ShadowStyle LastShadowStyle;

    public bool ColorCodesEnabled { get; set; } = true;
    public Color Color { get; private set; } = LegendColors.Silver;
    public int Height { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public int Width { get; private set; }
    public IReadOnlyList<string>? WrappedLines { get; private set; }
    public bool HasContent => Width > 0;

    /// <summary>
    ///     Width to wrap at, in pixels. Zero disables wrapping. Read by <see cref="Update" />.
    /// </summary>
    public int WrapWidth { get; set; }

    /// <summary>
    ///     Shadow style applied during <see cref="Draw" />; also widens/heightens the bounding box reported by
    ///     <see cref="Width" /> and <see cref="Height" />.
    /// </summary>
    public ShadowStyle ShadowStyle { get; set; }

    /// <summary>
    ///     Shadow color used when <see cref="ShadowStyle" /> is not <see cref="ShadowStyle.None" />.
    /// </summary>
    public Color ShadowColor { get; set; } = Color.Black;

    /// <summary>
    ///     Re-measures, and (when <see cref="WrapWidth" /> is positive) re-wraps the text. No-op when
    ///     <paramref name="text" />, <paramref name="color" />, <see cref="WrapWidth" />, and
    ///     <see cref="ShadowStyle" /> all match the previous call.
    /// </summary>
    public void Update(string text, Color color)
    {
        if ((text == Text) && (color == Color) && (WrapWidth == LastWrapWidth) && (ShadowStyle == LastShadowStyle))
            return;

        Text = text;
        Color = color;
        LastWrapWidth = WrapWidth;
        LastShadowStyle = ShadowStyle;

        if (string.IsNullOrEmpty(text))
        {
            Width = 0;
            Height = 0;
            WrappedLines = null;

            return;
        }

        if (WrapWidth > 0)
        {
            WrappedLines = TextRenderer.WrapText(text, WrapWidth);
            Width = WrapWidth;
            Height = Math.Max(TextRenderer.CHAR_HEIGHT, WrappedLines.Count * TextRenderer.CHAR_HEIGHT);

            return;
        }

        WrappedLines = null;
        var marginX = ShadowStyle switch
        {
            ShadowStyle.BothSides                              => 2,
            ShadowStyle.BottomLeft or ShadowStyle.BottomRight  => 1,
            _                                                  => 0
        };
        var marginY = ShadowStyle == ShadowStyle.None ? 0 : 1;
        Width = TextRenderer.MeasureWidth(text) + marginX;
        Height = TextRenderer.CHAR_HEIGHT + marginY;
    }

    /// <summary>
    ///     Draws <paramref name="text" /> (or <see cref="Text" /> when null) at <paramref name="position" />,
    ///     applying <see cref="ShadowStyle" /> and clipping each pass to <paramref name="clipRect" />. Pass
    ///     <see cref="Rectangle.Empty" /> (or omit) to skip clipping entirely.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, Vector2 position, Rectangle clipRect = default, string? text = null, float opacity = 1f)
    {
        text ??= Text;

        if (string.IsNullOrEmpty(text))
            return;

        switch (ShadowStyle)
        {
            case ShadowStyle.None:
                DrawClipped(spriteBatch, position, text, Color, clipRect, opacity);

                break;
            case ShadowStyle.BottomLeft:
                DrawClipped(spriteBatch, position + new Vector2(0, 1), text, ShadowColor, clipRect, opacity);
                DrawClipped(spriteBatch, position + new Vector2(1, 0), text, Color, clipRect, opacity);

                break;
            case ShadowStyle.BottomRight:
                DrawClipped(spriteBatch, position + new Vector2(1, 1), text, ShadowColor, clipRect, opacity);
                DrawClipped(spriteBatch, position, text, Color, clipRect, opacity);

                break;
            case ShadowStyle.BothSides:
                DrawClipped(spriteBatch, position + new Vector2(2, 1), text, ShadowColor, clipRect, opacity);
                DrawClipped(spriteBatch, position + new Vector2(0, 1), text, ShadowColor, clipRect, opacity);
                DrawClipped(spriteBatch, position + new Vector2(1, 0), text, Color, clipRect, opacity);

                break;
        }
    }

    private void DrawClipped(SpriteBatch spriteBatch, Vector2 position, string text, Color color, Rectangle clipRect, float opacity)
    {
        if (clipRect.IsEmpty)
        {
            TextRenderer.DrawText(spriteBatch, position, text, color, ColorCodesEnabled, opacity);

            return;
        }

        var textWidth = TextRenderer.MeasureWidth(text);
        var textBounds = new Rectangle((int)position.X, (int)position.Y, textWidth, TextRenderer.CHAR_HEIGHT);

        if (!clipRect.Intersects(textBounds))
            return;

        if (clipRect.Contains(textBounds))
        {
            TextRenderer.DrawText(spriteBatch, position, text, color, ColorCodesEnabled, opacity);

            return;
        }

        TextRenderer.DrawTextClipped(spriteBatch, position, text, color, clipRect, ColorCodesEnabled, opacity);
    }
}
