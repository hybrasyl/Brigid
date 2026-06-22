using Chaos.Client.Definitions;
using Xunit;

namespace Chaos.Client.Tests;

public sealed class DoorProximityDedupTests
{
    //payload type is opaque to the algorithm — use a tag string to identify which hit "survived" dedup.
    private static DoorProximityDedup.DoorHit<string> Hit(int distSq, int tileX, int tileY, string tag)
        => new(distSq, tileX, tileY, tag);

    [Fact]
    public void SingleTile_KeepsOne()
    {
        var input = new[]
        {
            Hit(10, 5, 5, "a")
        };

        var output = DoorProximityDedup.CollapseAdjacent(input);

        Assert.Single(output);
        Assert.Equal("a", output[0].Payload);
    }

    [Fact]
    public void TwoTilesEastWest_CollapsesToNearest()
    {
        //cursor is closer to (3,5) than (4,5) — closer tile wins.
        var input = new[]
        {
            Hit(10, 3, 5, "closer"),
            Hit(40, 4, 5, "farther")
        };

        var output = DoorProximityDedup.CollapseAdjacent(input);

        Assert.Single(output);
        Assert.Equal("closer", output[0].Payload);
    }

    [Fact]
    public void TwoTilesNorthSouth_CollapsesToNearest()
    {
        var input = new[]
        {
            Hit(10, 5, 3, "closer"),
            Hit(40, 5, 4, "farther")
        };

        var output = DoorProximityDedup.CollapseAdjacent(input);

        Assert.Single(output);
        Assert.Equal("closer", output[0].Payload);
    }

    [Fact]
    public void ThreeTileDoor_CollapsesToNearest()
    {
        //E/W 3-tile door: (3,5)-(4,5)-(5,5). Cursor sits near (4,5) center.
        //Distances are sorted ascending per the method contract.
        var input = new[]
        {
            Hit(4,  4, 5, "middle"),
            Hit(16, 3, 5, "left"),
            Hit(16, 5, 5, "right")
        };

        var output = DoorProximityDedup.CollapseAdjacent(input);

        Assert.Single(output);
        Assert.Equal("middle", output[0].Payload);
    }

    [Fact]
    public void FourTileDoor_CollapsesToNearest()
    {
        //E/W 4-tile door: (3,5)-(4,5)-(5,5)-(6,5). Cursor near (4,5).
        var input = new[]
        {
            Hit(4,  4, 5, "near1"),
            Hit(16, 5, 5, "near2"),
            Hit(36, 3, 5, "far1"),
            Hit(64, 6, 5, "far2")
        };

        var output = DoorProximityDedup.CollapseAdjacent(input);

        Assert.Single(output);
        Assert.Equal("near1", output[0].Payload);
    }

    [Fact]
    public void TwoDistinctDoors_PreservesBoth()
    {
        //two single-tile doors separated by a gap along Y — not 4-adjacent, should remain separate.
        var input = new[]
        {
            Hit(10, 3, 5, "doorA"),
            Hit(20, 3, 8, "doorB")
        };

        var output = DoorProximityDedup.CollapseAdjacent(input);

        Assert.Equal(2, output.Count);
        Assert.Equal("doorA", output[0].Payload);
        Assert.Equal("doorB", output[1].Payload);
    }

    [Fact]
    public void DiagonalTiles_NotCollapsed()
    {
        //(3,5) and (4,6): Manhattan distance = 2, so the 4-neighbor rule treats them as separate doors.
        //this is deliberate — all catalogued doors are axis-aligned strips; diagonal tile pairs are
        //always two different doors (e.g. adjacent rooms' door frames near a corner).
        var input = new[]
        {
            Hit(10, 3, 5, "a"),
            Hit(12, 4, 6, "b")
        };

        var output = DoorProximityDedup.CollapseAdjacent(input);

        Assert.Equal(2, output.Count);
        Assert.Equal("a", output[0].Payload);
        Assert.Equal("b", output[1].Payload);
    }

    [Fact]
    public void DistanceSortPreserved()
    {
        //mix of distinct doors — output sort order should mirror input sort order (ascending DistanceSq).
        var input = new[]
        {
            Hit(5,  1, 1, "a"),
            Hit(10, 1, 5, "b"),
            Hit(15, 1, 10, "c")
        };

        var output = DoorProximityDedup.CollapseAdjacent(input);

        Assert.Equal(3, output.Count);
        Assert.Equal(5,  output[0].DistanceSq);
        Assert.Equal(10, output[1].DistanceSq);
        Assert.Equal(15, output[2].DistanceSq);
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        var output = DoorProximityDedup.CollapseAdjacent(Array.Empty<DoorProximityDedup.DoorHit<string>>());

        Assert.Empty(output);
    }

    [Fact]
    public void FiveContiguousTiles_CollapseToOne()
    {
        //defensive: no catalogued door is 5 tiles, but the algorithm should still handle arbitrary
        //cluster sizes without duplicating entries. Transitive adjacency (tile N is only adjacent to
        //N-1) must fold the whole strip into one cluster.
        var input = new[]
        {
            Hit(4,  5, 5, "keep"),
            Hit(16, 4, 5, "merge1"),
            Hit(16, 6, 5, "merge2"),
            Hit(36, 3, 5, "merge3"),
            Hit(36, 7, 5, "merge4")
        };

        var output = DoorProximityDedup.CollapseAdjacent(input);

        Assert.Single(output);
        Assert.Equal("keep", output[0].Payload);
    }
}
