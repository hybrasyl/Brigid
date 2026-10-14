#region
using System.IO.Compression;
using System.Text.Json;
using Brigid.Data.AssetPacks;
using Brigid.Rendering;
using DALib.Drawing;
using SkiaSharp;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The item dye contract (BRIG-41): an item icon below sprite 20001 is dyeable when its art contains the canonical
///     ramp, <c>no_dye</c> lists the exceptions, and an explicit <c>dyeable</c> list is the exact set. The manifests here
///     have the shapes that Taliesin and the old scaffold write.
/// </summary>
public sealed class ItemPackDyeTests
{
    private static readonly SKColor Ramp = ItemDyeRamp.Colors[2];
    private static readonly SKColor Plain = new(0x40, 0x80, 0x40);

    private static SKImage Image(SKColor color, byte alpha = 255)
    {
        using var bitmap = new SKBitmap(4, 4);
        bitmap.Erase(Plain);
        bitmap.SetPixel(1, 1, color.WithAlpha(alpha));

        return SKImage.FromBitmap(bitmap);
    }

    private static SKColor PixelAt(SKImage image, int x, int y)
    {
        using var bitmap = SKBitmap.FromImage(image);

        return bitmap.GetPixel(x, y);
    }

    private static ItemPack Pack(string coversJson)
    {
        var manifest = JsonSerializer.Deserialize<AssetPackManifest>($$"""
                                                                     {
                                                                       "schema_version": 1,
                                                                       "pack_id": "items",
                                                                       "content_type": "item_icons",
                                                                       "covers": {{coversJson}}
                                                                     }
                                                                     """)!;
        var stream = new MemoryStream();

        using (new ZipArchive(stream, ZipArchiveMode.Create, true)) { }

        stream.Position = 0;

        return new ItemPack(new ZipArchive(stream, ZipArchiveMode.Read), manifest);
    }

    [Fact]
    public void TaliesinManifest_DetectsTheRamp_AndHonoursNoDye()
    {
        //Taliesin writes only the exceptions
        using var pack = Pack("""{ "item_icons": { "no_dye": [7] } }""");
        using var withRamp = Image(Ramp);
        using var withoutRamp = Image(Plain);

        Assert.True(pack.IsDyeable(5, withRamp));
        Assert.False(pack.IsDyeable(6, withoutRamp));
        Assert.False(pack.IsDyeable(7, withRamp));
    }

    [Fact]
    public void TaliesinManifest_WithNoExceptions_DetectsTheRamp()
    {
        using var pack = Pack("""{ "item_icons": {} }""");
        using var withRamp = Image(Ramp);

        Assert.True(pack.IsDyeable(5, withRamp));
    }

    [Fact]
    public void ScaffoldManifest_UsesTheDyeableListAsTheExactSet()
    {
        //the old scaffold (Scripts/sample-packs/items-test-001-002) writes an opt-in list; it wins over the art
        using var pack = Pack("""{ "item_icons": { "dyeable": [10, 11], "no_dye": [10] } }""");
        using var withRamp = Image(Ramp);
        using var withoutRamp = Image(Plain);

        Assert.True(pack.IsDyeable(10, withoutRamp));
        Assert.True(pack.IsDyeable(11, withRamp));
        Assert.False(pack.IsDyeable(12, withRamp));
    }

    [Fact]
    public void ModernIds_AreNeverDyeableByTheRampSwap()
    {
        using var detect = Pack("{}");
        using var listed = Pack("""{ "item_icons": { "dyeable": [20001] } }""");
        using var withRamp = Image(Ramp);

        Assert.True(detect.IsDyeable(ItemSpriteIds.MODERN_FIRST - 1, withRamp));
        Assert.False(detect.IsDyeable(ItemSpriteIds.MODERN_FIRST, withRamp));
        Assert.False(listed.IsDyeable(20001, withRamp));
    }

    [Fact]
    public void Detection_IgnoresATransparentRampPixel()
    {
        //an unpremultiplied bitmap keeps the RGB of an alpha-0 pixel, so the ramp color is there and only the alpha
        //test can refuse it (a premultiplied bitmap would store #00000000 and prove nothing)
        var info = new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bytes = new byte[info.BytesSize];
        bytes[0] = Ramp.Red;
        bytes[1] = Ramp.Green;
        bytes[2] = Ramp.Blue;
        bytes[3] = 0;

        using var bitmap = new SKBitmap(info);
        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        Assert.Equal(Ramp.WithAlpha(0), bitmap.GetPixel(0, 0));

        using var pack = Pack("{}");
        using var hidden = SKImage.FromBitmap(bitmap);

        Assert.False(pack.IsDyeable(5, hidden));
    }

    [Fact]
    public void Detection_KeepsTheFirstAnswerForAnId()
    {
        using var pack = Pack("{}");
        using var withRamp = Image(Ramp);
        using var withoutRamp = Image(Plain);

        Assert.True(pack.IsDyeable(5, withRamp));
        Assert.True(pack.IsDyeable(5, withoutRamp));
    }

    [Theory]
    [InlineData("""{ "item_icons": null }""")]
    [InlineData("null")]
    public void NullCovers_StillDetect(string coversJson)
    {
        using var pack = Pack(coversJson);
        using var withRamp = Image(Ramp);

        Assert.True(pack.IsDyeable(5, withRamp));
    }

    [Fact]
    public void AnEmptyDyeableList_MeansNothingIsDyeable()
    {
        using var pack = Pack("""{ "item_icons": { "dyeable": [] } }""");
        using var withRamp = Image(Ramp);

        Assert.False(pack.IsDyeable(5, withRamp));
    }

    [Fact]
    public void DyePass_ReplacesEachRampColorWithItsEntry_AndKeepsAlpha()
    {
        var replacement = new ColorTableEntry { Colors = [SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow, SKColors.Cyan, SKColors.Magenta] };

        using var bitmap = new SKBitmap(7, 1);
        bitmap.Erase(Plain);

        for (var i = 0; i < 6; i++)
            bitmap.SetPixel(i, 0, ItemDyeRamp.Colors[i]);

        using var source = SKImage.FromBitmap(bitmap);
        using var dyed = ItemDyePass.ApplyDyeFindReplace(source, replacement);

        for (var i = 0; i < 6; i++)
            Assert.Equal(replacement.Colors[i], PixelAt(dyed, i, 0));

        Assert.Equal(Plain, PixelAt(dyed, 6, 0));
    }

    [Fact]
    public void DyePass_ChangesExactlyTheImagesThatDetectionCallsDyeable()
    {
        var replacement = new ColorTableEntry { Colors = Enumerable.Repeat(SKColors.Red, 6).ToArray() };
        using var withRamp = Image(Ramp);
        using var withoutRamp = Image(Plain);
        using var dyedWith = ItemDyePass.ApplyDyeFindReplace(withRamp, replacement);
        using var dyedWithout = ItemDyePass.ApplyDyeFindReplace(withoutRamp, replacement);

        Assert.True(ItemDyeRamp.ContainsRampPixel(withRamp));
        Assert.NotEqual(PixelAt(withRamp, 1, 1), PixelAt(dyedWith, 1, 1));
        Assert.False(ItemDyeRamp.ContainsRampPixel(withoutRamp));
        Assert.Equal(PixelAt(withoutRamp, 1, 1), PixelAt(dyedWithout, 1, 1));
    }

    [Fact]
    public void RampCheck_GivesNoLine_WhenTheLegacyColoursAgree()
        => Assert.Empty(ItemDyeRamp.DifferencesFrom(ItemDyeRamp.Colors.ToArray()));

    [Fact]
    public void RampCheck_NamesTheIndexThatDiffers()
    {
        var legacy = ItemDyeRamp.Colors.ToArray();
        legacy[3] = new SKColor(1, 2, 3);

        var line = Assert.Single(ItemDyeRamp.DifferencesFrom(legacy));
        Assert.Contains("index 3", line);
        Assert.Contains("#010203", line);
    }

    [Fact]
    public void RampCheck_NamesALengthThatDiffers()
        => Assert.Contains("has 6 colors", Assert.Single(ItemDyeRamp.DifferencesFrom([SKColors.Red])));
}
