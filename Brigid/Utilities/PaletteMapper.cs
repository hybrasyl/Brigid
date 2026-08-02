#region
using DALib.Drawing;
using SkiaSharp;
#endregion

namespace Brigid.Utilities;

/// <summary>
///     Maps an image's pixels to indices into a palette, tolerating colours the palette does not contain.
///     <para>
///         DALib's <c>GetPalettizedPixelData</c> requires every pixel to be an exact palette member and throws
///         otherwise. That contract does not hold for the output of <c>ImageProcessor.Quantize</c>: above the colour
///         limit its pixels come from the quantizer session while its palette comes from that session's palette, and
///         nothing guarantees the two agree — which crashed the client on screenshot with "Color not found in
///         palette". A screenshot is a convenience; it must not be able to kill the game loop.
///     </para>
///     <para>
///         Exact matches are used where present, so a well-formed pair round-trips identically. Anything else takes
///         the nearest palette entry rather than throwing — for a colour the quantizer itself chose, the nearest entry
///         is by construction a near-perfect match, so the saved image is visually unchanged.
///     </para>
///     <para>
///         <strong>Workaround for HTOO-15</strong> (Hybrasyl Tooling board). This class exists only because the
///         upstream pair is inconsistent; when DALib guarantees <c>Quantize</c>'s output is a member of the palette it
///         returns, delete this and call <c>GetPalettizedPixelData</c> directly. The tolerant mapping arguably belongs
///         in DALib as an overload — it is Hybrasyl-owned — which is what the ticket proposes.
///     </para>
/// </summary>
internal static class PaletteMapper
{
    /// <summary>
    ///     Indices into <paramref name="palette" />, one per pixel, row-major. Alpha is ignored: the palette formats
    ///     this feeds are opaque, matching DALib's behaviour of comparing colours at full alpha.
    /// </summary>
    public static byte[] MapToIndices(SKImage image, Palette palette)
    {
        //one map, seeded with the palette and back-filled with nearest-matches as they are resolved, so the hot loop
        //is a single lookup. Safe to share: an exact colour is already present and so never reaches Nearest, and a
        //resolved miss can only ever map to the same entry again.
        var indexOf = new Dictionary<SKColor, byte>(palette.Count);

        //first index wins, matching DALib's DistinctBy so a duplicated colour resolves the same way
        for (var i = 0; i < palette.Count; i++)
            indexOf.TryAdd(
                palette[i]
                    .WithAlpha(byte.MaxValue),
                (byte)i);

        var indices = new byte[image.Width * image.Height];

        using var pixels = image.PeekPixels();

        //a non-raster-backed image has no pixels to peek. Nothing in this client produces one, but the whole point of
        //this class is that saving a screenshot cannot take down the game loop, so it must not end in a null deref.
        if (pixels is null)
            return indices;

        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
            {
                var color = pixels.GetPixelColor(x, y)
                                  .WithAlpha(byte.MaxValue);

                if (!indexOf.TryGetValue(color, out var index))
                {
                    //memoised so an off-palette colour costs one scan of the palette, not one per pixel — a
                    //full-screen gradient would otherwise be 307k pixels times 256 entries
                    index = Nearest(color, palette);
                    indexOf[color] = index;
                }

                indices[(y * image.Width) + x] = index;
            }

        return indices;
    }

    //squared euclidean distance in RGB. Not perceptually weighted, which does not matter here: the input is a colour
    //the quantizer picked against this same palette, so the nearest entry is within rounding of it.
    private static byte Nearest(SKColor color, Palette palette)
    {
        var best = 0;
        var bestDistance = int.MaxValue;

        for (var i = 0; i < palette.Count; i++)
        {
            var entry = palette[i];
            var dr = entry.Red - color.Red;
            var dg = entry.Green - color.Green;
            var db = entry.Blue - color.Blue;
            var distance = (dr * dr) + (dg * dg) + (db * db);

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = i;

            if (distance == 0)
                break;
        }

        return (byte)best;
    }
}
