#region
using System.IO.Compression;
using Brigid.Data.AssetPacks;
using Brigid.Rendering;
using SkiaSharp;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The item-icon resolver that every item surface uses (BRIG-77). These tests use only the pack path: the legacy
///     path needs the game data. They share the registry collection, because the registry is static.
/// </summary>
[Collection("AssetPackRegistry")]
public sealed class ItemIconSourceTests : IDisposable
{
    private const int ITEM_FLAG = 0x8000;

    public ItemIconSourceTests() => AssetPackRegistry.ResetForTest();

    public void Dispose() => AssetPackRegistry.ResetForTest();

    private static byte[] Png(SKColor color, int size = 32)
    {
        using var bitmap = new SKBitmap(size, size);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }

    private static void RegisterItems(string packId, Dictionary<string, byte[]> entries, int[]? dyeable = null, int[]? noDye = null)
    {
        var stream = new MemoryStream();

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach ((var name, var bytes) in entries)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(bytes);
            }

        stream.Position = 0;

        Assert.True(AssetPackRegistry.RegisterForTest(
            new ZipArchive(stream, ZipArchiveMode.Read),
            new AssetPackManifest
            {
                SchemaVersion = 1,
                PackId = packId,
                ContentType = "item_icons",
                Covers = new Dictionary<string, AssetPackCoverageEntry> { ["item_icons"] = new() { Dyeable = dyeable, NoDye = noDye } }
            }));
    }

    private static SKColor ColorOf(ItemImage loaded)
    {
        using var image = loaded.Image;
        using var bitmap = SKBitmap.FromImage(image);

        return bitmap.GetPixel(0, 0);
    }

    [Theory]
    [InlineData(ITEM_FLAG | 20001, 20001)]
    [InlineData(ITEM_FLAG | 32767, 32767)]
    [InlineData(ITEM_FLAG | 5, 5)]
    [InlineData(5, 5)]
    [InlineData(20001, 20001)]
    public void ToSpriteId_RemovesTheWireFlag(int wire, int expected) => Assert.Equal(expected, ItemSpriteIds.ToSpriteId(wire));

    [Theory]
    [InlineData(32, -1)]
    [InlineData(31, 0)]
    [InlineData(30, 0)]
    [InlineData(24, 3)]
    [InlineData(16, 7)]
    [InlineData(33, -1)]
    [InlineData(48, -9)]
    public void SlotOffset_CentresTheIconInALegacySlot(int size, int expected) => Assert.Equal(expected, IconTexture.SlotOffset(size));

    [Fact]
    public void Load_FindsAModernItem_FromTheWireValue()
    {
        RegisterItems("modern", new() { ["item20001.png"] = Png(SKColors.Red) });

        var loaded = ItemIconSource.Load(ITEM_FLAG | 20001, 0);

        Assert.NotNull(loaded);
        Assert.True(loaded.Value.IsFromPack);
        Assert.Equal((0, 0), (loaded.Value.FrameLeft, loaded.Value.FrameTop));
        Assert.Equal(SKColors.Red, ColorOf(loaded.Value));
    }

    [Fact]
    public void Load_AsksTheNextPack_WhenTheFirstEntryDoesNotDecode()
    {
        RegisterItems("broken", new() { ["item20001.png"] = "not a png"u8.ToArray() });
        RegisterItems("good", new() { ["item20001.png"] = Png(SKColors.Blue) });

        var loaded = ItemIconSource.Load(20001, 0);

        Assert.NotNull(loaded);
        Assert.Equal(SKColors.Blue, ColorOf(loaded.Value));
    }

    [Fact]
    public void Load_AsksTheNextPack_WhenTheFirstHasNoEntry()
    {
        RegisterItems("other", new() { ["item20002.png"] = Png(SKColors.Red) });
        RegisterItems("good", new() { ["item20001.png"] = Png(SKColors.Blue) });

        Assert.Equal(SKColors.Blue, ColorOf(ItemIconSource.Load(20001, 0)!.Value));
    }

    [Fact]
    public void Load_TakesTheDyeDecision_FromThePackThatAnswered()
    {
        //the broken pack lists item 5 as dyeable. the good pack has ramp art but lists item 5 in no_dye, so the pack that
        //gives the image says no dye. a dye would need the game's dye table, which these tests do not load, so a
        //decision taken from the broken pack would throw
        var ramp = ItemDyeRamp.Colors[2];
        RegisterItems("broken", new() { ["item00005.png"] = "not a png"u8.ToArray() }, dyeable: [5]);
        RegisterItems("good", new() { ["item00005.png"] = Png(ramp) }, noDye: [5]);

        Assert.Equal(ramp, ColorOf(ItemIconSource.Load(5, 30)!.Value));
    }

    [Fact]
    public void Load_UsesThePackThatRegisteredFirst()
    {
        RegisterItems("first", new() { ["item20001.png"] = Png(SKColors.Red) });
        RegisterItems("second", new() { ["item20001.png"] = Png(SKColors.Blue) });

        Assert.Equal(SKColors.Red, ColorOf(ItemIconSource.Load(20001, 0)!.Value));
    }

    [Fact]
    public void Load_LeavesAModernItemUndyed_EvenWithRampArt()
    {
        RegisterItems("modern", new() { ["item20001.png"] = Png(new SKColor(0x8F, 0x5B, 0xA3)) }, dyeable: [20001]);

        var loaded = ItemIconSource.Load(20001, 5);

        Assert.NotNull(loaded);
        Assert.Equal(new SKColor(0x8F, 0x5B, 0xA3), ColorOf(loaded.Value));
    }

    [Fact]
    public void Load_GivesNothing_ForSpriteZero()
    {
        RegisterItems("modern", new() { ["item20001.png"] = Png(SKColors.Red) });

        Assert.Null(ItemIconSource.Load(0, 0));
        Assert.Null(ItemIconSource.Load(ITEM_FLAG, 0));
    }
}
