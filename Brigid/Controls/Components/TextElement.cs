#region
using Brigid.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Components;

/// <summary>
///     Stores text-draw state and renders it via the font texture atlas. Re-measures only when content, color,
///     <see cref="WrapWidth" />, or <see cref="ShadowStyle" /> changes between successive <see cref="Update" /> calls.
///     No GPU resources are held — the shared font atlas handles all rendering.
/// </summary>
public sealed class TextElement
{
    private int LastWrapWidth;
    private int? LastFontSize;
    private float LastCharacterSpacing;
    private int LastFontGeneration = -1;
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
    ///     Explicit glyph pixel size, or null for <see cref="Rendering.FontEngine.RENDER_SIZE" />. Measurement, wrapping
    ///     and drawing all honour it, so a caller that shrinks text to fit gets a wrap that agrees with what is drawn.
    ///     The line grid (<see cref="TextRenderer.CHAR_HEIGHT" />) is unaffected — this changes ink, not layout pitch.
    /// </summary>
    public int? FontSize { get; set; }

    /// <summary>
    ///     Persistent letter-spacing stacked on top of <see cref="Rendering.FontEngine.DEFAULT_TRACKING" />, honoured by
    ///     measurement, wrapping and drawing alike. Pass <c>-DEFAULT_TRACKING</c> to render at the face's natural
    ///     advances — the default tracking is negative and, since these faces have no sidebearing slack, is what pushes
    ///     glyph ink into its neighbour. Distinct from the per-call spacing shrink-to-fit passes to <see cref="Draw" />,
    ///     which stacks on top of this.
    /// </summary>
    public float CharacterSpacing { get; set; }

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
        //re-measure when the active font changes (cycling fonts) so cached widths/wraps don't go stale
        var fontGeneration = Rendering.FontEngine.Instance.Generation;

        if ((text == Text) && (color == Color) && (WrapWidth == LastWrapWidth) && (ShadowStyle == LastShadowStyle)
            && (FontSize == LastFontSize) && (CharacterSpacing.Equals(LastCharacterSpacing))
            && (fontGeneration == LastFontGeneration))
            return;

        Text = text;
        Color = color;
        LastWrapWidth = WrapWidth;
        LastFontSize = FontSize;
        LastCharacterSpacing = CharacterSpacing;
        LastShadowStyle = ShadowStyle;
        LastFontGeneration = fontGeneration;

        if (string.IsNullOrEmpty(text))
        {
            Width = 0;
            Height = 0;
            WrappedLines = null;

            return;
        }

        if (WrapWidth > 0)
        {
            WrappedLines = TextRenderer.WrapText(text, WrapWidth, FontSize, CharacterSpacing);
            Width = WrapWidth;
            Height = Math.Max(TextRenderer.CHAR_HEIGHT, WrappedLines.Count * TextRenderer.CHAR_HEIGHT);

            return;
        }

        WrappedLines = null;
        (var marginX, var marginY) = ShadowStyle.ShadowMargin;
        Width = TextRenderer.MeasureWidth(text, FontSize, CharacterSpacing) + marginX;
        Height = TextRenderer.CHAR_HEIGHT + marginY;
    }

    /// <summary>
    ///     Re-measures (and re-wraps) if the active font changed since the last <see cref="Update" /> — i.e. the user
    ///     cycled the UI font at runtime. Static labels never call <see cref="Update" /> again on their own, so without
    ///     this their cached <see cref="Width" />/<see cref="Height" /> stay measured in the previous face while the
    ///     glyphs draw in the new one, throwing off center/right alignment. Cheap no-op when the font is unchanged.
    /// </summary>
    public void RefreshForFont()
    {
        if (!string.IsNullOrEmpty(Text) && (Rendering.FontEngine.Instance.Generation != LastFontGeneration))
            Update(Text, Color);
    }

    /// <summary>
    ///     Draws <paramref name="text" /> (or <see cref="Text" /> when null) at <paramref name="position" />,
    ///     applying <see cref="ShadowStyle" /> and clipping each pass to <paramref name="clipRect" />. Pass
    ///     <see cref="Rectangle.Empty" /> (or omit) to skip clipping entirely. <paramref name="characterSpacing" />
    ///     is the per-glyph tracking adjustment (negative tightens) used by shrink-to-fit callers; every shadow pass
    ///     uses the same value so the offsets stay aligned with the glyph pass.
    /// </summary>
    public void Draw(
        SpriteBatch spriteBatch,
        Vector2 position,
        Rectangle clipRect = default,
        string? text = null,
        float opacity = 1f,
        float characterSpacing = 0f)
    {
        text ??= Text;

        if (string.IsNullOrEmpty(text))
            return;

        switch (ShadowStyle)
        {
            case ShadowStyle.None:
                DrawClipped(spriteBatch, position, text, Color, clipRect, opacity, characterSpacing);

                break;
            case ShadowStyle.BottomLeft:
                DrawClipped(spriteBatch, position + new Vector2(0, 1), text, ShadowColor, clipRect, opacity, characterSpacing);
                DrawClipped(spriteBatch, position + new Vector2(1, 0), text, Color, clipRect, opacity, characterSpacing);

                break;
            case ShadowStyle.BottomRight:
                DrawClipped(spriteBatch, position + new Vector2(1, 1), text, ShadowColor, clipRect, opacity, characterSpacing);
                DrawClipped(spriteBatch, position, text, Color, clipRect, opacity, characterSpacing);

                break;
            case ShadowStyle.BothSides:
                DrawClipped(spriteBatch, position + new Vector2(2, 1), text, ShadowColor, clipRect, opacity, characterSpacing);
                DrawClipped(spriteBatch, position + new Vector2(0, 1), text, ShadowColor, clipRect, opacity, characterSpacing);
                DrawClipped(spriteBatch, position + new Vector2(1, 0), text, Color, clipRect, opacity, characterSpacing);

                break;
            case ShadowStyle.Outline:
                //8-way dark ring around the glyphs, then the glyph centered in the +1 margin on top
                DrawClipped(spriteBatch, position + new Vector2(0, 0), text, ShadowColor, clipRect, opacity, characterSpacing);
                DrawClipped(spriteBatch, position + new Vector2(1, 0), text, ShadowColor, clipRect, opacity, characterSpacing);
                DrawClipped(spriteBatch, position + new Vector2(2, 0), text, ShadowColor, clipRect, opacity, characterSpacing);
                DrawClipped(spriteBatch, position + new Vector2(0, 1), text, ShadowColor, clipRect, opacity, characterSpacing);
                DrawClipped(spriteBatch, position + new Vector2(2, 1), text, ShadowColor, clipRect, opacity, characterSpacing);
                DrawClipped(spriteBatch, position + new Vector2(0, 2), text, ShadowColor, clipRect, opacity, characterSpacing);
                DrawClipped(spriteBatch, position + new Vector2(1, 2), text, ShadowColor, clipRect, opacity, characterSpacing);
                DrawClipped(spriteBatch, position + new Vector2(2, 2), text, ShadowColor, clipRect, opacity, characterSpacing);
                DrawClipped(spriteBatch, position + new Vector2(1, 1), text, Color, clipRect, opacity, characterSpacing);

                break;
        }
    }

    private void DrawClipped(
        SpriteBatch spriteBatch,
        Vector2 position,
        string text,
        Color color,
        Rectangle clipRect,
        float opacity,
        float characterSpacing)
    {
        if (clipRect.IsEmpty)
        {
            TextRenderer.DrawText(spriteBatch, position, text, color, ColorCodesEnabled, opacity, characterSpacing + CharacterSpacing, FontSize);

            return;
        }

        //tracking shifts the drawn width by an amount only DrawCore can compute exactly (it advances per colour
        //run, excluding {=x} code chars), so the bounds fast paths below can't be trusted — clip per glyph instead
        if (characterSpacing != 0f)
        {
            TextRenderer.DrawTextClipped(
                spriteBatch,
                position,
                text,
                color,
                clipRect,
                ColorCodesEnabled,
                opacity,
                characterSpacing + CharacterSpacing,
                FontSize);

            return;
        }

        var textWidth = TextRenderer.MeasureWidth(text, FontSize, CharacterSpacing);
        var textBounds = new Rectangle((int)position.X, (int)position.Y, textWidth, TextRenderer.CHAR_HEIGHT);

        if (!clipRect.Intersects(textBounds))
            return;

        if (clipRect.Contains(textBounds))
        {
            TextRenderer.DrawText(spriteBatch, position, text, color, ColorCodesEnabled, opacity, characterSpacing + CharacterSpacing, FontSize);

            return;
        }

        TextRenderer.DrawTextClipped(
            spriteBatch,
            position,
            text,
            color,
            clipRect,
            ColorCodesEnabled,
            opacity,
            characterSpacing + CharacterSpacing,
            FontSize);
    }
}
