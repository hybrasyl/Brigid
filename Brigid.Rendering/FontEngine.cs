#region
using FontStashSharp;
using FontStashSharp.Interfaces;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Text rendering backend built on FontStashSharp. Loads a real TTF and rasterizes glyphs on demand into a
///     dynamic atlas (anti-aliased, full Unicode coverage), replacing the legacy Dark Ages <c>.fnt</c> bitmap fonts.
///     <para>
///         The render pixel size is intentionally decoupled from the UI layout cell height
///         (<see cref="TextRenderer.CHAR_HEIGHT" />). Layout still positions lines on the legacy 12px grid; the glyph
///         ink is rasterized at <see cref="RENDER_SIZE" /> and nudged by <see cref="V_OFFSET" /> to sit in the row.
///         Both are single knobs, tunable without touching panel code.
///     </para>
///     <para>
///         Additional faces dropped into <c>Content/Fonts</c> and listed in <see cref="FallbackFonts" /> are added to
///         the same <see cref="FontSystem" /> as a fallback chain — FontStashSharp consults them automatically when the
///         primary face lacks a codepoint (CJK, symbols, etc.), which is what retires the legacy EUC-KR Korean path.
///     </para>
/// </summary>
public sealed class FontEngine
{
    /// <summary>Pixel size the primary face is rasterized at. Visual glyph size; not the line-grid spacing.</summary>
    public const int RENDER_SIZE = 15;

    /// <summary>Vertical nudge (px) applied to every line so the glyph ink centers in the 12px layout row.</summary>
    public const int V_OFFSET = -1;

    private const string FONTS_DIR = "Content/Fonts";
    private const string PRIMARY_FONT = "CrimsonPro-SemiBold.ttf";

    //optional fallback faces for codepoints the primary lacks (CJK etc.). Loaded only if present in Content/Fonts.
    private static readonly string[] FallbackFonts =
    [
        "NotoSansCJK-Regular.ttf",
        "NotoSansKR-Regular.ttf"
    ];

    private readonly Dictionary<int, DynamicSpriteFont> Fonts = [];
    private readonly ClippingFontRenderer Renderer = new();
    private readonly FontSystem System;

    public static FontEngine Instance { get; private set; } = null!;

    /// <summary>The font line height in pixels at <see cref="RENDER_SIZE" />.</summary>
    public int LineHeight => (int)MathF.Round(GetFont().LineHeight);

    private FontEngine()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, FONTS_DIR);
        var primaryPath = Path.Combine(dir, PRIMARY_FONT);

        if (!File.Exists(primaryPath))
            throw new FileNotFoundException($"Primary UI font not found: {primaryPath}");

        var settings = new FontSystemSettings
        {
            //match the premultiplied-alpha SpriteBatch pipeline the UI pass uses
            PremultiplyAlpha = true
        };

        System = new FontSystem(settings);
        System.AddFont(File.ReadAllBytes(primaryPath));

        foreach (var fallback in FallbackFonts)
        {
            var path = Path.Combine(dir, fallback);

            if (File.Exists(path))
                System.AddFont(File.ReadAllBytes(path));
        }
    }

    public static void Initialize() => Instance = new FontEngine();

    /// <summary>Pixel width of <paramref name="text" /> as laid out by the font (single line, no color codes).</summary>
    public int MeasureWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return (int)MathF.Round(GetFont()
            .MeasureString(text).X);
    }

    /// <summary>
    ///     Draws one line of text. <paramref name="clip" /> (when non-null) clips each glyph against the rectangle,
    ///     preserving the legacy per-glyph clipping used by scrolling labels and text-box selection.
    /// </summary>
    public void DrawLine(
        SpriteBatch spriteBatch,
        string text,
        Vector2 position,
        Color color,
        Rectangle? clip)
    {
        if (string.IsNullOrEmpty(text))
            return;

        Renderer.SpriteBatch = spriteBatch;
        Renderer.Clip = clip;

        GetFont()
            .DrawText(
                Renderer,
                text,
                new Vector2(position.X, position.Y + V_OFFSET),
                color);
    }

    private DynamicSpriteFont GetFont()
    {
        if (!Fonts.TryGetValue(RENDER_SIZE, out var font))
        {
            font = System.GetFont(RENDER_SIZE);
            Fonts[RENDER_SIZE] = font;
        }

        return font;
    }

    /// <summary>
    ///     FontStashSharp renderer that draws each glyph quad through a <see cref="SpriteBatch" />, applying an optional
    ///     per-glyph clip rectangle (mirrors the old <c>TextRenderer.ClipGlyph</c>). UI text always draws at scale 1 with
    ///     no rotation, so clipping operates directly in destination pixels.
    /// </summary>
    private sealed class ClippingFontRenderer : IFontStashRenderer
    {
        public Rectangle? Clip;
        public SpriteBatch SpriteBatch = null!;

        public GraphicsDevice GraphicsDevice => SpriteBatch.GraphicsDevice;

        public void Draw(
            Texture2D texture,
            Vector2 pos,
            Rectangle? src,
            Color color,
            float rotation,
            Vector2 scale,
            float depth)
        {
            if (src is null)
                return;

            var rect = src.Value;

            if (Clip is { } clip && !ClipGlyph(ref pos, ref rect, scale, in clip))
                return;

            SpriteBatch.Draw(
                texture,
                pos,
                rect,
                color,
                rotation,
                Vector2.Zero,
                scale,
                SpriteEffects.None,
                depth);
        }

        private static bool ClipGlyph(
            ref Vector2 position,
            ref Rectangle sourceRect,
            Vector2 scale,
            in Rectangle clipRect)
        {
            var destX = position.X;
            var destY = position.Y;
            var destW = sourceRect.Width * scale.X;
            var destH = sourceRect.Height * scale.Y;
            var destRight = destX + destW;
            var destBottom = destY + destH;

            if ((destX >= clipRect.Right) || (destRight <= clipRect.X) ||
                (destY >= clipRect.Bottom) || (destBottom <= clipRect.Y))
                return false;

            if ((destX >= clipRect.X) && (destRight <= clipRect.Right) &&
                (destY >= clipRect.Y) && (destBottom <= clipRect.Bottom))
                return true;

            var leftClip = MathF.Max(0, clipRect.X - destX);
            var topClip = MathF.Max(0, clipRect.Y - destY);
            var rightClip = MathF.Max(0, destRight - clipRect.Right);
            var bottomClip = MathF.Max(0, destBottom - clipRect.Bottom);

            sourceRect = new Rectangle(
                sourceRect.X + (int)(leftClip / scale.X),
                sourceRect.Y + (int)(topClip / scale.Y),
                sourceRect.Width - (int)((leftClip + rightClip) / scale.X),
                sourceRect.Height - (int)((topClip + bottomClip) / scale.Y));

            position = new Vector2(destX + leftClip, destY + topClip);

            return (sourceRect.Width > 0) && (sourceRect.Height > 0);
        }
    }
}
