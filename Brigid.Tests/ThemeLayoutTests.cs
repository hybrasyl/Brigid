#region
using Brigid.Data.AssetPacks;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using SkiaSharp;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The geometry that <see cref="ThemeArt" /> composites with: logical rectangles snapped to native pixels, the nine
///     pieces of a nine-slice part, the cover and contain fits, and the halving steps that filter a large image down.
/// </summary>
public class ThemeLayoutTests
{
    [Fact]
    public void ToNative_RoundsEachEdge()
        => Assert.Equal(new Rectangle(15, 30, 15, 15), ThemeLayout.ToNative(new Rectangle(10, 20, 10, 10), 1.5f, 1.5f));

    [Fact]
    public void ToNative_KeepsASharedEdgeShared()
    {
        //at 1.5x, x = 11 lands on 16.5; both rectangles must round it the same way
        var left = ThemeLayout.ToNative(new Rectangle(0, 0, 11, 10), 1.5f, 1.5f);
        var right = ThemeLayout.ToNative(new Rectangle(11, 0, 10, 10), 1.5f, 1.5f);

        Assert.Equal(left.Right, right.Left);
    }

    [Fact]
    public void NineSlice_SplitsIntoNinePieces()
    {
        var patches = ThemeLayout.NineSlice(100, 60, new SKRectI(10, 20, 30, 5), 400, 200, new SKRectI(20, 40, 60, 10));

        Assert.Equal(9, patches.Count);
        Assert.Equal(4, patches.Count(p => p.IsCorner));

        //top left corner
        Assert.Equal(new SKRect(0, 0, 10, 20), patches[0].Source);
        Assert.Equal(new SKRect(0, 0, 20, 40), patches[0].Destination);

        //centre
        Assert.Equal(new SKRect(10, 20, 70, 55), patches[4].Source);
        Assert.Equal(new SKRect(20, 40, 340, 190), patches[4].Destination);

        //bottom right corner
        Assert.Equal(new SKRect(70, 55, 100, 60), patches[8].Source);
        Assert.Equal(new SKRect(340, 190, 400, 200), patches[8].Destination);
    }

    [Fact]
    public void NineSlice_ShrinksTheInsetsWhenTheOutputIsSmallerThanThem()
    {
        //40 + 60 = 100 px of inset in a 50 px wide output: both shrink to 20 + 30, and the centre column goes away
        var patches = ThemeLayout.NineSlice(100, 100, new SKRectI(40, 0, 60, 0), 50, 10, new SKRectI(40, 0, 60, 0));

        Assert.Equal(2, patches.Count);
        Assert.Equal(new SKRect(0, 0, 20, 10), patches[0].Destination);
        Assert.Equal(new SKRect(20, 0, 50, 10), patches[1].Destination);
        Assert.DoesNotContain(patches, p => p.Destination.Width < 0);
    }

    [Fact]
    public void NineSlice_ClampsInsetsTooLargeToAdd()
    {
        //the two insets overflow an int when added; the clamp must still split the width between them
        var patches = ThemeLayout.NineSlice(10, 10, SKRectI.Empty, 100, 10, new SKRectI(int.MaxValue, 0, int.MaxValue, 0));

        Assert.All(patches, p => Assert.InRange(p.Destination.Left, 0, 100));
        Assert.All(patches, p => Assert.InRange(p.Destination.Right, 0, 100));
    }

    [Fact]
    public void NineSlice_WithNoInsetsIsOnlyTheCentre()
    {
        var patch = Assert.Single(ThemeLayout.NineSlice(32, 32, SKRectI.Empty, 100, 50, SKRectI.Empty));

        Assert.Equal(new SKRect(0, 0, 32, 32), patch.Source);
        Assert.Equal(new SKRect(0, 0, 100, 50), patch.Destination);
        Assert.False(patch.IsCorner);
    }

    [Fact]
    public void ScaleInsets_RoundsToWholePixels()
        => Assert.Equal(new SKRectI(24, 25, 0, 1), ThemeLayout.ScaleInsets(new SKRectI(96, 100, 1, 3), 0.25f, 0.25f));

    [Fact]
    public void CoverSource_CropsTheSidesOfAWideImage()
        //16:9 into 4:3: keep the full height, crop the width to 4:3 and centre it
        => Assert.Equal(new SKRect(480, 0, 3360, 2160), ThemeLayout.CoverSource(3840, 2160, 1280, 960));

    [Fact]
    public void CoverSource_CropsTheTopAndBottomOfATallImage()
        => Assert.Equal(new SKRect(0, 250, 1000, 750), ThemeLayout.CoverSource(1000, 1000, 1920, 960));

    [Fact]
    public void ContainDestination_CentresTheWholeImage()
        => Assert.Equal(new SKRect(0, 120, 1280, 840), ThemeLayout.ContainDestination(3840, 2160, 1280, 960));

    [Theory]
    //3840 at 0.1: 1920 (0.2), 960 (0.4), 480 (0.8) — stops at the first size with a scale of 0.5 or more
    [InlineData(3840, 2160, 0.1f, 0.1f, 480, 270)]
    //no step when the scale is already 0.5 or more
    [InlineData(100, 100, 0.5f, 2f, 100, 100)]
    //each axis on its own
    [InlineData(1000, 100, 0.2f, 1f, 250, 100)]
    //an odd size rounds up, so no source column is dropped
    [InlineData(101, 101, 0.3f, 0.3f, 51, 51)]
    public void ReducedSize_HalvesUntilTheScaleIsAtLeastHalf(int width, int height, float scaleX, float scaleY, int expectedWidth, int expectedHeight)
        => Assert.Equal((expectedWidth, expectedHeight), ThemeArt.ReducedSize(width, height, scaleX, scaleY));

    [Fact]
    public void Reduce_AveragesBlocksInsteadOfSkippingPixels()
    {
        //a 1 px black and white checkerboard; sampling every other pixel would give one solid colour,
        //a filtered reduction gives mid grey. At 0.25 the steps stop at half size, where the remaining scale is 0.5
        //and the final linear draw can average the rest.
        using var bitmap = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul));

        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
                bitmap.SetPixel(x, y, ((x + y) % 2) == 0 ? SKColors.White : SKColors.Black);

        using var image = SKImage.FromBitmap(bitmap);
        using var reduced = ThemeArt.Reduce(image, 0.25f, 0.25f, out var scaleX, out var scaleY);

        Assert.NotNull(reduced);
        Assert.Equal(4, reduced.Width);
        Assert.Equal(0.5f, scaleX);
        Assert.Equal(0.5f, scaleY);

        using var pixels = SKBitmap.FromImage(reduced);
        var red = pixels.GetPixel(1, 1).Red;
        Assert.InRange(red, 100, 155);
    }

    [Fact]
    public void Reduce_ReturnsNullWhenNoStepIsNecessary()
    {
        using var bitmap = new SKBitmap(4, 4);
        using var image = SKImage.FromBitmap(bitmap);

        Assert.Null(ThemeArt.Reduce(image, 0.75f, 1f, out var scaleX, out var scaleY));
        Assert.Equal(1f, scaleX);
        Assert.Equal(1f, scaleY);
    }

    [Theory]
    [InlineData(1f)]
    //4x art at 1x output: the image is reduced first, and each corner is still its own colour
    [InlineData(0.25f)]
    public void Composite_KeepsEachCornerAndStretchesTheCentre(float artScale)
    {
        using var source = Quadrants(30, 10);
        var part = new UiThemePart("frame.png", new SKRectI(10, 10, 10, 10), UiThemeFit.Stretch, false);

        using var composite = ThemeArt.Composite(part, source, 100, 60, artScale, artScale);
        using var pixels = SKBitmap.FromImage(composite);

        Assert.Equal(100, pixels.Width);
        Assert.Equal(60, pixels.Height);
        Assert.Equal(SKColors.Red, pixels.GetPixel(0, 0));
        Assert.Equal(SKColors.Lime, pixels.GetPixel(99, 0));
        Assert.Equal(SKColors.Blue, pixels.GetPixel(0, 59));
        Assert.Equal(SKColors.Yellow, pixels.GetPixel(99, 59));
        Assert.Equal(SKColors.Gray, pixels.GetPixel(50, 30));
    }

    [Fact]
    public void Composite_AtArtScaleKeepsTheCornerSize()
    {
        using var source = Quadrants(30, 10);
        var part = new UiThemePart("frame.png", new SKRectI(10, 10, 10, 10), UiThemeFit.Stretch, false);

        using var composite = ThemeArt.Composite(part, source, 100, 60, 1f, 1f);
        using var pixels = SKBitmap.FromImage(composite);

        //the corner is 10 px: the last corner pixel is red and the next one is the grey edge
        Assert.Equal(SKColors.Red, pixels.GetPixel(9, 9));
        Assert.Equal(SKColors.Gray, pixels.GetPixel(10, 10));
        Assert.Equal(SKColors.Yellow, pixels.GetPixel(90, 50));
        Assert.Equal(SKColors.Gray, pixels.GetPixel(89, 49));
    }

    [Fact]
    public void Composite_TilesTheCentreInsteadOfStretchingIt()
    {
        //a 30 px frame with 10 px insets; the 10 px centre is black on its left half and white on its right half
        using var bitmap = new SKBitmap(new SKImageInfo(30, 30, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Gray);

        for (var y = 10; y < 20; y++)
            for (var x = 10; x < 20; x++)
                bitmap.SetPixel(x, y, x < 15 ? SKColors.Black : SKColors.White);

        using var image = SKImage.FromBitmap(bitmap);
        var part = new UiThemePart("frame.png", new SKRectI(10, 10, 10, 10), UiThemeFit.Stretch, true);

        using var composite = ThemeArt.Composite(part, image, 60, 30, 1f, 1f);
        using var pixels = SKBitmap.FromImage(composite);

        //the 40 px centre repeats the 10 px pattern four times: black, white, black, white ...
        Assert.Equal(SKColors.Black, pixels.GetPixel(12, 15));
        Assert.Equal(SKColors.White, pixels.GetPixel(17, 15));
        Assert.Equal(SKColors.Black, pixels.GetPixel(22, 15));
        Assert.Equal(SKColors.White, pixels.GetPixel(47, 15));
    }

    [Fact]
    public void Composite_UsesEachAxisScaleForItsInsets()
    {
        //a maximized window that is not 4:3 scales the axes unequally: 2x across and 1x down
        using var source = Quadrants(30, 10);
        var part = new UiThemePart("frame.png", new SKRectI(10, 10, 10, 10), UiThemeFit.Stretch, false);

        using var composite = ThemeArt.Composite(part, source, 100, 60, 2f, 1f);
        using var pixels = SKBitmap.FromImage(composite);

        //the corner is 20 px wide and 10 px high
        Assert.Equal(SKColors.Red, pixels.GetPixel(19, 9));
        Assert.Equal(SKColors.Gray, pixels.GetPixel(20, 5));
        Assert.Equal(SKColors.Gray, pixels.GetPixel(5, 10));
    }

    [Fact]
    public void Composite_NearestKeepsHardPixelEdgesWhenEnlarged()
    {
        //a 2x2 image enlarged to 8x8: nearest makes four solid 4x4 blocks, linear would blend the middle columns
        using var bitmap = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.SetPixel(0, 0, SKColors.Red);
        bitmap.SetPixel(1, 0, SKColors.Blue);
        bitmap.SetPixel(0, 1, SKColors.Blue);
        bitmap.SetPixel(1, 1, SKColors.Red);

        using var image = SKImage.FromBitmap(bitmap);
        var part = new UiThemePart("pixel.png", null, UiThemeFit.Stretch, false);

        using var nearest = ThemeArt.Composite(part, image, 8, 8, 4f, 4f, UiThemeFilter.Nearest);
        using var nearestPixels = SKBitmap.FromImage(nearest);

        Assert.Equal(SKColors.Red, nearestPixels.GetPixel(3, 0));
        Assert.Equal(SKColors.Blue, nearestPixels.GetPixel(4, 0));

        using var linear = ThemeArt.Composite(part, image, 8, 8, 4f, 4f);
        using var linearPixels = SKBitmap.FromImage(linear);

        Assert.NotEqual(SKColors.Red, linearPixels.GetPixel(3, 0));
    }

    [Fact]
    public void Composite_CoversWithoutStretching()
    {
        //a 2:1 image, left half red and right half blue, covering a square: the middle half of the width shows
        using var bitmap = new SKBitmap(new SKImageInfo(40, 20, SKColorType.Rgba8888, SKAlphaType.Premul));

        for (var y = 0; y < 20; y++)
            for (var x = 0; x < 40; x++)
                bitmap.SetPixel(x, y, x < 20 ? SKColors.Red : SKColors.Blue);

        using var image = SKImage.FromBitmap(bitmap);
        var part = new UiThemePart("bg.png", null, UiThemeFit.Cover, false);

        using var composite = ThemeArt.Composite(part, image, 40, 40, 1f, 1f);
        using var pixels = SKBitmap.FromImage(composite);

        Assert.Equal(SKColors.Red, pixels.GetPixel(2, 20));
        Assert.Equal(SKColors.Blue, pixels.GetPixel(37, 20));
        Assert.Equal(SKColors.Red, pixels.GetPixel(2, 0));
        Assert.Equal(SKColors.Red, pixels.GetPixel(2, 39));
    }

    //a square image with a solid corner block of each colour and a grey cross between them
    private static SKImage Quadrants(int size, int corner)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));

        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var left = x < corner;
                var right = x >= size - corner;
                var top = y < corner;
                var bottom = y >= size - corner;

                bitmap.SetPixel(
                    x,
                    y,
                    (left && top) ? SKColors.Red
                    : (right && top) ? SKColors.Lime
                    : (left && bottom) ? SKColors.Blue
                    : (right && bottom) ? SKColors.Yellow
                    : SKColors.Gray);
            }

        return SKImage.FromBitmap(bitmap);
    }

    [Theory]
    //the icy-steel frame: a 44 art px slice at density 4
    [InlineData(44, 44, 44, 44, 4f, 11)]
    //the widest side counts, and a part pixel rounds up
    [InlineData(10, 24, 10, 24, 4f, 6)]
    [InlineData(9, 9, 9, 9, 4f, 3)]
    [InlineData(0, 0, 0, 0, 2f, 0)]
    public void ASliceBorderIsItsWidestInsetInLogicalPixels(int left, int top, int right, int bottom, float density, int expected)
        => Assert.Equal(expected, ThemeLayout.SliceBorder(new SKRectI(left, top, right, bottom), density));

    [Theory]
    //the flat 1 px frame keeps the panel's own padding
    [InlineData(6, 1, 6)]
    [InlineData(8, 1, 8)]
    //the icy-steel border is 11 px, so the content sits 14 px in
    [InlineData(6, 11, 14)]
    [InlineData(10, 11, 14)]
    [InlineData(20, 11, 20)]
    public void ContentKeepsClearOfAWideFrameBorder(int padding, int border, int expected)
        => Assert.Equal(expected, Controls.Generic.FramePanel.ComputeInset(padding, border));
}
