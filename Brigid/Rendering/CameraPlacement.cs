#region
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Where the camera puts the player in the world area of a HUD. The classic HUD (616 × 308) was set by hand to put the
///     player tile 178 px below the top of its world area, and the large HUD (616 × 424) to 208 px. One rule gives both:
///     the tile moves 30 px for each 116 px of height, from the classic HUD. A new HUD between them (the main HUD, 347 px
///     tall) gets a place between them. Across, the tile is half a tile left of the centre, so its middle is on the centre.
/// </summary>
internal static class CameraPlacement
{
    private const int CLASSIC_HEIGHT = 308;
    private const int CLASSIC_TILE_TOP = 178;
    private const int LARGE_HEIGHT = 424;
    private const int LARGE_TILE_TOP = 208;
    private const int HALF_TILE_WIDTH = 28;

    /// <summary>The camera offset for a world area of the size of <paramref name="viewport" />.</summary>
    public static Vector2 OffsetFor(Rectangle viewport)
    {
        var tileTop = CLASSIC_TILE_TOP
                      + (int)Math.Round(
                          (viewport.Height - CLASSIC_HEIGHT) * (LARGE_TILE_TOP - CLASSIC_TILE_TOP) / (double)(LARGE_HEIGHT - CLASSIC_HEIGHT));

        //the camera puts the tile at the centre plus the offset; the centre can be on a half pixel
        return new Vector2(-HALF_TILE_WIDTH, tileTop - viewport.Height / 2f);
    }
}
