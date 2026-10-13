#region
using SkiaSharp;
#endregion

namespace Brigid.Data.AssetPacks;

/// <summary>
///     The canonical purple dye ramp of legacy item art (BRIG-41). Every dyeable legacy item palette has these six
///     colors at palette indices 98–103, and the legacy dye replaces them with the six colors of a <c>color0.tbl</c>
///     entry. A PNG item icon below sprite <see cref="ItemSpriteIds.MODERN_FIRST" /> is dyeable by the same rule: it
///     contains at least one pixel of the ramp. The ramp pixels must be fully opaque, as palette art is: the PNG decode
///     rounds the color of a semi-transparent pixel, so such a pixel is not found and not dyed.
/// </summary>
public static class ItemDyeRamp
{
    private static readonly SKColor[] RampColors =
    [
        new(0xB3, 0x93, 0xC7),
        new(0x9B, 0x7B, 0xB7),
        new(0x8F, 0x5B, 0xA3),
        new(0x7F, 0x3B, 0x93),
        new(0x47, 0x23, 0x5F),
        new(0x37, 0x00, 0x5B)
    ];

    /// <summary>The six ramp colors, lightest first, in palette-index order 98–103.</summary>
    public static IReadOnlyList<SKColor> Colors => RampColors;

    /// <summary>
    ///     The ramp index (0–5) whose RGB equals the RGB of <paramref name="pixel" />, or -1. Alpha is not compared. The
    ///     detection and the dye pass both use this test, so they cannot disagree.
    /// </summary>
    public static int IndexOf(SKColor pixel)
    {
        for (var i = 0; i < RampColors.Length; i++)
            if ((pixel.Red == RampColors[i].Red) && (pixel.Green == RampColors[i].Green) && (pixel.Blue == RampColors[i].Blue))
                return i;

        return -1;
    }

    /// <summary>True when the image has at least one visible pixel (alpha above 0) that is a ramp color.</summary>
    public static bool ContainsRampPixel(SKImage image)
    {
        using var bitmap = SKBitmap.FromImage(image);

        if (bitmap is null)
            return false;

        foreach (var pixel in bitmap.Pixels)
            if ((pixel.Alpha > 0) && (IndexOf(pixel) >= 0))
                return true;

        return false;
    }

    /// <summary>
    ///     Compares the ramp with the live legacy dye colors (entry 0 of <c>color0.tbl</c>) and gives one line for each
    ///     index that differs, or for a length that differs. No line means that the ramp agrees.
    /// </summary>
    public static IEnumerable<string> DifferencesFrom(IReadOnlyList<SKColor> legacyDefaultColors)
    {
        if (legacyDefaultColors.Count != RampColors.Length)
        {
            yield return $"the dye ramp has {RampColors.Length} colors, but color0.tbl entry 0 has {legacyDefaultColors.Count}";

            yield break;
        }

        for (var i = 0; i < RampColors.Length; i++)
        {
            var expected = RampColors[i];
            var actual = legacyDefaultColors[i];

            if ((expected.Red != actual.Red) || (expected.Green != actual.Green) || (expected.Blue != actual.Blue))
                yield return $"the dye ramp differs from color0.tbl entry 0 at index {i}: "
                             + $"expected #{expected.Red:X2}{expected.Green:X2}{expected.Blue:X2}, got #{actual.Red:X2}{actual.Green:X2}{actual.Blue:X2}";
        }
    }
}
