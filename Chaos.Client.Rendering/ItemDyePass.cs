#region
using DALib.Drawing;
using SkiaSharp;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Runtime find-and-replace dye pass for PNG-sourced item icons in <c>.datf</c> asset packs. Replaces pixels matching
///     the canonical purple ramp (legacy palette indices 98–103) with the supplied 6-color replacement ramp from
///     <see cref="ColorTable" />, preserving the source pixel's alpha. Used by <c>ItemRenderer</c> when an asset pack
///     covers an item that the manifest's <c>dyeable</c> set marks as dye-eligible and the server-sent color byte is
///     non-zero.
/// </summary>
/// <remarks>
///     Mirrors the legacy <c>Palette.Dye(ColorTableEntry)</c> operation that copies six dye colors over palette slots
///     98–103 before rendering; here the dye math happens after rasterization because PNG packs carry no palette.
/// </remarks>
public static class ItemDyePass
{
    /// <summary>
    ///     The six-color ramp that occupies legacy palette indices 98–103 on every dyeable item palette. PNG packs replace
    ///     pixels matching this ramp with the active dye entry to reproduce legacy dye behavior. Verified at startup via
    ///     <see cref="VerifyAgainstLegacy" />.
    /// </summary>
    public static readonly SKColor[] CanonicalPurpleRamp =
    [
        new SKColor(0xB3, 0x93, 0xC7),
        new SKColor(0x9B, 0x7B, 0xB7),
        new SKColor(0x8F, 0x5B, 0xA3),
        new SKColor(0x7F, 0x3B, 0x93),
        new SKColor(0x47, 0x23, 0x5F),
        new SKColor(0x37, 0x00, 0x5B)
    ];

    /// <summary>
    ///     Returns a new <see cref="SKImage" /> with canonical-purple-ramp pixels replaced by the supplied 6-color
    ///     replacement ramp, preserving alpha. Pixels that don't match any of the six canonical purples (RGB-only
    ///     comparison) are left untouched. The source image is not mutated; the caller continues to own it.
    /// </summary>
    /// <param name="source">PNG-decoded item icon to recolor.</param>
    /// <param name="replacement">
    ///     Dye color table entry whose <see cref="ColorTableEntry.Colors" /> provides the six replacement RGB values,
    ///     index-aligned with <see cref="CanonicalPurpleRamp" />.
    /// </param>
    public static SKImage ApplyDyeFindReplace(SKImage source, ColorTableEntry replacement)
    {
        using var sourceBitmap = SKBitmap.FromImage(source);
        var pixels = sourceBitmap.Pixels;

        var replace = replacement.Colors;

        //pack canonical purples into uint RGB keys once so the inner comparison is two byte loads + an int compare,
        //not six SKColor field reads per match attempt.
        Span<uint> findRgb = stackalloc uint[6];
        Span<uint> replaceRgb = stackalloc uint[6];

        for (var i = 0; i < 6; i++)
        {
            var f = CanonicalPurpleRamp[i];
            findRgb[i] = ((uint)f.Red << 16) | ((uint)f.Green << 8) | f.Blue;

            var r = replace[i];
            replaceRgb[i] = ((uint)r.Red << 16) | ((uint)r.Green << 8) | r.Blue;
        }

        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            var rgb = ((uint)p.Red << 16) | ((uint)p.Green << 8) | p.Blue;

            for (var j = 0; j < 6; j++)
            {
                if (rgb != findRgb[j])
                    continue;

                var target = replaceRgb[j];
                pixels[i] = new SKColor(
                    (byte)((target >> 16) & 0xFF),
                    (byte)((target >> 8) & 0xFF),
                    (byte)(target & 0xFF),
                    p.Alpha);
                break;
            }
        }

        var resultBitmap = new SKBitmap(sourceBitmap.Info);
        resultBitmap.Pixels = pixels;

        //SKImage.FromBitmap copies/owns the bitmap; dispose our temp so we don't leak the native handle.
        var image = SKImage.FromBitmap(resultBitmap);
        resultBitmap.Dispose();
        return image;
    }

    /// <summary>
    ///     Verifies <see cref="CanonicalPurpleRamp" /> matches the supplied legacy palette-0 colors (typically
    ///     <c>DataContext.AislingDrawData.DyeColorTable[0].Colors</c>). Logs a single-line warning to stderr per
    ///     mismatched index; does not throw. Intended to run once at startup so a divergence between the hardcoded ramp
    ///     and the live legacy data surfaces as a console diagnostic.
    /// </summary>
    public static void VerifyAgainstLegacy(SKColor[] legacyPaletteZeroColors)
    {
        if (legacyPaletteZeroColors.Length != CanonicalPurpleRamp.Length)
        {
            Console.Error.WriteLine(
                $"[asset-pack] WARNING: canonical purple ramp length {CanonicalPurpleRamp.Length} differs from legacy palette 0 length {legacyPaletteZeroColors.Length}");
            return;
        }

        for (var i = 0; i < CanonicalPurpleRamp.Length; i++)
        {
            var expected = CanonicalPurpleRamp[i];
            var actual = legacyPaletteZeroColors[i];

            if ((expected.Red == actual.Red) && (expected.Green == actual.Green) && (expected.Blue == actual.Blue))
                continue;

            Console.Error.WriteLine(
                $"[asset-pack] WARNING: canonical purple ramp diverges from legacy palette 0 at index {i}: expected #{expected.Red:X2}{expected.Green:X2}{expected.Blue:X2}, got #{actual.Red:X2}{actual.Green:X2}{actual.Blue:X2}");
        }
    }
}
