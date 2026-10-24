using Brigid.Data;
using DALib.Data;
using Xunit;

namespace Brigid.Tests;

// The server streams a map that is not in the cache one row in each 0x3C packet. Retail completes the map on the row with
// the last index and saves it, whatever arrived before, so a short stream saved a map of zero tiles (BRIG-113). The
// tracker counts a row only when it arrives with its full width, and the screen saves the map only when every row arrived.
public sealed class MapRowTrackerTests
{
    //one row of tiles: each tile is bg, lfg, rfg, each a big-endian u16, from a seed so that each row differs
    private static byte[] Row(int width, int seed)
    {
        var data = new byte[width * 6];

        for (var i = 0; i < data.Length; i += 2)
        {
            var value = (ushort)(seed * 100 + i);
            data[i] = (byte)(value >> 8);
            data[i + 1] = (byte)value;
        }

        return data;
    }

    [Fact]
    public void AFullStreamCompletesAndWritesEachTile()
    {
        var map = new MapFile(3, 2);
        var tracker = new MapRowTracker(map);

        Assert.Equal(MapRowResult.Accepted, tracker.Apply(0, Row(3, 1)));
        Assert.False(tracker.IsComplete);
        Assert.Equal(MapRowResult.Accepted, tracker.Apply(1, Row(3, 2)));

        Assert.True(tracker.IsComplete);
        Assert.Equal(2, tracker.ReceivedCount);
        //row 1, column 2: bytes 12 to 17 of the row, from the seed 2
        Assert.Equal(212, map.Tiles[2, 1].Background);
        Assert.Equal(214, map.Tiles[2, 1].LeftForeground);
        Assert.Equal(216, map.Tiles[2, 1].RightForeground);
    }

    [Fact]
    public void OnlyTheLastRowWithNoDataDoesNotComplete()
    {
        //the card's example: MapInfo 2x2, then only row 1 with no data
        var tracker = new MapRowTracker(new MapFile(2, 2));

        Assert.Equal(MapRowResult.Short, tracker.Apply(1, []));
        Assert.True(tracker.IsLastRow(1));
        Assert.False(tracker.IsComplete);
        Assert.Equal(0, tracker.ReceivedCount);
    }

    [Fact]
    public void AMissingRowDoesNotComplete()
    {
        var tracker = new MapRowTracker(new MapFile(2, 3));

        tracker.Apply(0, Row(2, 1));
        tracker.Apply(2, Row(2, 3));

        Assert.False(tracker.IsComplete);
        Assert.Equal(2, tracker.ReceivedCount);
    }

    [Fact]
    public void AShortRowWritesItsTilesButIsNotReceived()
    {
        var map = new MapFile(3, 1);
        map.Tiles[2, 0] = new MapTile();
        var tracker = new MapRowTracker(map);

        //two tiles and half of a third
        Assert.Equal(MapRowResult.Short, tracker.Apply(0, Row(3, 1).AsSpan(0, 15)));

        Assert.False(tracker.IsComplete);
        Assert.Equal(100, map.Tiles[0, 0].Background);
        Assert.Equal(0, map.Tiles[2, 0].Background);
    }

    [Fact]
    public void ADuplicateRowIsWrittenAgainAndCountedOnce()
    {
        var map = new MapFile(2, 2);
        var tracker = new MapRowTracker(map);

        tracker.Apply(0, Row(2, 1));

        Assert.Equal(MapRowResult.Duplicate, tracker.Apply(0, Row(2, 5)));
        Assert.Equal(1, tracker.ReceivedCount);
        Assert.Equal(500, map.Tiles[0, 0].Background);
        Assert.False(tracker.IsComplete);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(ushort.MaxValue)]
    public void AnOutOfRangeRowWritesNothing(int rowIndex)
    {
        var tracker = new MapRowTracker(new MapFile(2, 2));

        Assert.Equal(MapRowResult.OutOfRange, tracker.Apply(rowIndex, Row(2, 1)));
        Assert.Equal(0, tracker.ReceivedCount);
        Assert.False(tracker.IsLastRow(rowIndex));
    }

    [Fact]
    public void BytesPastTheWidthAreIgnored()
    {
        var map = new MapFile(2, 1);
        var tracker = new MapRowTracker(map);

        Assert.Equal(MapRowResult.Accepted, tracker.Apply(0, Row(3, 1)));
        Assert.True(tracker.IsComplete);
        Assert.Equal(106, map.Tiles[1, 0].Background);
    }

    //the row with the last index ends the stream, as on retail; the screen saves the map only for Complete
    [Fact]
    public void ReceiveEndsTheStreamOnTheLastRow()
    {
        var tracker = new MapRowTracker(new MapFile(2, 3));

        Assert.Equal(MapStreamStatus.Open, tracker.Receive(0, Row(2, 1)));
        Assert.Equal(MapStreamStatus.Open, tracker.Receive(1, Row(2, 2)));
        Assert.Equal(MapStreamStatus.Complete, tracker.Receive(2, Row(2, 3)));
    }

    [Fact]
    public void ReceiveGivesIncompleteForALastRowWithARowMissingOrShort()
    {
        var missing = new MapRowTracker(new MapFile(2, 2));
        Assert.Equal(MapStreamStatus.Incomplete, missing.Receive(1, Row(2, 2)));

        var shortLast = new MapRowTracker(new MapFile(2, 2));
        shortLast.Receive(0, Row(2, 1));
        Assert.Equal(MapStreamStatus.Incomplete, shortLast.Receive(1, []));
    }

    [Fact]
    public void AShortCopyOfAReceivedRowTakesTheRowBack()
    {
        var tracker = new MapRowTracker(new MapFile(2, 1));

        tracker.Apply(0, Row(2, 1));
        Assert.True(tracker.IsComplete);

        //the short copy writes over the first tile, so the row no longer holds the full copy
        Assert.Equal(MapRowResult.Short, tracker.Apply(0, Row(2, 5).AsSpan(0, 6)));
        Assert.False(tracker.IsComplete);
        Assert.Equal(0, tracker.ReceivedCount);

        Assert.Equal(MapRowResult.Accepted, tracker.Apply(0, Row(2, 1)));
        Assert.True(tracker.IsComplete);
    }

    [Fact]
    public void ProblemsCountsEachResultThatIsNotAccepted()
    {
        var tracker = new MapRowTracker(new MapFile(2, 2));
        Assert.Null(tracker.Problems);

        tracker.Apply(0, Row(2, 1));
        Assert.Null(tracker.Problems);

        tracker.Apply(0, Row(2, 1));
        tracker.Apply(1, []);
        tracker.Apply(1, []);
        tracker.Apply(7, Row(2, 1));

        Assert.Equal("2 short, 1 duplicate, 1 out of range", tracker.Problems);
    }
}
