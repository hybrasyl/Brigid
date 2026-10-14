#region
using Microsoft.Xna.Framework;
using SkiaSharp;
#endregion

namespace Brigid.Rendering;

/// <summary>One piece of a nine-slice draw: the source rectangle in the art and the destination rectangle in the output.</summary>
internal readonly record struct SlicePatch(SKRect Source, SKRect Destination, bool IsCorner);

/// <summary>
///     Pure geometry for theme art: snapping a logical rectangle to native pixels, the nine pieces of a nine-slice part,
///     and the cover and contain fits. Separate from <see cref="ThemeArt" /> so it can be tested without a graphics
///     device.
/// </summary>
internal static class ThemeLayout
{
    /// <summary>
    ///     Converts a logical (640×480) rectangle to native pixels. Each edge is rounded on its own, so two rectangles
    ///     that share an edge in logical space also share it in native space, with no gap and no overlap.
    /// </summary>
    public static Rectangle ToNative(Rectangle logical, float scaleX, float scaleY)
    {
        var left = (int)MathF.Round(logical.Left * scaleX);
        var top = (int)MathF.Round(logical.Top * scaleY);
        var right = (int)MathF.Round(logical.Right * scaleX);
        var bottom = (int)MathF.Round(logical.Bottom * scaleY);

        return new Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>
    ///     Scales a nine-slice inset from art pixels to output pixels and rounds it to whole pixels, so that the seams
    ///     between pieces fall on pixel edges.
    /// </summary>
    /// <summary>The widest inset of a nine-slice in logical pixels: the widest art inset divided by the density, rounded up.</summary>
    public static int SliceBorder(SKRectI slice, float density)
        => (int)Math.Ceiling(Math.Max(Math.Max(slice.Left, slice.Top), Math.Max(slice.Right, slice.Bottom)) / density);

    public static SKRectI ScaleInsets(SKRectI insets, float scaleX, float scaleY)
        => new(
            (int)MathF.Round(insets.Left * scaleX),
            (int)MathF.Round(insets.Top * scaleY),
            (int)MathF.Round(insets.Right * scaleX),
            (int)MathF.Round(insets.Bottom * scaleY));

    /// <summary>
    ///     Computes the nine pieces of a nine-slice draw. <paramref name="sourceInsets" /> are in source pixels and
    ///     <paramref name="destinationInsets" /> in output pixels. Insets wider than the source are clamped to it. When the
    ///     output is smaller than its two insets on an axis, both insets shrink in proportion so the corners meet and do
    ///     not overlap. Pieces with no area are left out.
    /// </summary>
    public static List<SlicePatch> NineSlice(
        int sourceWidth,
        int sourceHeight,
        SKRectI sourceInsets,
        int destinationWidth,
        int destinationHeight,
        SKRectI destinationInsets)
    {
        var (srcLeft, srcRight) = ClampPair(sourceInsets.Left, sourceInsets.Right, sourceWidth);
        var (srcTop, srcBottom) = ClampPair(sourceInsets.Top, sourceInsets.Bottom, sourceHeight);
        var (dstLeft, dstRight) = ClampPair(destinationInsets.Left, destinationInsets.Right, destinationWidth);
        var (dstTop, dstBottom) = ClampPair(destinationInsets.Top, destinationInsets.Bottom, destinationHeight);

        float[] srcX = [0, srcLeft, sourceWidth - srcRight, sourceWidth];
        float[] srcY = [0, srcTop, sourceHeight - srcBottom, sourceHeight];
        float[] dstX = [0, dstLeft, destinationWidth - dstRight, destinationWidth];
        float[] dstY = [0, dstTop, destinationHeight - dstBottom, destinationHeight];

        var patches = new List<SlicePatch>(9);

        for (var row = 0; row < 3; row++)
            for (var column = 0; column < 3; column++)
            {
                var source = new SKRect(srcX[column], srcY[row], srcX[column + 1], srcY[row + 1]);
                var destination = new SKRect(dstX[column], dstY[row], dstX[column + 1], dstY[row + 1]);

                if ((source.Width <= 0) || (source.Height <= 0) || (destination.Width <= 0) || (destination.Height <= 0))
                    continue;

                patches.Add(new SlicePatch(source, destination, (row != 1) && (column != 1)));
            }

        return patches;
    }

    /// <summary>
    ///     The part of a source image to draw so that it covers the output: the largest centred rectangle with the
    ///     aspect ratio of the output. The overflow is cropped equally on both sides.
    /// </summary>
    public static SKRect CoverSource(int sourceWidth, int sourceHeight, int destinationWidth, int destinationHeight)
    {
        var scale = MathF.Max((float)destinationWidth / sourceWidth, (float)destinationHeight / sourceHeight);
        var width = destinationWidth / scale;
        var height = destinationHeight / scale;
        var left = (sourceWidth - width) / 2f;
        var top = (sourceHeight - height) / 2f;

        return new SKRect(left, top, left + width, top + height);
    }

    /// <summary>
    ///     Where a source image goes inside the output so that all of it shows: the largest centred rectangle with the
    ///     aspect ratio of the source.
    /// </summary>
    public static SKRect ContainDestination(int sourceWidth, int sourceHeight, int destinationWidth, int destinationHeight)
    {
        var scale = MathF.Min((float)destinationWidth / sourceWidth, (float)destinationHeight / sourceHeight);
        var width = sourceWidth * scale;
        var height = sourceHeight * scale;
        var left = (destinationWidth - width) / 2f;
        var top = (destinationHeight - height) / 2f;

        return new SKRect(left, top, left + width, top + height);
    }

    //an inset pair that does not fit its axis shrinks in proportion, so the two sides meet and keep their ratio
    private static (int Near, int Far) ClampPair(int near, int far, int length)
    {
        near = Math.Max(0, near);
        far = Math.Max(0, far);

        //long, so two very large insets cannot overflow the sum and skip the clamp
        var total = (long)near + far;

        if ((total <= length) || (total == 0))
            return (near, far);

        var scaledNear = (int)Math.Round(near * (double)length / total);

        return (scaledNear, length - scaledNear);
    }
}
