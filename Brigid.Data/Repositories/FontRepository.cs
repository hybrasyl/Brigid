#region
using DALib.Drawing;
#endregion

namespace Brigid.Data.Repositories;

/// <summary>
///     Provides bitmap font data loaded from Legend.dat: English (8x12 glyphs) and Korean (16x12 glyphs).
/// </summary>
public sealed class FontRepository
{
    private const int ENGLISH_GLYPH_WIDTH = 8;
    private const int KOREAN_GLYPH_WIDTH = 16;
    private const int GLYPH_HEIGHT = 12;

    public FntFile EnglishFont { get; } = FntFile.FromArchive(
        "eng00",
        DatArchives.Legend,
        ENGLISH_GLYPH_WIDTH,
        GLYPH_HEIGHT);

    public FntFile KoreanFont { get; } = FntFile.FromArchive(
        "han00",
        DatArchives.Legend,
        KOREAN_GLYPH_WIDTH,
        GLYPH_HEIGHT);
}