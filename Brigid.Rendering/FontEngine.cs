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
///         vertical offset that centers its line box in the legacy 12px layout band. Faces may ship optional
///         bold/italic variant files, selected per call via <see cref="FontStyle" /> (missing variants fall back to
///         the regular file); the styled draw/measure overloads also take an explicit pixel size, which is what
///         markdown rendering uses for headers and code spans.
///     </para>
///     <para>
///         The render pixel size is decoupled from the UI layout cell height (<see cref="TextRenderer.CHAR_HEIGHT" />):
///         layout positions lines on the 12px grid while glyphs rasterize at <see cref="RENDER_SIZE" /> (or native size
///         during the native pass). Optional CJK fallback faces are added to every face's <see cref="FontSystem" /> so
///         codepoints the primary lacks resolve automatically — what retires the legacy EUC-KR Korean path.
///     </para>
/// </summary>
public sealed class FontEngine : ITextMeasurer
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
    //Bold/Italic/BoldItalic are optional style-variant files resolved via FontStyle; a missing variant falls back to
    //the regular file. IsMono marks the face FontStyle.Mono resolves to, independent of the active face.
    private static readonly FaceDef[] FaceDefs =
    [
        new("Noto Sans Mono", "NotoSansMono-Regular.ttf", Bold: "NotoSansMono-Bold.ttf", IsMono: true),
        new(
            "Anonymous Mono",
            "AnonymousPro-Regular.ttf",
            Bold: "AnonymousPro-Bold.ttf",
            Italic: "AnonymousPro-Italic.ttf",
            BoldItalic: "AnonymousPro-BoldItalic.ttf"),
        new("Iosevka Mono", "IosevkaCharonMono-Regular.ttf")
    ];

    //optional fallback faces for codepoints the primaries lack (CJK, emoji), added to every face. Loaded if
    //present. NotoEmoji is the MONOCHROME variable font (glyf outlines, default instance) — FontStashSharp's
    //rasterizer cannot read color tables (CBDT/COLR), so emoji render as line art tinted with the text color.
    private static readonly string[] FallbackFonts =
    [
        "NotoSansCJK-Regular.ttf",
        "NotoSansKR-Regular.ttf",
        "NotoEmoji-Regular.ttf"
    ];

    private readonly Face[] Faces;
    private readonly ClippingFontRenderer Renderer = new();

    //index of the FontStyle.Mono face in Faces, or -1 when not loaded (Mono then falls back to the active face)
    private readonly int MonoIndex;
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

    /// <summary>
    ///     Bumped whenever the active face or the layout scale changes, so cached text measurements/wraps can
    ///     invalidate.
    /// </summary>
    public int Generation { get; private set; }

    /// <summary>The active face's line height in pixels at <see cref="RENDER_SIZE" /> (virtual space).</summary>
    public int LineHeight => (int)MathF.Round(GetFont(RENDER_SIZE).LineHeight);

    private FontEngine(int initialIndex)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, FONTS_DIR);

        var settings = new FontSystemSettings
        {
            //match the premultiplied-alpha SpriteBatch pipeline the UI pass uses
            GlyphRenderResult = GlyphRenderResult.Premultiplied
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
        var monoIndex = -1;

        foreach (var def in FaceDefs)
        {
            var path = Path.Combine(dir, def.File);

            if (!File.Exists(path))
                continue;

            var face = new Face(def.Name, LoadSystem(path));

            //optional style variants — a missing file leaves the slot null and GetFont falls back to regular
            face.SetVariant(FontStyle.Bold, TryLoadSystem(def.Bold));
            face.SetVariant(FontStyle.Italic, TryLoadSystem(def.Italic));
            face.SetVariant(FontStyle.BoldItalic, TryLoadSystem(def.BoldItalic));

            //center the line box in the 12px layout band: offset = (band - lineHeight) / 2 (negative, nudges up)
            face.VerticalOffset = (int)MathF.Round((TextRenderer.CHAR_HEIGHT - face.GetFont(FontStyle.Regular, RENDER_SIZE).LineHeight) / 2f);

            if (def.IsMono && (monoIndex < 0))
                monoIndex = faces.Count;

            faces.Add(face);
        }

        FontSystem LoadSystem(string fontPath)
        {
            var system = new FontSystem(settings);
            system.AddFont(File.ReadAllBytes(fontPath));

            foreach (var bytes in fallbackBytes)
                system.AddFont(bytes);

            return system;
        }

        FontSystem? TryLoadSystem(string? file)
        {
            if (file is null)
                return null;

            var fontPath = Path.Combine(dir, file);

            return File.Exists(fontPath) ? LoadSystem(fontPath) : null;
        }

        if (faces.Count == 0)
            throw new FileNotFoundException($"No UI fonts found in {dir}");

        Faces = [.. faces];
        MonoIndex = monoIndex;
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
    ///     Bumps <see cref="Generation" /> when the scale actually changes — measured widths depend on it, so cached
    ///     measurements/wraps must invalidate on resize.
    /// </summary>
    public void SetLayoutScale(float scaleX, float scaleY)
    {
        if ((LayoutScaleX == scaleX) && (LayoutScaleY == scaleY))
            return;

        LayoutScaleX = scaleX;
        LayoutScaleY = scaleY;
        Generation++;
    }

    /// <summary>Pixel width of <paramref name="text" /> as laid out by the font (single line, no color codes).</summary>
    public int MeasureWidth(string text) => MeasureWidth(text, RENDER_SIZE, FontStyle.Regular);

    /// <summary>
    ///     Pixel width of <paramref name="text" /> at an explicit pixel size and style (single line, no color codes).
    /// </summary>
    public int MeasureWidth(string text, int size, FontStyle style = FontStyle.Regular)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        size = Math.Max(1, size);
        var sanitized = SanitizeSurrogates(text);
        var (face, faceStyle) = Resolve(style);

        if ((LayoutScaleX == 1f) && (LayoutScaleY == 1f))
            return (int)MathF.Round(face.GetFont(faceStyle, size).MeasureString(sanitized, characterSpacing: DEFAULT_TRACKING).X);

        //mirror the native draw: measure at the native pixel size with native-scaled tracking, then divide back into
        //virtual space. This is what makes the measured width equal the drawn width glyph-for-glyph.
        var nativeSize = Math.Max(1, (int)MathF.Round(size * LayoutScaleY));
        var nativeWidth = face.GetFont(faceStyle, nativeSize)
                              .MeasureString(sanitized, characterSpacing: DEFAULT_TRACKING * LayoutScaleX).X;

        return (int)MathF.Round(nativeWidth / LayoutScaleX);
    }

    /// <summary>The line height in virtual px of <paramref name="style" /> at pixel size <paramref name="size" />.</summary>
    public int GetLineHeight(int size, FontStyle style = FontStyle.Regular)
    {
        size = Math.Max(1, size);
        var (face, faceStyle) = Resolve(style);

        return (int)MathF.Round(face.GetFont(faceStyle, size).LineHeight);
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
        float characterSpacing = 0f,
        FontStyle style = FontStyle.Regular)
    {
        if (string.IsNullOrEmpty(text))
            return;

        //legacy path: active face at RENDER_SIZE, line box centered in the 12px layout band. A style (e.g. bold)
        //resolves to the active face's variant while keeping that band centering, so styled and regular runs on the
        //same baseline align vertically. style defaults to Regular, so untyped callers are unchanged.
        var (face, faceStyle) = Resolve(style);
        var pos = new Vector2(position.X, position.Y + face.VerticalOffset);

        DrawLineCore(spriteBatch, SanitizeSurrogates(text), pos, color, clip, face, faceStyle, RENDER_SIZE, characterSpacing);
    }

    /// <summary>
    ///     Draws one line of text at an explicit pixel size and style. Unlike the legacy overload, no line-band
    ///     vertical offset is applied — the caller owns line layout (positions are the top of the line box, spaced by
    ///     <see cref="GetLineHeight" />).
    /// </summary>
    public void DrawLine(
        SpriteBatch spriteBatch,
        string text,
        Vector2 position,
        Color color,
        Rectangle? clip,
        int size,
        FontStyle style,
        float characterSpacing = 0f)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var (face, faceStyle) = Resolve(style);

        DrawLineCore(spriteBatch, SanitizeSurrogates(text), position, color, clip, face, faceStyle, Math.Max(1, size), characterSpacing);
    }

    private void DrawLineCore(
        SpriteBatch spriteBatch,
        string sanitized,
        Vector2 position,
        Color color,
        Rectangle? clip,
        Face face,
        FontStyle style,
        int size,
        float characterSpacing)
    {
        Renderer.SpriteBatch = spriteBatch;
        Renderer.Clip = clip;

        if (NativeScaleX == 1f && NativeScaleY == 1f)
        {
            face.GetFont(style, size)
                .DrawText(Renderer, sanitized, position, color, 0f, Vector2.Zero, null, 0f, characterSpacing + DEFAULT_TRACKING);

            return;
        }

        //native-resolution pass: rasterize glyphs at native pixel size, then scale the quads by the inverse of the pass
        //transform so they land at the right virtual-space size — crisp at native res. characterSpacing is in virtual
        //px, so scale it into the native font's horizontal space.
        var nativeSize = Math.Max(1, (int)MathF.Round(size * NativeScaleY));

        face.GetFont(style, nativeSize)
            .DrawText(
                Renderer,
                sanitized,
                position,
                color,
                0f,
                Vector2.Zero,
                new Vector2(1f / NativeScaleX, 1f / NativeScaleY),
                0f,
                (characterSpacing + DEFAULT_TRACKING) * NativeScaleX);
    }

    /// <summary>
    ///     Resolves a style to the face and face-local style to render with: <see cref="FontStyle.Mono" /> selects the
    ///     dedicated monospace face (falling back to the active face when none is loaded); everything else selects the
    ///     active face. The Mono flag is masked off the returned style, so <see cref="Face" /> only ever sees
    ///     Regular/Bold/Italic/BoldItalic — missing variants there fall back to the face's regular file.
    /// </summary>
    private (Face Face, FontStyle Style) Resolve(FontStyle style)
    {
        var face = (style & FontStyle.Mono) != 0 && (MonoIndex >= 0)
            ? Faces[MonoIndex]
            : Faces[ActiveIndex];

        return (face, style & ~FontStyle.Mono);
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

    private DynamicSpriteFont GetFont(int size) => Faces[ActiveIndex].GetFont(FontStyle.Regular, size);

    /// <summary>A face definition: display name, regular file, and optional style-variant files.</summary>
    private sealed record FaceDef(
        string Name,
        string File,
        string? Bold = null,
        string? Italic = null,
        string? BoldItalic = null,
        bool IsMono = false);

    /// <summary>
    ///     A selectable UI face: a FontSystem per loaded style variant, a per-(style, size) glyph cache, and a
    ///     line-band centering offset. Styles with no variant file resolve to the regular FontSystem.
    /// </summary>
    private sealed class Face
    {
        private readonly Dictionary<(FontStyle Style, int Size), DynamicSpriteFont> Cache = [];

        //indexed by FontStyle value, which Resolve masks to Regular..BoldItalic (0..3) before any Face call —
        //the Mono flag never reaches here. The Regular slot is always populated (set in the constructor).
        private readonly FontSystem?[] Variants = new FontSystem?[4];
        public readonly string Name;
        public int VerticalOffset;

        public Face(string name, FontSystem regular)
        {
            Name = name;
            Variants[(int)FontStyle.Regular] = regular;
        }

        public DynamicSpriteFont GetFont(FontStyle style, int size)
        {
            //normalize missing variants to Regular so fallback hits the same cache entry
            var effective = Variants[(int)style] is null ? FontStyle.Regular : style;

            if (!Cache.TryGetValue((effective, size), out var font))
            {
                font = Variants[(int)effective]!.GetFont(size);
                Cache[(effective, size)] = font;
            }

            return font;
        }

        public void SetVariant(FontStyle style, FontSystem? system) => Variants[(int)style] = system;
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
