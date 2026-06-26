#region
using System.Buffers;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Brigid.Rendering;

public static class TextureConverter
{
    //Warm gold 50/50 blend used by CreateGroupTintedTexture for group-member highlighting.
    private static readonly Color GroupTint = new(255, 231, 59);
    private static readonly Color HitTint = new(255, 0, 0);

    public static GraphicsDevice Device { get; set; } = null!;

    internal static T ConvertImage<T>(SKImage image, Func<GraphicsDevice, int, int, T> factory) where T: Texture2D
    {
        var width = image.Width;
        var height = image.Height;

        var info = new SKImageInfo(
            width,
            height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        var byteCount = info.BytesSize;
        var pixels = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);

            try
            {
                image.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes);
            } finally
            {
                handle.Free();
            }

            var texture = factory(Device, width, height);
            texture.SetData(pixels, 0, byteCount);

            return texture;
        } finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    /// <summary>
    ///     Creates a group-tinted copy of a texture using a warm yellow/gold (255, 231, 59) 50/50 blend. Matches the
    ///     original DA client's group member highlight.
    /// </summary>
    public static Texture2D CreateGroupTintedTexture(Texture2D source)
    {
        var count = source.Width * source.Height;
        var pixels = ArrayPool<Color>.Shared.Rent(count);

        try
        {
            source.GetData(pixels, 0, count);
            Blend50Pixels(pixels, count, GroupTint);

            var tinted = new Texture2D(Device, source.Width, source.Height);
            tinted.SetData(pixels, 0, count);

            return tinted;
        } finally
        {
            ArrayPool<Color>.Shared.Return(pixels);
        }
    }

    /// <summary>
    ///     Creates a hit-tinted copy of a texture using a red 50/50 blend for the projectile impact flash.
    /// </summary>
    public static Texture2D CreateHitTintedTexture(Texture2D source)
    {
        var count = source.Width * source.Height;
        var pixels = ArrayPool<Color>.Shared.Rent(count);

        try
        {
            source.GetData(pixels, 0, count);
            Blend50Pixels(pixels, count, HitTint);

            var tinted = new Texture2D(Device, source.Width, source.Height);
            tinted.SetData(pixels, 0, count);

            return tinted;
        } finally
        {
            ArrayPool<Color>.Shared.Return(pixels);
        }
    }

    /// <summary>
    ///     Creates a tinted (blue-shifted) copy of a texture. Used for entity hover highlights.
    /// </summary>
    public static Texture2D CreateTintedTexture(Texture2D source)
    {
        var count = source.Width * source.Height;
        var pixels = ArrayPool<Color>.Shared.Rent(count);

        try
        {
            source.GetData(pixels, 0, count);
            TintPixels(pixels, count);

            var tinted = new Texture2D(Device, source.Width, source.Height);
            tinted.SetData(pixels, 0, count);

            return tinted;
        } finally
        {
            ArrayPool<Color>.Shared.Return(pixels);
        }
    }

    /// <summary>
    ///     Applies a 50/50 per-channel additive blend with <paramref name="tint" /> in-place. Alpha is preserved and
    ///     transparent pixels are skipped. Matches the retail DA client's generic tint primitive (see
    ///     <c>Darkages.exe</c> <c>FUN_004548b0</c> for the original RGB565 implementation).
    /// </summary>
    internal static void Blend50Pixels(Color[] pixels, int count, Color tint)
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
    ///     Converts each pixel to Rec. 601 luminance and multiplies by <paramref name="tint" />, producing a duotone
    ///     result where the tint color replaces the hue while original luminance preserves shape and detail. Bright
    ///     pixels become full-saturation tint; dark pixels become dark tint; alpha is preserved. Stronger and more
    ///     visually identifiable than <see cref="Blend50Pixels" /> for state overlays like learnable/locked ability
    ///     icons, which otherwise retain too much of the source art's color.
    /// </summary>
    internal static void LuminanceTintPixels(Color[] pixels, int count, Color tint)
    {
        //Rec. 601 luminance weights × 1000 for integer-only math (byte-accurate result without float conversion).
        for (var i = 0; i < count; i++)
        {
            var p = pixels[i];

            if (p.A == 0)
                continue;

            var lumen = ((299 * p.R) + (587 * p.G) + (114 * p.B)) / 1000;

            pixels[i] = new Color(
                (byte)((lumen * tint.R) / 255),
                (byte)((lumen * tint.G) / 255),
                (byte)((lumen * tint.B) / 255),
                p.A);
        }
    }

    /// <summary>
    ///     Applies a blue-shift tint to a pixel array in-place. Used for entity hover highlights.
    /// </summary>
    internal static void TintPixels(Color[] pixels, int count)
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

    public static Texture2D ToTexture2D(SKImage image)
        => ConvertImage(
            image,
            static (d, w, h) => new Texture2D(
                d,
                w,
                h,
                false,
                SurfaceFormat.Color));
}
