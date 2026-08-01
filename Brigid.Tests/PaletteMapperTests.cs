#region
using Brigid.Utilities;
using DALib.Drawing;
using DALib.Extensions;
using SkiaSharp;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     Screenshots crashed the client with "Color not found in palette": DALib's <c>GetPalettizedPixelData</c> demands
///     every pixel be an exact palette member, and <c>ImageProcessor.Quantize</c> does not guarantee its own output
///     meets that — above the colour limit its pixels and its palette come from different sources.
///     <see cref="PaletteMapper" /> takes the nearest entry instead of throwing.
/// </summary>
public class PaletteMapperTests
{
    private static SKImage ImageOf(params SKColor[] pixels)
    {
        var info = new SKImageInfo(pixels.Length, 1, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var bitmap = new SKBitmap(info);
        bitmap.Pixels = pixels;

        return SKImage.FromBitmap(bitmap);
    }

    private static Palette PaletteOf(params SKColor[] colors) => new(colors);

    [Fact]
    public void OffPaletteColour_TakesTheNearestEntry_InsteadOfThrowing()
    {
        var red = new SKColor(255, 0, 0);
        var blue = new SKColor(0, 0, 255);

        //nowhere in the palette, and much closer to red than to blue
        var offPalette = new SKColor(250, 4, 4);

        using var image = ImageOf(offPalette);
        var palette = PaletteOf(red, blue);

        //the failure being fixed: DALib rejects this pair outright
        Assert.Throws<InvalidOperationException>(() => image.GetPalettizedPixelData(palette));

        var indices = PaletteMapper.MapToIndices(image, palette);

        Assert.Equal(0, Assert.Single(indices));
    }

    [Fact]
    public void ExactMatches_RoundTripUnchanged()
    {
        var a = new SKColor(10, 20, 30);
        var b = new SKColor(200, 100, 50);
        var c = new SKColor(0, 0, 0);

        using var image = ImageOf(b, c, a, b);
        var palette = PaletteOf(a, b, c);

        //a well-formed pair must map identically to DALib, or saved screenshots would shift colour
        Assert.Equal(image.GetPalettizedPixelData(palette), PaletteMapper.MapToIndices(image, palette));
    }

    [Fact]
    public void DuplicateEntries_ResolveToTheFirst_MatchingDALib()
    {
        var grey = new SKColor(128, 128, 128);

        using var image = ImageOf(grey);
        var palette = PaletteOf(new SKColor(1, 1, 1), grey, grey);

        Assert.Equal(image.GetPalettizedPixelData(palette), PaletteMapper.MapToIndices(image, palette));
        Assert.Equal(1, PaletteMapper.MapToIndices(image, palette)[0]);
    }

    [Fact]
    public void EveryPixelResolves_ForAFullyOffPaletteImage()
    {
        //the screenshot case in miniature: nothing matches, and it still has to produce a complete index map
        var pixels = new SKColor[64];

        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = new SKColor((byte)(i * 3), (byte)(255 - i), (byte)(i | 7));

        using var image = ImageOf(pixels);
        var palette = PaletteOf(new SKColor(0, 0, 0), new SKColor(255, 255, 255));

        var indices = PaletteMapper.MapToIndices(image, palette);

        Assert.Equal(pixels.Length, indices.Length);
        Assert.All(indices, i => Assert.True(i < 2, $"index {i} is outside the 2-entry palette"));
    }
}
