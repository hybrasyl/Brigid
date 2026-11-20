using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     BRIG-184: the test driver clicks a tile at <see cref="Camera.TileCentreToScreen" />. The world screen reads a click
///     as a tile through <see cref="Camera.ScreenToWorld" /> and <see cref="Camera.WorldToTile" />, so the point must give
///     back the same tile at each camera place, zoom and offset, also after the floor to a whole pixel.
/// </summary>
public sealed class CameraTileCentreTests
{
    public static TheoryData<float, float, float, float, float> Cameras
        => new()
        {
            { 0, 0, 1, 0, 0 },
            { 812, 410, 1, 0, 0 },
            { 812.5f, 410.25f, 1, 0, -30 },
            { 300, 900, 2, 12, 7 },
            { 1500, 60, 0.5f, -20, 0 }
        };

    [Theory]
    [MemberData(nameof(Cameras))]
    public void TheCentreOfATileReadsBackAsThatTile(float positionX, float positionY, float zoom, float offsetX, float offsetY)
    {
        const int MAP_HEIGHT = 40;
        var camera = new Camera(612, 324)
        {
            Position = new Vector2(positionX, positionY),
            Zoom = zoom,
            Offset = new Vector2(offsetX, offsetY)
        };

        for (var x = 0; x < 40; x += 3)
            for (var y = 0; y < MAP_HEIGHT; y += 3)
            {
                var point = camera.TileCentreToScreen(x, y, MAP_HEIGHT);
                var pixel = new Vector2(MathF.Floor(point.X), MathF.Floor(point.Y));
                var world = camera.ScreenToWorld(pixel);

                Assert.Equal(new Point(x, y), Camera.WorldToTile(world.X, world.Y, MAP_HEIGHT));
            }
    }
}
