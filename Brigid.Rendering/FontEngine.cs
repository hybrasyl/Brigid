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

    //virtual→native scale of the active draw pass. When both are 1 (the default), text is drawn into the 640×480
    //render target exactly as laid out. When the UI renders directly to the backbuffer under a Scale(sx,sy) transform,
    //these are set so glyphs rasterize at native pixel size and the per-glyph quad scale cancels the pass transform —
    //crisp text instead of a point-upscaled bitmap. Layout/measurement always stays in virtual (RENDER_SIZE) space.
    private float NativeScaleX = 1f;
    private float NativeScaleY = 1f;

    //vertical nudge (virtual px) that centers the font's line box inside the legacy CHAR_HEIGHT (12px) layout band.
    //UILabel lays text out assuming a 12px-tall line; the TTF's line height is taller and FontStashSharp anchors by
    //the line top, so without this every label would sit low (bottom-aligned). Computed from the face's metrics.
    private int VerticalOffset;

    public static FontEngine Instance { get; private set; } = null!;

    /// <summary>The font line height in pixels at <see cref="RENDER_SIZE" /> (virtual space).</summary>
    public int LineHeight => (int)MathF.Round(GetFont(RENDER_SIZE).LineHeight);

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

        //center the line box in the 12px layout band: offset = (band - lineHeight) / 2 (negative, nudges text up)
        VerticalOffset = (int)MathF.Round((TextRenderer.CHAR_HEIGHT - GetFont(RENDER_SIZE).LineHeight) / 2f);
    }

    public static void Initialize() => Instance = new FontEngine();

    /// <summary>Pixel width of <paramref name="text" /> as laid out by the font (single line, no color codes).</summary>
    public int MeasureWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return (int)MathF.Round(GetFont(RENDER_SIZE)
            .MeasureString(SanitizeSurrogates(text)).X);
    }

    /// <summary>
    ///     Sets the virtual→native scale for subsequent <see cref="DrawLine" /> calls. Pass <c>(1, 1)</c> to draw at
    ///     virtual resolution (into the render target); pass the window's backbuffer/virtual ratio to draw crisply at
    ///     native resolution under a matching <c>Scale(sx, sy)</c> SpriteBatch transform.
    /// </summary>
    public void SetNativeScale(float scaleX, float scaleY)
    {
        NativeScaleX = scaleX;
        NativeScaleY = scaleY;
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

        var sanitized = SanitizeSurrogates(text);
        var pos = new Vector2(position.X, position.Y + VerticalOffset);

        if (NativeScaleX == 1f && NativeScaleY == 1f)
        {
            GetFont(RENDER_SIZE)
                .DrawText(Renderer, sanitized, pos, color);

            return;
        }

        //native-resolution pass: rasterize glyphs at native pixel size, then scale the quads by the inverse of the
        //pass transform so they land at the same virtual-space size — net 1:1 at native resolution, i.e. crisp.
        var nativeSize = Math.Max(1, (int)MathF.Round(RENDER_SIZE * NativeScaleY));

        GetFont(nativeSize)
            .DrawText(
                Renderer,
                sanitized,
                pos,
                color,
                0f,
                Vector2.Zero,
                new Vector2(1f / NativeScaleX, 1f / NativeScaleY));
    }

    /// <summary>
    ///     Replaces unpaired UTF-16 surrogates with the replacement character so FontStashSharp's codepoint decoder
    ///     never sees malformed UTF-16. This happens routinely while typing astral characters (emoji): text input is
    ///     delivered one <see cref="char" /> at a time, so the buffer transiently holds a lone surrogate, and
    ///     <see cref="char.ConvertToUtf32(string, int)" /> throws on it. Returns the original string unchanged (no
    ///     allocation) when it is already well-formed, which is the overwhelmingly common case.
    /// </summary>
    private static string SanitizeSurrogates(string text)
    {
        var needsFix = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (char.IsHighSurrogate(c))
            {
                if ((i + 1 < text.Length) && char.IsLowSurrogate(text[i + 1]))
                {
                    i++;                       // valid pair — skip the low half

                    continue;
                }

                needsFix = true;               // lone high surrogate

                break;
            }

            if (char.IsLowSurrogate(c))
            {
                needsFix = true;               // lone low surrogate

                break;
            }
        }

        if (!needsFix)
            return text;

        var chars = text.ToCharArray();

        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];

            if (char.IsHighSurrogate(c))
            {
                if ((i + 1 < chars.Length) && char.IsLowSurrogate(chars[i + 1]))
                {
                    i++;

                    continue;
                }

                chars[i] = '�';
            } else if (char.IsLowSurrogate(c))
            {
                chars[i] = '�';
            }
        }

        return new string(chars);
    }

    private DynamicSpriteFont GetFont(int size)
    {
        if (!Fonts.TryGetValue(size, out var font))
        {
            font = System.GetFont(size);
            Fonts[size] = font;
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
