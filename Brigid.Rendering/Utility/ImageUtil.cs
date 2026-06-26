#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Brigid.Rendering.Utility;

/// <summary>
///     Stateless CPU pixel-manipulation primitives. Operates on <see cref="Color" /> arrays (premultiplied RGBA) or
///     produces new <see cref="Texture2D" /> instances. All device-requiring helpers take an explicit
///     <see cref="GraphicsDevice" /> — no singleton dependency.
/// </summary>
/// <remarks>
///     Naming convention: <c>Build*</c> returns a new <see cref="Texture2D" />; <c>Apply*</c>/<c>Fill*</c>/<c>Draw*</c>
///     mutate a <see cref="Color" /> array in place and return nothing.
/// </remarks>
public static class ImageUtil
{
    /// <summary>Warm gold 50/50 blend used for group-member highlights.</summary>
    public static readonly Color GroupTint = LegendColors.CanaryYellow;

    /// <summary>Red 50/50 blend used for the projectile-impact flash.</summary>
    public static readonly Color HitTint = LegendColors.Red;

    /// <summary>
    ///     Applies a 50/50 per-channel additive blend with <paramref name="tint" /> in-place. Alpha is preserved and
    ///     fully-transparent pixels are skipped.
    /// </summary>
    public static void Blend50(Color[] pixels, int count, Color tint)
    {
        for (var i = 0; i < count; i++)
        {
            var p = pixels[i];

            if (p.A == 0)
                continue;

            pixels[i] = new Color(
                (byte)((p.R + tint.R) / 2),
                (byte)((p.G + tint.G) / 2),
                (byte)((p.B + tint.B) / 2),
                p.A);
        }
    }

    /// <summary>
    ///     Applies a blue-shift tint to a pixel array in-place. Used for entity hover highlights.
    /// </summary>
    public static void ApplyHoverTint(Color[] pixels, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var p = pixels[i];

            if (p.A == 0)
                continue;

            var r = Math.Clamp((128 * p.R + 2 * p.B) / 256 + 59, 0, 255);
            var g = Math.Clamp((131 * p.G - 2 * p.B) / 256 + 82, 0, 255);
            var b = Math.Clamp((133 * p.B - 2 * p.G) / 256 + 120, 0, 255);

            pixels[i] = new Color(
                (byte)r,
                (byte)g,
                (byte)b,
                p.A);
        }
    }

    /// <summary>
    ///     Blends the lower portion of <paramref name="pixels" /> toward <paramref name="tint" /> using its alpha as the
    ///     blend weight. The tint region starts at row <c>(feetAnchorY - paintHeight)</c> clamped to
    ///     <paramref name="height" /> and extends to the buffer bottom. Used for partial entity/item submersion on
    ///     ground-tinted tiles.
    /// </summary>
    public static void ApplyGroundTint(Color[] pixels, int width, int height, int paintHeight, int feetAnchorY, Color tint)
    {
        var tintTop = feetAnchorY - paintHeight;
        var startRow = Math.Clamp(tintTop, 0, height);

        var tintR = tint.R;
        var tintG = tint.G;
        var tintB = tint.B;
        var alpha = tint.A / 255f;

        for (var y = startRow; y < height; y++)
        {
            var rowStart = y * width;

            for (var x = 0; x < width; x++)
            {
                var i = rowStart + x;
                var pixel = pixels[i];

                if (pixel.A == 0)
                    continue;

                //unpremultiply, lerp toward tint color, re-premultiply
                var a = pixel.A / 255f;
                var r = (byte)(pixel.R / a * (1 - alpha) + tintR * alpha);
                var g = (byte)(pixel.G / a * (1 - alpha) + tintG * alpha);
                var b = (byte)(pixel.B / a * (1 - alpha) + tintB * alpha);

                pixels[i] = new Color(
                    (byte)(r * a),
                    (byte)(g * a),
                    (byte)(b * a),
                    pixel.A);
            }
        }
    }

    /// <summary>
    ///     Returns a new texture that is a 50/50 warm-gold tint of <paramref name="source" /> for group-member highlight.
    /// </summary>
    public static Texture2D BuildGroupTinted(GraphicsDevice device, Texture2D source)
    {
        using var scope = new PixelBufferScope(source);
        Blend50(scope.Pixels, scope.Count, GroupTint);

        var result = new Texture2D(device, scope.Width, scope.Height);
        scope.CommitTo(result);

        return result;
    }

    /// <summary>
    ///     Returns a new texture that is a 50/50 red tint of <paramref name="source" /> for the projectile-impact flash.
    /// </summary>
    public static Texture2D BuildHitTinted(GraphicsDevice device, Texture2D source)
    {
        using var scope = new PixelBufferScope(source);
        Blend50(scope.Pixels, scope.Count, HitTint);

        var result = new Texture2D(device, scope.Width, scope.Height);
        scope.CommitTo(result);

        return result;
    }

    /// <summary>
    ///     Returns a new texture that is a blue-shift tinted copy of <paramref name="source" /> for hover highlights.
    /// </summary>
    public static Texture2D BuildHoverTinted(GraphicsDevice device, Texture2D source)
    {
        using var scope = new PixelBufferScope(source);
        ApplyHoverTint(scope.Pixels, scope.Count);

        var result = new Texture2D(device, scope.Width, scope.Height);
        scope.CommitTo(result);

        return result;
    }

    /// <summary>
    ///     Returns a new texture whose lower portion (from <c>feetAnchorY - paintHeight</c> down) is blended toward
    ///     <paramref name="tint" /> using its alpha as the blend weight. Used to partially submerge entities/items on
    ///     ground-tinted tiles.
    /// </summary>
    public static Texture2D BuildGroundTinted(GraphicsDevice device, Texture2D source, int paintHeight, int feetAnchorY, Color tint)
    {
        using var scope = new PixelBufferScope(source);
        ApplyGroundTint(scope.Pixels, scope.Width, scope.Height, paintHeight, feetAnchorY, tint);

        var result = new Texture2D(device, scope.Width, scope.Height);
        scope.CommitTo(result);

        return result;
    }

    /// <summary>
    ///     Fills <paramref name="pixels" /> in-place with a 2-color checker pattern of <paramref name="cellSize" />-pixel
    ///     cells. <paramref name="pixels" /> must contain at least <c>totalSize * totalSize</c> entries.
    /// </summary>
    public static void FillCheckerPattern(Color[] pixels, int totalSize, int cellSize, Color a, Color b)
    {
        for (var y = 0; y < totalSize; y++)
            for (var x = 0; x < totalSize; x++)
            {
                var cellX = x / cellSize;
                var cellY = y / cellSize;
                pixels[y * totalSize + x] = ((cellX + cellY) % 2) == 0 ? a : b;
            }
    }

    /// <summary>
    ///     Returns a new <see cref="CachedTexture2D" /> containing a <paramref name="totalSize" />x<paramref name="totalSize" />
    ///     checker pattern with <paramref name="cellSize" />-pixel cells. Used for missing-asset placeholders.
    /// </summary>
    public static CachedTexture2D BuildCheckerCached(GraphicsDevice device, int totalSize, int cellSize, Color a, Color b)
    {
        var pixels = new Color[totalSize * totalSize];
        FillCheckerPattern(pixels, totalSize, cellSize, a, b);

        var result = new CachedTexture2D(device, totalSize, totalSize);
        result.SetData(pixels);

        return result;
    }

    /// <summary>
    ///     Fills the axis-aligned rectangle <c>(x, y, x+w, y+h)</c> in <paramref name="pixels" /> with
    ///     <paramref name="color" />. Rect coordinates are clamped to buffer bounds.
    /// </summary>
    public static void FillRect(Color[] pixels, int bufferWidth, int bufferHeight, int x, int y, int w, int h, Color color)
    {
        var x0 = Math.Max(0, x);
        var y0 = Math.Max(0, y);
        var x1 = Math.Min(bufferWidth, x + w);
        var y1 = Math.Min(bufferHeight, y + h);

        for (var yy = y0; yy < y1; yy++)
        {
            var rowStart = yy * bufferWidth;

            for (var xx = x0; xx < x1; xx++)
                pixels[rowStart + xx] = color;
        }
    }

    /// <summary>
    ///     Shifts every odd-indexed row (y=1, 3, 5, …) in <paramref name="pixels" /> rightward by <paramref name="step" />
    ///     pixels and clears the leftmost <paramref name="step" /> pixels on those rows to
    ///     <see cref="Color.Transparent" />. Operates in-place. <paramref name="sourceWidth" /> is the current occupied
    ///     width of each row (&lt;= <paramref name="width" />); the buffer may be wider to accommodate additional shifts.
    /// </summary>
    public static void ShiftOddRowsRight(Color[] pixels, int width, int height, int sourceWidth, int step)
    {
        for (var row = 1; row < height; row += 2)
        {
            var rowStart = row * width;
            for (var col = sourceWidth - 1; col >= 0; col--)
                pixels[rowStart + col + step] = pixels[rowStart + col];
            for (var col = 0; col < step; col++)
                pixels[rowStart + col] = Color.Transparent;
        }
    }

    /// <summary>
    ///     For each offset <c>(dx, dy)</c> in <paramref name="quarter" />, sets the four mirrored pixels
    ///     <c>(cx±dx, cy±dy)</c> to <paramref name="color" />. Offsets that fall outside the buffer are silently skipped.
    ///     Used to rasterize shapes with 4-way symmetry (circles, ellipses, isometric tile cursors) from a single-quadrant
    ///     offset list.
    /// </summary>
    public static void DrawProjectedQuadrants(
        Color[] pixels,
        int bufferWidth,
        int bufferHeight,
        int cx,
        int cy,
        ReadOnlySpan<Point> quarter,
        Color color)
    {
        foreach (var p in quarter)
        {
            SetPixel(pixels, bufferWidth, bufferHeight, cx + p.X, cy + p.Y, color);
            SetPixel(pixels, bufferWidth, bufferHeight, cx - p.X, cy + p.Y, color);
            SetPixel(pixels, bufferWidth, bufferHeight, cx + p.X, cy - p.Y, color);
            SetPixel(pixels, bufferWidth, bufferHeight, cx - p.X, cy - p.Y, color);
        }
    }

    /// <summary>Writes <paramref name="color" /> at <c>(x, y)</c> iff the coordinates are inside the buffer.</summary>
    private static void SetPixel(Color[] pixels, int width, int height, int x, int y, Color color)
    {
        if (((uint)x < (uint)width) && ((uint)y < (uint)height))
            pixels[y * width + x] = color;
    }

    /// <summary>
    ///     Returns a square texture with a <paramref name="borderWidth" />-thick border of <paramref name="borderColor" />
    ///     and a transparent interior.
    /// </summary>
    public static Texture2D BuildFilledBorder(GraphicsDevice device, int size, int borderWidth, Color borderColor)
    {
        var pixels = new Color[size * size];

        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var onBorder = (x < borderWidth) || (x >= size - borderWidth) || (y < borderWidth) || (y >= size - borderWidth);
                pixels[y * size + x] = onBorder ? borderColor : Color.Transparent;
            }

        var tex = new Texture2D(device, size, size);
        tex.SetData(pixels);
        return tex;
    }

    /// <summary>
    ///     Returns a new <see cref="CachedTexture2D" /> that is a 50/50 blend of <paramref name="source" /> with
    ///     <paramref name="tint" />. Used for cooldown overlays on skill/spell icons.
    /// </summary>
    public static CachedTexture2D BuildCooldownTintedCached(GraphicsDevice device, Texture2D source, Color tint)
    {
        using var scope = new PixelBufferScope(source);
        Blend50(scope.Pixels, scope.Count, tint);

        var result = new CachedTexture2D(device, scope.Width, scope.Height);
        scope.CommitTo(result);

        return result;
    }

    /// <summary>
    ///     Returns a new 1×<paramref name="height" /> texture containing a linear vertical alpha gradient from
    ///     <paramref name="startAlpha" /> (top row) to <paramref name="endAlpha" /> (bottom row) over
    ///     <paramref name="baseColor" />'s RGB channels. Used for dialog darkening overlays.
    /// </summary>
    public static Texture2D BuildVerticalAlphaGradient(GraphicsDevice device, int height, Color baseColor, byte startAlpha, byte endAlpha)
    {
        var pixels = new Color[height];
        for (var y = 0; y < height; y++)
        {
            var alpha = (byte)Math.Clamp(y * (endAlpha - startAlpha) / Math.Max(1, height - 1) + startAlpha, 0, 255);
            pixels[y] = new Color(baseColor.R, baseColor.G, baseColor.B, alpha);
        }
        var tex = new Texture2D(device, 1, height);
        tex.SetData(pixels);
        return tex;
    }

    /// <summary>
    ///     Draws a horizontally-stretchable rounded capsule (chat-bubble body) into <paramref name="pixels" /> at
    ///     <c>(x, y)</c> with the given <paramref name="width" /> and <paramref name="height" />. The outline is painted
    ///     with <paramref name="border" /> and the interior with <paramref name="fill" />. Rows are inset by a fixed
    ///     5-step curve at the top and bottom to produce the rounded-capsule silhouette.
    /// </summary>
    public static void DrawBubbleBody(
        Color[] pixels,
        int bufferWidth,
        int x,
        int y,
        int width,
        int height,
        Color border,
        Color fill)
    {
        ReadOnlySpan<int> topInset =
        [
            5,
            3,
            2,
            1,
            1
        ];

        ReadOnlySpan<int> bottomInset =
        [
            1,
            1,
            2,
            3,
            5
        ];

        for (var row = 0; row < height; row++)
        {
            int li,
                ri;

            if (row < topInset.Length)
            {
                li = topInset[row];
                ri = topInset[row];
            } else if (row >= (height - bottomInset.Length))
            {
                var bottomRow = row - (height - bottomInset.Length);
                li = bottomInset[bottomRow];
                ri = bottomInset[bottomRow];
            } else
            {
                li = 0;
                ri = 0;
            }

            var startX = x + li;
            var endX = x + width - 1 - ri;

            if (startX > endX)
                continue;

            //fill
            for (var col = startX + 1; col <= (endX - 1); col++)
                SetPixelByLength(
                    pixels,
                    bufferWidth,
                    col,
                    y + row,
                    fill);

            //border
            SetPixelByLength(
                pixels,
                bufferWidth,
                startX,
                y + row,
                border);

            SetPixelByLength(
                pixels,
                bufferWidth,
                endX,
                y + row,
                border);

            //top and bottom edge spans
            if ((row == 0) || (row == (height - 1)))
                for (var col = startX; col <= endX; col++)
                    SetPixelByLength(
                        pixels,
                        bufferWidth,
                        col,
                        y + row,
                        border);

            //smooth horizontal transitions when inset changes from previous row
            if (row > 0)
            {
                var prevLi = GetBubbleInset(
                    row - 1,
                    height,
                    topInset,
                    bottomInset);
                var prevRi = prevLi;

                if (li < prevLi)
                    for (var col = x + li; col < (x + prevLi); col++)
                        SetPixelByLength(
                            pixels,
                            bufferWidth,
                            col,
                            y + row,
                            border);

                if (ri < prevRi)
                    for (var col = x + width - prevRi; col <= (x + width - 1 - ri); col++)
                        SetPixelByLength(
                            pixels,
                            bufferWidth,
                            col,
                            y + row,
                            border);
            }

            if (row < (height - 1))
            {
                var nextLi = GetBubbleInset(
                    row + 1,
                    height,
                    topInset,
                    bottomInset);
                var nextRi = nextLi;

                if (li < nextLi)
                    for (var col = x + li; col < (x + nextLi); col++)
                        SetPixelByLength(
                            pixels,
                            bufferWidth,
                            col,
                            y + row,
                            border);

                if (ri < nextRi)
                    for (var col = x + width - nextRi; col <= (x + width - 1 - ri); col++)
                        SetPixelByLength(
                            pixels,
                            bufferWidth,
                            col,
                            y + row,
                            border);
            }
        }
    }

    /// <summary>
    ///     Draws a downward-pointing chat-bubble tail (the small triangular spike that hangs from the bubble's bottom
    ///     edge) centered horizontally at <paramref name="centerX" /> with its base at row <paramref name="baseY" />.
    ///     The outline is painted with <paramref name="border" /> and the interior with <paramref name="fill" />.
    /// </summary>
    public static void DrawBubbleTail(
        Color[] pixels,
        int bufferWidth,
        int centerX,
        int baseY,
        Color border,
        Color fill)
    {
        var startPx = centerX - 4;

        for (var i = 0; i < 7; i++)
            SetPixelByLength(
                pixels,
                bufferWidth,
                startPx + i,
                baseY,
                fill);

        for (var i = 0; i < 7; i++)
            SetPixelByLength(
                pixels,
                bufferWidth,
                startPx + i,
                baseY + 1,
                i is < 2 or > 4 ? border : fill);

        for (var i = 2; i < 5; i++)
            SetPixelByLength(
                pixels,
                bufferWidth,
                startPx + i,
                baseY + 2,
                i == 3 ? fill : border);

        SetPixelByLength(
            pixels,
            bufferWidth,
            startPx + 3,
            baseY + 3,
            border);

        SetPixelByLength(
            pixels,
            bufferWidth,
            startPx + 3,
            baseY + 4,
            border);
    }

    /// <summary>
    ///     Returns the left/right edge inset for bubble <paramref name="row" />. The top <c>topInset.Length</c> rows and
    ///     bottom <c>bottomInset.Length</c> rows are inset to form the rounded caps; interior rows have zero inset.
    /// </summary>
    private static int GetBubbleInset(
        int row,
        int height,
        ReadOnlySpan<int> topInset,
        ReadOnlySpan<int> bottomInset)
    {
        if (row < topInset.Length)
            return topInset[row];

        if (row >= (height - bottomInset.Length))
            return bottomInset[row - (height - bottomInset.Length)];

        return 0;
    }

    /// <summary>
    ///     Bounds-checked pixel write for the bubble primitives. Derives buffer height from
    ///     <c>pixels.Length / bufferWidth</c> so callers only have to supply the bufferWidth.
    /// </summary>
    private static void SetPixelByLength(
        Color[] pixels,
        int bufferWidth,
        int x,
        int y,
        Color color)
    {
        if ((x < 0) || (y < 0))
            return;

        var h = pixels.Length / bufferWidth;

        if ((x >= bufferWidth) || (y >= h))
            return;

        pixels[y * bufferWidth + x] = color;
    }

    /// <summary>
    ///     Produces a half-resolution copy of <paramref name="src" /> via a 2x2 arithmetic-mean box filter. Each destination
    ///     pixel is the per-channel rounded average of the 2x2 source block at <c>(2x, 2y)</c>. When <c>dstWidth * 2</c> or
    ///     <c>dstHeight * 2</c> is less than the source dimension, trailing source columns/rows are dropped.
    /// </summary>
    /// <param name="src">Source pixel buffer in row-major order.</param>
    /// <param name="srcWidth">Width of the source buffer in pixels.</param>
    /// <param name="dstWidth">Destination width in pixels. Must satisfy <c>dstWidth * 2 &lt;= srcWidth</c>.</param>
    /// <param name="dstHeight">Destination height in pixels. Caller must ensure the source buffer has at least <c>dstHeight * 2</c> rows.</param>
    public static SKColor[] DownsampleIcon(SKColor[] src, int srcWidth, int dstWidth, int dstHeight)
    {
        var dst = new SKColor[dstWidth * dstHeight];

        for (var y = 0; y < dstHeight; y++)
        {
            var sy = y * 2;
            var rowA = sy * srcWidth;
            var rowB = rowA + srcWidth;

            for (var x = 0; x < dstWidth; x++)
            {
                var sx = x * 2;
                var p00 = src[rowA + sx];
                var p01 = src[rowA + sx + 1];
                var p10 = src[rowB + sx];
                var p11 = src[rowB + sx + 1];

                var r = (p00.Red + p01.Red + p10.Red + p11.Red + 2) >> 2;
                var g = (p00.Green + p01.Green + p10.Green + p11.Green + 2) >> 2;
                var b = (p00.Blue + p01.Blue + p10.Blue + p11.Blue + 2) >> 2;
                var a = (p00.Alpha + p01.Alpha + p10.Alpha + p11.Alpha + 2) >> 2;

                dst[y * dstWidth + x] = new SKColor((byte)r, (byte)g, (byte)b, (byte)a);
            }
        }

        return dst;
    }
}