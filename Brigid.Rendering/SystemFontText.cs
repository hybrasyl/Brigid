#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Rasterizes short strings using the operating system's default typeface (via SkiaSharp), with no dependency on
///     Dark Ages font assets. Exists for the launcher screen, which must render before any <c>.dat</c> archive is
///     loaded — at which point the legacy/atlas path in <see cref="TextRenderer" /> (which reads
///     <c>DataContext.Fonts</c>) is not yet available. Not intended for in-world text.
/// </summary>
public static class SystemFontText
{
    /// <summary>
    ///     Renders <paramref name="text" /> at <paramref name="pixelSize" /> in <paramref name="color" /> to a tightly
    ///     sized, premultiplied-alpha texture. The colour is baked into the pixels, so draw the result with a white tint.
    ///     Requires <see cref="TextureConverter.Device" /> to be set.
    /// </summary>
    public static Texture2D Render(string text, float pixelSize, Color color)
    {
        var typeface = SKTypeface.Default;

        using var font = new SKFont(typeface, pixelSize)
        {
            Subpixel = true,
            Edging = SKFontEdging.Antialias
        };

        using var paint = new SKPaint
        {
            Color = new SKColor(color.R, color.G, color.B, color.A),
            IsAntialias = true
        };

        var width = Math.Max(1, (int)MathF.Ceiling(font.MeasureText(text, paint)));
        var metrics = font.Metrics;
        var height = Math.Max(1, (int)MathF.Ceiling(metrics.Descent - metrics.Ascent));
        var baseline = (int)MathF.Ceiling(-metrics.Ascent);

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawText(text, 0, baseline, SKTextAlign.Left, font, paint);

        using var image = surface.Snapshot();

        return TextureConverter.ToTexture2D(image);
    }

    /// <summary>
    ///     Returns the advance width (px) of <paramref name="text" /> at <paramref name="pixelSize" /> without rasterizing
    ///     a texture — for layout/measurement that should not allocate (e.g. text clipping loops).
    /// </summary>
    public static int Measure(string text, float pixelSize)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        using var font = new SKFont(SKTypeface.Default, pixelSize)
        {
            Subpixel = true,
            Edging = SKFontEdging.Antialias
        };

        return (int)MathF.Ceiling(font.MeasureText(text));
    }
}
