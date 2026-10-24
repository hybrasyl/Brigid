using Brigid.Data;
using Brigid.Models;
using DALib.Data;
using DALib.Networking.Wire;
using Xunit;

namespace Brigid.Tests;

// The server sends a map row with each tile value big-endian, and the cache file holds each value little-endian. The
// server's MapInfo gives the CRC-CCITT of the file, so the client saves a map only when its bytes match that checksum
// (BRIG-113), and loads a cached map only when its size and checksum match.
public sealed class MapCacheTests : IDisposable
{
    private readonly string Root = Path.Combine(Path.GetTempPath(), "brigid-maps-" + Guid.NewGuid().ToString("N"));

    public MapCacheTests() => Directory.CreateDirectory(Root);

    public void Dispose() => Directory.Delete(Root, true);

    //a 1x2 map from two wire rows with known bytes
    private static MapFile StreamedMap()
    {
        var map = new MapFile(1, 2);
        var tracker = new MapRowTracker(map);
        tracker.Apply(0, [0x01, 0x02, 0x03, 0x04, 0x05, 0x06]);
        tracker.Apply(1, [0x11, 0x12, 0x13, 0x14, 0x15, 0x16]);

        return map;
    }

    private static readonly byte[] CacheBytes = [0x02, 0x01, 0x04, 0x03, 0x06, 0x05, 0x12, 0x11, 0x14, 0x13, 0x16, 0x15];

    [Fact]
    public void ToCacheBytesWritesEachWireValueLittleEndian()
    {
        var map = StreamedMap();

        Assert.Equal(CacheBytes, MapCache.ToCacheBytes(map, CrcCcitt.Compute(CacheBytes)));
    }

    [Fact]
    public void ToCacheBytesGivesNullWhenTheChecksumDoesNotMatch()
    {
        var map = StreamedMap();

        Assert.Null(MapCache.ToCacheBytes(map, (ushort)(CrcCcitt.Compute(CacheBytes) ^ 1)));
    }

    [Fact]
    public void ASavedMapLoadsWithTheSameTiles()
    {
        var checksum = CrcCcitt.Compute(CacheBytes);
        var bytes = MapCache.ToCacheBytes(StreamedMap(), checksum);

        Assert.NotNull(bytes);
        Assert.True(MapCache.TrySave(Root, 7, bytes));

        var loaded = MapCache.TryLoad(Root, 7, 1, 2, checksum);

        Assert.NotNull(loaded);
        Assert.Equal(0x0102, loaded.Tiles[0, 0].Background);
        Assert.Equal(0x0304, loaded.Tiles[0, 0].LeftForeground);
        Assert.Equal(0x0506, loaded.Tiles[0, 0].RightForeground);
        Assert.Equal(0x1516, loaded.Tiles[0, 1].RightForeground);
    }

    [Fact]
    public void TryLoadRefusesAnAbsentFileAWrongSizeAndAWrongChecksum()
    {
        var checksum = CrcCcitt.Compute(CacheBytes);
        File.WriteAllBytes(Path.Combine(Root, "lod7.map"), CacheBytes);

        Assert.Null(MapCache.TryLoad(Root, 8, 1, 2, checksum));
        Assert.Null(MapCache.TryLoad(Root, 7, 2, 2, checksum));
        Assert.Null(MapCache.TryLoad(Root, 7, 1, 2, (ushort)(checksum ^ 1)));
        Assert.NotNull(MapCache.TryLoad(Root, 7, 1, 2, checksum));
    }
}
