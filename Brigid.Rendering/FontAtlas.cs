#region
using Brigid.Data;
using DALib.Drawing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Per-glyph rendering info — atlas source region plus layout metadata. XOffset/YOffset position the glyph
///     relative to the current cursor + baseline; XAdvance is how far the cursor moves after drawing (per-glyph
///     for proportional fonts, fixed for legacy/TTF).
/// </summary>
public readonly record struct GlyphInfo(
    AtlasRegion Region,
    int XOffset,
    int YOffset,
    int XAdvance);

/// <summary>
///     Pre-built texture atlases for English and Korean font glyphs. Loading priority:
///     1. Modern BMFont pair (`Content/Fonts/{Name}.fnt` + referenced TGA atlas) — supports proportional widths
///        and extended Latin coverage.
///     2. Modern TTF rasterized at runtime via SkiaSharp — fallback if no BMFont is present.
///     3. Legacy DALib `.fnt` bitmap — fallback if neither modern option is found.
///     Korean text always uses the legacy DALib path.
/// </summary>
public sealed class FontAtlas : IDisposable
{
    private const int GLYPH_HEIGHT = 12;
    private const int LEGACY_FIRST_CODEPOINT = 33;          // '!'
    private const int LEGACY_ENGLISH_ADVANCE = 6;
    private const int MODERN_FIRST_CODEPOINT = 0x21;        // '!'
    private const int MODERN_LAST_CODEPOINT = 0x17F;        // Latin Extended-A end
    private const int MODERN_GLYPH_WIDTH = 10;
    private const float MODERN_FONT_PIXEL_SIZE = 8f;
    private const string MODERN_FONTS_RELATIVE_DIR = "Content/Fonts";
    private const string MODERN_FONT_RELATIVE_PATH = "Content/Fonts/MonoSpatial.ttf";

    //English rendering uses exactly one of these, determined by loading path. BmFontPage bypasses TextureAtlas
    //because the BMFont pipeline already produces a pre-packed atlas page.
    private readonly TextureAtlas? EnglishAtlas;
    private readonly Texture2D? BmFontPage;
    private readonly int EnglishGlyphBase;                  // codepoint → glyph index offset
    private readonly int EnglishGlyphCount;
    private readonly GlyphInfo[] EnglishGlyphs;
    private readonly TextureAtlas KoreanAtlas;
    private readonly AtlasRegion[] KoreanGlyphs;

    public static FontAtlas Instance { get; private set; } = null!;

    public bool HasModernEnglish { get; }

    private FontAtlas(GraphicsDevice device)
    {
        var fonts = DataContext.Fonts;

        var fontsDir = Path.Combine(AppContext.BaseDirectory, MODERN_FONTS_RELATIVE_DIR);
        var bmFontPath = FindFirstFile(fontsDir, "*.fnt");
        var ttfPath = Path.Combine(AppContext.BaseDirectory, MODERN_FONT_RELATIVE_PATH);

        if (bmFontPath is not null)
        {
            BmFontPage = BuildBmFontAtlas(device, bmFontPath, out EnglishGlyphs);
            EnglishGlyphBase = 0;                           // BMFont is indexed directly by codepoint
            EnglishGlyphCount = EnglishGlyphs.Length;
            HasModernEnglish = true;
        } else if (File.Exists(ttfPath))
        {
            EnglishAtlas = BuildModernEnglishAtlas(device, ttfPath, out EnglishGlyphs);
            EnglishGlyphBase = MODERN_FIRST_CODEPOINT;
            EnglishGlyphCount = EnglishGlyphs.Length;
            HasModernEnglish = true;
        } else
        {
            EnglishAtlas = BuildLegacyEnglishAtlas(device, fonts.EnglishFont, out EnglishGlyphs);
            EnglishGlyphBase = LEGACY_FIRST_CODEPOINT;
            EnglishGlyphCount = EnglishGlyphs.Length;
            HasModernEnglish = false;
        }

        KoreanAtlas = BuildLegacyKoreanAtlas(device, fonts.KoreanFont, out KoreanGlyphs);
    }

    public void Dispose()
    {
        EnglishAtlas?.Dispose();
        BmFontPage?.Dispose();
        KoreanAtlas.Dispose();
    }

    /// <summary>
    ///     Looks up an English glyph by Unicode codepoint. Returns <c>default</c> (empty region, zero advance) when
    ///     the codepoint is not mapped in this atlas.
    /// </summary>
    public GlyphInfo GetEnglishGlyphInfo(int codepoint)
    {
        var index = EnglishGlyphBase == 0 ? codepoint : codepoint - EnglishGlyphBase;

        if ((index < 0) || (index >= EnglishGlyphCount))
            return default;

        return EnglishGlyphs[index];
    }

    /// <summary>
    ///     Convenience accessor for the per-glyph advance. Returns the fixed legacy advance (6) when the codepoint
    ///     has no per-glyph value or is unmapped, so callers always get a sensible cursor increment.
    /// </summary>
    public int GetEnglishAdvance(int codepoint)
    {
        var info = GetEnglishGlyphInfo(codepoint);

        return info.XAdvance > 0 ? info.XAdvance : LEGACY_ENGLISH_ADVANCE;
    }

    public AtlasRegion GetKoreanGlyph(int glyphIndex) => KoreanGlyphs[glyphIndex];

    public static void Initialize(GraphicsDevice device) => Instance = new FontAtlas(device);

    private static string? FindFirstFile(string dir, string pattern)
    {
        if (!Directory.Exists(dir))
            return null;

        var matches = Directory.GetFiles(dir, pattern);

        return matches.Length == 0 ? null : matches[0];
    }

    private static Texture2D LoadBmFontPage(GraphicsDevice device, string pagePath)
    {
        var ext = Path.GetExtension(pagePath).ToLowerInvariant();

        if (ext == ".png")
        {
            //BMFont PNG output typically uses RGB channels for glyph intensity on an opaque black background
            //(vs. TGA output which uses the alpha channel directly). Derive alpha from the brightest RGB channel
            //and produce premultiplied white-on-transparent pixels regardless of the PNG's original alpha channel.
            using var stream = File.OpenRead(pagePath);
            var texture = Texture2D.FromStream(device, stream);
            var pixels = new Color[texture.Width * texture.Height];
            texture.GetData(pixels);

            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                var intensity = Math.Max(p.R, Math.Max(p.G, p.B));

                pixels[i] = new Color(intensity, intensity, intensity, intensity);
            }

            texture.SetData(pixels);

            return texture;
        }

        var tgaPixels = TgaLoader.Load(pagePath, out var w, out var h);
        var tgaTexture = new Texture2D(device, w, h);
        tgaTexture.SetData(tgaPixels);

        return tgaTexture;
    }

    private static Texture2D BuildBmFontAtlas(GraphicsDevice device, string fntPath, out GlyphInfo[] glyphs)
    {
        var metadata = BmFontMetadata.Parse(fntPath);

        if (metadata.Chars.Count == 0)
            throw new InvalidDataException($"BMFont {fntPath} has no char entries");

        var fontDir = Path.GetDirectoryName(fntPath)!;
        var pagePath = Path.Combine(fontDir, metadata.PageFile);

        if (!File.Exists(pagePath))
            throw new FileNotFoundException($"BMFont page texture not found: {pagePath}");

        //the BMFont pipeline already produced a pre-packed atlas — wrap the page as a single Texture2D,
        //glyph rects reference sub-regions directly.
        var pageTexture = LoadBmFontPage(device, pagePath);

        //size the lookup array to cover every declared codepoint
        var maxCodepoint = 0;

        foreach (var id in metadata.Chars.Keys)
            if (id > maxCodepoint)
                maxCodepoint = id;

        glyphs = new GlyphInfo[maxCodepoint + 1];

        foreach (var (id, entry) in metadata.Chars)
        {
            if (entry.Width == 0 || entry.Height == 0)
            {
                //space or zero-size glyph — record the advance but no drawable region
                glyphs[id] = new GlyphInfo(default, entry.XOffset, entry.YOffset, entry.XAdvance);

                continue;
            }

            var region = new AtlasRegion(
                pageTexture,
                new Rectangle(entry.X, entry.Y, entry.Width, entry.Height));
            glyphs[id] = new GlyphInfo(region, entry.XOffset, entry.YOffset, entry.XAdvance);
        }

        return pageTexture;
    }

    private static TextureAtlas BuildLegacyEnglishAtlas(GraphicsDevice device, FntFile font, out GlyphInfo[] glyphs)
    {
        var atlas = new TextureAtlas(
            device,
            PackingMode.Grid,
            font.GlyphWidth,
            GLYPH_HEIGHT);
        var bytesPerRow = (font.GlyphWidth + 7) / 8;
        var bytesPerGlyph = bytesPerRow * GLYPH_HEIGHT;

        for (var i = 0; i < font.GlyphCount; i++)
        {
            var pixels = RasterizeLegacyGlyph(
                font.Data,
                i * bytesPerGlyph,
                bytesPerRow,
                font.GlyphWidth);

            atlas.Add(
                i,
                pixels,
                font.GlyphWidth,
                GLYPH_HEIGHT);
        }

        atlas.Build();

        glyphs = new GlyphInfo[font.GlyphCount];

        for (var i = 0; i < font.GlyphCount; i++)
        {
            var region = atlas.TryGetRegion(i) ?? default;
            glyphs[i] = new GlyphInfo(region, 0, 0, LEGACY_ENGLISH_ADVANCE);
        }

        return atlas;
    }

    private static TextureAtlas BuildLegacyKoreanAtlas(GraphicsDevice device, FntFile font, out AtlasRegion[] glyphs)
    {
        var atlas = new TextureAtlas(
            device,
            PackingMode.Grid,
            font.GlyphWidth,
            GLYPH_HEIGHT);
        var bytesPerRow = (font.GlyphWidth + 7) / 8;
        var bytesPerGlyph = bytesPerRow * GLYPH_HEIGHT;

        for (var i = 0; i < font.GlyphCount; i++)
        {
            var pixels = RasterizeLegacyGlyph(
                font.Data,
                i * bytesPerGlyph,
                bytesPerRow,
                font.GlyphWidth);

            atlas.Add(
                i,
                pixels,
                font.GlyphWidth,
                GLYPH_HEIGHT);
        }

        atlas.Build();

        glyphs = new AtlasRegion[font.GlyphCount];

        for (var i = 0; i < font.GlyphCount; i++)
            glyphs[i] = atlas.TryGetRegion(i) ?? default;

        return atlas;
    }

    private static TextureAtlas BuildModernEnglishAtlas(GraphicsDevice device, string ttfPath, out GlyphInfo[] glyphs)
    {
        var atlas = new TextureAtlas(
            device,
            PackingMode.Grid,
            MODERN_GLYPH_WIDTH,
            GLYPH_HEIGHT);

        using var typeface = SKTypeface.FromFile(ttfPath);
        using var font = new SKFont(typeface, MODERN_FONT_PIXEL_SIZE)
        {
            Subpixel = false,
            Edging = SKFontEdging.Alias              //no AA — crisp binary pixels; swap to Antialias for smooth TTFs
        };

        var metrics = font.Metrics;
        var baseline = (int)Math.Round((GLYPH_HEIGHT - (metrics.Descent - metrics.Ascent)) / 2f - metrics.Ascent);

        using var paint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = false
        };

        var glyphCount = MODERN_LAST_CODEPOINT - MODERN_FIRST_CODEPOINT + 1;
        var missingCount = 0;

        for (var cp = MODERN_FIRST_CODEPOINT; cp <= MODERN_LAST_CODEPOINT; cp++)
        {
            if (typeface.GetGlyph(cp) == 0)
            {
                missingCount++;

                continue;
            }

            var pixels = RasterizeModernGlyph((char)cp, font, paint, baseline);
            atlas.Add(cp - MODERN_FIRST_CODEPOINT, pixels, MODERN_GLYPH_WIDTH, GLYPH_HEIGHT);
        }

        atlas.Build();

        glyphs = new GlyphInfo[glyphCount];

        for (var i = 0; i < glyphCount; i++)
        {
            var region = atlas.TryGetRegion(i) ?? default;
            glyphs[i] = new GlyphInfo(region, 0, 0, LEGACY_ENGLISH_ADVANCE);
        }

        if (missingCount > 0)
            Console.Error.WriteLine($"[font] {Path.GetFileName(ttfPath)}: {missingCount} of {glyphCount} codepoints missing from font (Latin Extended range); those chars will render blank");

        return atlas;
    }

    private static Color[] RasterizeLegacyGlyph(
        byte[] data,
        int offset,
        int bytesPerRow,
        int glyphWidth)
    {
        var pixels = new Color[glyphWidth * GLYPH_HEIGHT];

        for (var row = 0; row < GLYPH_HEIGHT; row++)
        {
            var rowOffset = offset + row * bytesPerRow;

            for (var byteIdx = 0; byteIdx < bytesPerRow; byteIdx++)
            {
                var dataByte = data[rowOffset + byteIdx];

                if (dataByte == 0)
                    continue;

                for (var bit = 7; bit >= 0; bit--)
                {
                    if ((dataByte & (1 << bit)) == 0)
                        continue;

                    var pixelX = byteIdx * 8 + (7 - bit);

                    if (pixelX >= glyphWidth)
                        continue;

                    pixels[row * glyphWidth + pixelX] = Color.White;
                }
            }
        }

        return pixels;
    }

    private static Color[] RasterizeModernGlyph(char c, SKFont font, SKPaint paint, int baseline)
    {
        var pixels = new Color[MODERN_GLYPH_WIDTH * GLYPH_HEIGHT];
        var info = new SKImageInfo(MODERN_GLYPH_WIDTH, GLYPH_HEIGHT, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.DrawText(c.ToString(), 0, baseline, SKTextAlign.Left, font, paint);

        using var img = surface.Snapshot();
        using var pm = img.PeekPixels();
        var span = pm.GetPixelSpan();

        for (var i = 0; i < pixels.Length; i++)
        {
            var r = span[i * 4 + 0];
            var g = span[i * 4 + 1];
            var b = span[i * 4 + 2];
            var a = span[i * 4 + 3];
            pixels[i] = new Color(r, g, b, a);
        }

        return pixels;
    }
}
