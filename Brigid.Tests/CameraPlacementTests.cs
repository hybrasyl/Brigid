#region
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-144: one rule places the player in the world area of each HUD. It gives the two offsets that the classic and
///     the large HUD were set to by hand, and the offset of the new HUD between them.
/// </summary>
public class CameraPlacementTests
{
    [Fact]
    public void TheClassicHudKeepsItsOffset()
        => Assert.Equal(new Vector2(-28, 24), CameraPlacement.OffsetFor(new Rectangle(0, 0, 616, 308)));

    [Fact]
    public void TheLargeHudKeepsItsOffset()
        => Assert.Equal(new Vector2(-28, -4), CameraPlacement.OffsetFor(new Rectangle(0, 0, 616, 424)));

    [Fact]
    public void TheMainHudPutsThePlayerTileOnAWholePixel()
    {
        var world = new Rectangle(4, 24, 632, 347);
        var offset = CameraPlacement.OffsetFor(world);

        //the player tile is 10 px lower than on the classic HUD: 39 px more height, at the classic-to-large rate
        Assert.Equal(-28, offset.X);
        Assert.Equal(188f, world.Height / 2f + offset.Y);
    }
}
