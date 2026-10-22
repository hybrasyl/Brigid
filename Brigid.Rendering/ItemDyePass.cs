#region
using Brigid.Data.AssetPacks;
using DALib.Drawing;
using SkiaSharp;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Runtime find-and-replace dye pass for PNG-sourced item icons in <c>.datf</c> asset packs. Replaces pixels matching
///     the canonical purple ramp (legacy palette indices 98–103) with the supplied 6-color replacement ramp from
///     <see cref="ColorTable" />, preserving the source pixel's alpha. Used by <c>ItemIconSource</c> when the item pack
///     that gives an icon says that the item is dyeable (<c>ItemPack.IsDyeable</c>) and the server-sent color byte is
///     non-zero.
/// </summary>
/// <remarks>
///     Mirrors the legacy <c>Palette.Dye(ColorTableEntry)</c> operation that copies six dye colors over palette slots
///     98–103 before rendering; here the dye math happens after rasterization because PNG packs carry no palette.
/// </remarks>
public static class ItemDyePass
{
    /// <summary>
    ///     Returns a new <see cref="SKImage" /> with canonical-purple-ramp pixels replaced by the supplied 6-color
    ///     replacement ramp, preserving alpha. Pixels that don't match any of the six canonical purples (RGB-only
    ///     comparison) are left untouched. The source image is not mutated; the caller continues to own it.
    /// </summary>
    /// <param name="source">PNG-decoded item icon to recolor.</param>
    /// <param name="replacement">
    ///     Dye color table entry whose <see cref="ColorTableEntry.Colors" /> provides the six replacement RGB values,
    ///     index-aligned with <see cref="ItemDyeRamp.Colors" />.
    /// </param>
    public static SKImage ApplyDyeFindReplace(SKImage source, ColorTableEntry replacement)
    {
        using var sourceBitmap = SKBitmap.FromImage(source);
        var pixels = sourceBitmap.Pixels;

        var replace = replacement.Colors;

        //ItemDyeRamp.IndexOf is the same test that the dyeability detection uses, so the two cannot disagree
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            var index = ItemDyeRamp.IndexOf(p);

            if (index < 0)
                continue;

            var target = replace[index];
            pixels[i] = new SKColor(target.Red, target.Green, target.Blue, p.Alpha);
        }

        var resultBitmap = new SKBitmap(sourceBitmap.Info);
        resultBitmap.Pixels = pixels;

        //SKImage.FromBitmap copies/owns the bitmap; dispose our temp so we don't leak the native handle.
        var image = SKImage.FromBitmap(resultBitmap);
        resultBitmap.Dispose();
        return image;
    }
}
