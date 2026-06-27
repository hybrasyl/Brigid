#region
using FontStashSharp;
using FontStashSharp.Interfaces;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Text rendering backend built on FontStashSharp. Loads real TTFs and rasterizes glyphs on demand into a dynamic
///     atlas (anti-aliased, full Unicode coverage), replacing the legacy Dark Ages <c>.fnt</c> bitmap fonts.
///     <para>
///         Several selectable faces are loaded up front; the active one is chosen by <see cref="CycleFont" /> /
///         <see cref="SetActiveFont" /> and persisted by the client. Each face carries its own glyph cache and a
///         vertical offset that centers its line box in the legacy 12px layout band.
///     </para>
///     <para>
///         The render pixel size is decoupled from the UI layout cell height (<see cref="TextRenderer.CHAR_HEIGHT" />):
///         layout positions lines on the 12px grid while glyphs rasterize at <see cref="RENDER_SIZE" /> (or native size
///         during the native pass). Optional CJK fallback faces are added to every face's <see cref="FontSystem" /> so
///         codepoints the primary lacks resolve automatically — what retires the legacy EUC-KR Korean path.
///     </para>
/// </summary>
public sealed class FontEngine
{
    /// <summary>Pixel size faces are rasterized at for virtual-space layout. Visual glyph size; not the line grid.</summary>
    public const int RENDER_SIZE = 15;

    /// <summary>
    ///     Global default letter-spacing (tracking) in virtual px, applied to all text in both measurement and draw so
    ///     layout stays consistent. Negative tightens. Per-call <c>characterSpacing</c> (e.g. shrink-to-fit) stacks on top.
    /// </summary>
    public const float DEFAULT_TRACKING = -1f;

    private const string FONTS_DIR = "Content/Fonts";

    //selectable UI faces, cycled in order. The first is the default/required; later entries load only if present.
    private static readonly (string Name, string File)[] FaceDefs =
    [
        ("Crimson Pro", "CrimsonPro-SemiBold.ttf"),
        ("Noto Sans Mono", "NotoSansMono-Regular.ttf")
    ];

    //optional fallback faces for codepoints the primaries lack (CJK etc.), added to every face. Loaded if present.
    private static readonly string[] FallbackFonts =
    [
        "NotoSansCJK-Regular.ttf",
        "NotoSansKR-Regular.ttf"
    ];

    private readonly Face[] Faces;
    private readonly ClippingFontRenderer Renderer = new();
    private int ActiveIndex;

    //virtual→native scale of the active draw pass. When both are 1 (the default), text is drawn into the 640×480
    //render target exactly as laid out. When the UI renders directly to the backbuffer under a Scale(sx,sy) transform,
    //these are set so glyphs rasterize at native pixel size and the per-glyph quad scale cancels the pass transform —
    //crisp text instead of a point-upscaled bitmap. Layout/measurement always stays in virtual (RENDER_SIZE) space.
    private float NativeScaleX = 1f;
    private float NativeScaleY = 1f;

    //persistent virtual→native scale used by measurement (unlike NativeScale, which is toggled only during the native
    //draw pass). UI text is always drawn at native size, so MeasureWidth must measure at that size — otherwise the
    //size-15 measured width diverges from the size-(15·scale) drawn width by a per-glyph amount that accumulates and
    //shows up as a right-edge buffer on right-aligned multi-glyph values. Set once per frame from the window ratio.
    private float LayoutScaleX = 1f;
    private float LayoutScaleY = 1f;

    public static FontEngine Instance { get; private set; } = null!;

    /// <summary>Number of selectable faces.</summary>
    public int FontCount => Faces.Length;

    /// <summary>Index of the active face (persisted by the client).</summary>
    public int ActiveFontIndex => ActiveIndex;

    /// <summary>Display name of the active face.</summary>
    public string ActiveFontName => Faces[ActiveIndex].Name;

    /// <summary>Bumped whenever the active face changes, so cached text measurements can invalidate.</summary>
    public int Generation { get; private set; }

    /// <summary>The active face's line height in pixels at <see cref="RENDER_SIZE" /> (virtual space).</summary>
    public int LineHeight => (int)MathF.Round(GetFont(RENDER_SIZE).LineHeight);

    private FontEngine(int initialIndex)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, FONTS_DIR);

        var settings = new FontSystemSettings
        {
            //match the premultiplied-alpha SpriteBatch pipeline the UI pass uses
            PremultiplyAlpha = true
        };

        //load CJK fallback bytes once, shared across every face's FontSystem
        var fallbackBytes = new List<byte[]>();

        foreach (var fallback in FallbackFonts)
        {
            var path = Path.Combine(dir, fallback);

            if (File.Exists(path))
                fallbackBytes.Add(File.ReadAllBytes(path));
        }

        var faces = new List<Face>();

        foreach (var (name, file) in FaceDefs)
        {
            var path = Path.Combine(dir, file);

            if (!File.Exists(path))
                continue;

            var system = new FontSystem(settings);
            system.AddFont(File.ReadAllBytes(path));

            foreach (var bytes in fallbackBytes)
                system.AddFont(bytes);

            var face = new Face(name, system);

            //center the line box in the 12px layout band: offset = (band - lineHeight) / 2 (negative, nudges up)
            face.VerticalOffset = (int)MathF.Round((TextRenderer.CHAR_HEIGHT - face.GetFont(RENDER_SIZE).LineHeight) / 2f);
            faces.Add(face);
        }

        if (faces.Count == 0)
            throw new FileNotFoundException($"No UI fonts found in {dir}");

        Faces = [.. faces];
        ActiveIndex = Math.Clamp(initialIndex, 0, Faces.Length - 1);
    }

    public static void Initialize(int initialFontIndex = 0) => Instance = new FontEngine(initialFontIndex);

    /// <summary>Advances to the next face (wrapping) and returns its index. Bumps <see cref="Generation" />.</summary>
    public int CycleFont()
    {
        ActiveIndex = (ActiveIndex + 1) % Faces.Length;
        Generation++;

        return ActiveIndex;
    }

    /// <summary>
    ///     Selects a face by index (clamped to the valid range). Bumps <see cref="Generation" /> when the active face
    ///     actually changes so cached text measurements invalidate. Used to apply the persisted face at startup.
    /// </summary>
    public void SetActiveFont(int index)
    {
        var clamped = Math.Clamp(index, 0, Faces.Length - 1);

        if (clamped == ActiveIndex)
            return;

        ActiveIndex = clamped;
        Generation++;
    }

    /// <summary>
    ///     Sets the persistent virtual→native scale used by <see cref="MeasureWidth" /> so measured widths match what the
    ///     native text pass actually draws. Call once per frame with the window/virtual ratio (constant unless resized).
    /// </summary>
    public void SetLayoutScale(float scaleX, float scaleY)
    {
        LayoutScaleX = scaleX;
        LayoutScaleY = scaleY;
    }

    /// <summary>Pixel width of <paramref name="text" /> as laid out by the font (single line, no color codes).</summary>
    public int MeasureWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var sanitized = SanitizeSurrogates(text);

        if ((LayoutScaleX == 1f) && (LayoutScaleY == 1f))
            return (int)MathF.Round(GetFont(RENDER_SIZE).MeasureString(sanitized, characterSpacing: DEFAULT_TRACKING).X);

        //mirror the native draw: measure at the native pixel size with native-scaled tracking, then divide back into
        //virtual space. This is what makes the measured width equal the drawn width glyph-for-glyph.
        var nativeSize = Math.Max(1, (int)MathF.Round(RENDER_SIZE * LayoutScaleY));
        var nativeWidth = GetFont(nativeSize)
            .MeasureString(sanitized, characterSpacing: DEFAULT_TRACKING * LayoutScaleX).X;

        return (int)MathF.Round(nativeWidth / LayoutScaleX);
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
        Rectangle? clip,
        float characterSpacing = 0f)
    {
        if (string.IsNullOrEmpty(text))
            return;

        Renderer.SpriteBatch = spriteBatch;
        Renderer.Clip = clip;

        var sanitized = SanitizeSurrogates(text);
        var active = Faces[ActiveIndex];
        var pos = new Vector2(position.X, position.Y + active.VerticalOffset);

        if (NativeScaleX == 1f && NativeScaleY == 1f)
        {
            active.GetFont(RENDER_SIZE)
                  .DrawText(Renderer, sanitized, pos, color, 0f, Vector2.Zero, null, 0f, characterSpacing + DEFAULT_TRACKING);

            return;
        }

        //native-resolution pass: rasterize glyphs at native pixel size, then scale the quads by the inverse of the pass
        //transform so they land at the right virtual-space size — crisp at native res. characterSpacing is in virtual
        //px, so scale it into the native font's horizontal space.
        var nativeSize = Math.Max(1, (int)MathF.Round(RENDER_SIZE * NativeScaleY));

        active.GetFont(nativeSize)
              .DrawText(
                  Renderer,
                  sanitized,
                  pos,
                  color,
                  0f,
                  Vector2.Zero,
                  new Vector2(1f / NativeScaleX, 1f / NativeScaleY),
                  0f,
                  (characterSpacing + DEFAULT_TRACKING) * NativeScaleX);
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

    private DynamicSpriteFont GetFont(int size) => Faces[ActiveIndex].GetFont(size);

    /// <summary>A selectable UI face: its own FontSystem, per-size glyph cache, and line-band centering offset.</summary>
    private sealed class Face(string name, FontSystem system)
    {
        private readonly Dictionary<int, DynamicSpriteFont> Cache = [];
        public readonly string Name = name;
        public int VerticalOffset;

        public DynamicSpriteFont GetFont(int size)
        {
            if (!Cache.TryGetValue(size, out var font))
            {
                font = system.GetFont(size);
                Cache[size] = font;
            }

            return font;
        }
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
