#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Isometric camera that converts between world pixel coordinates, screen coordinates, and tile coordinates. Supports
///     viewport clipping, zoom, and a configurable center offset.
/// </summary>
public sealed class Camera
{
    private const int HALF_TILE_WIDTH = 28;
    private const int HALF_TILE_HEIGHT = 14;
    private const int TILE_MARGIN = 2;

    /// <summary>
    ///     Fixed pixel offset applied to the camera center. Used to shift the player's screen position away from dead center.
    ///     Applied automatically in WorldToScreen/ScreenToWorld.
    /// </summary>
    public Vector2 Offset { get; set; }

    /// <summary>
    ///     World pixel position of the camera center.
    /// </summary>
    public Vector2 Position { get; set; }

    /// <summary>
    ///     Viewport height in screen pixels.
    /// </summary>
    public int ViewportHeight { get; private set; }

    /// <summary>
    ///     Viewport width in screen pixels.
    /// </summary>
    public int ViewportWidth { get; private set; }

    /// <summary>
    ///     Zoom level. 1.0 = no zoom. Greater values zoom in.
    /// </summary>
    public float Zoom { get; set; } = 1.0f;

    public Camera(int viewportWidth, int viewportHeight)
    {
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
    }

    /// <summary>
    ///     Returns the range of tile coordinates currently visible on screen, clamped to map bounds.
    /// </summary>
    /// <param name="mapWidth">
    ///     Map width in tiles
    /// </param>
    /// <param name="mapHeight">
    ///     Map height in tiles
    /// </param>
    /// <param name="extraMargin">
    ///     Additional tile rows to expand the min bounds by, beyond the base margin. Used for foreground tiles whose tall
    ///     images extend upward beyond their base tile.
    /// </param>
    public (int MinX, int MinY, int MaxX, int MaxY) GetVisibleTileBounds(int mapWidth, int mapHeight, int extraMargin = 0)
    {
        var topLeft = ScreenToWorld(Vector2.Zero);
        var topRight = ScreenToWorld(new Vector2(ViewportWidth, 0));
        var bottomLeft = ScreenToWorld(new Vector2(0, ViewportHeight));
        var bottomRight = ScreenToWorld(new Vector2(ViewportWidth, ViewportHeight));

        var tl = WorldToTileFractional(topLeft.X, topLeft.Y, mapHeight);
        var tr = WorldToTileFractional(topRight.X, topRight.Y, mapHeight);
        var bl = WorldToTileFractional(bottomLeft.X, bottomLeft.Y, mapHeight);
        var br = WorldToTileFractional(bottomRight.X, bottomRight.Y, mapHeight);

        var maxMargin = TILE_MARGIN + extraMargin;

        var minTileX = (int)MathF.Floor(
                           Min(
                               tl.X,
                               tr.X,
                               bl.X,
                               br.X))
                       - TILE_MARGIN;

        var minTileY = (int)MathF.Floor(
                           Min(
                               tl.Y,
                               tr.Y,
                               bl.Y,
                               br.Y))
                       - TILE_MARGIN;

        var maxTileX = (int)MathF.Ceiling(
                           Max(
                               tl.X,
                               tr.X,
                               bl.X,
                               br.X))
                       + maxMargin;

        var maxTileY = (int)MathF.Ceiling(
                           Max(
                               tl.Y,
                               tr.Y,
                               bl.Y,
                               br.Y))
                       + maxMargin;

        minTileX = Math.Clamp(minTileX, 0, mapWidth - 1);
        minTileY = Math.Clamp(minTileY, 0, mapHeight - 1);
        maxTileX = Math.Clamp(maxTileX, 0, mapWidth - 1);
        maxTileY = Math.Clamp(maxTileY, 0, mapHeight - 1);

        return (minTileX, minTileY, maxTileX, maxTileY);
    }

    private static float Max(
        float a,
        float b,
        float c,
        float d)
        => MathF.Max(MathF.Max(a, b), MathF.Max(c, d));

    private static float Min(
        float a,
        float b,
        float c,
        float d)
        => MathF.Min(MathF.Min(a, b), MathF.Min(c, d));

    /// <summary>
    ///     Resizes the viewport. Use when the window size changes but no MonoGame Viewport struct is available.
    /// </summary>
    public void Resize(int width, int height)
    {
        ViewportWidth = width;
        ViewportHeight = height;
    }

    /// <summary>
    ///     Converts a screen pixel position to a world pixel position.
    /// </summary>
    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        var center = new Vector2(ViewportWidth / 2f, ViewportHeight / 2f);

        return (screenPos - center - Offset) / Zoom + Position;
    }

    /// <summary>
    ///     Converts tile coordinates to world pixel position using the same isometric formula as DALib's Graphics.RenderMap:
    ///     <br />
    ///     pixelX = (mapHeight - 1 + tileX - tileY) * 28
    ///     <br />
    ///     pixelY = (tileX + tileY) * 14
    /// </summary>
    public static Vector2 TileToWorld(int tileX, int tileY, int mapHeight)
    {
        var pixelX = (mapHeight - 1 + tileX - tileY) * HALF_TILE_WIDTH;
        var pixelY = (tileX + tileY) * HALF_TILE_HEIGHT;

        return new Vector2(pixelX, pixelY);
    }

    /// <summary>
    ///     Syncs the camera's viewport dimensions from the current MonoGame Viewport.
    /// </summary>
    public void Update(Viewport viewport)
    {
        ViewportWidth = viewport.Width;
        ViewportHeight = viewport.Height;
    }

    /// <summary>
    ///     Converts a world pixel position to a screen pixel position.
    /// </summary>
    public Vector2 WorldToScreen(Vector2 worldPos)
    {
        var center = new Vector2(ViewportWidth / 2f, ViewportHeight / 2f);

        return (worldPos - Position) * Zoom + center + Offset;
    }

    /// <summary>
    ///     Converts world pixel coordinates to tile coordinates. Inverts <see cref="TileToWorld" />, which returns the
    ///     bounding-box top-left of a tile's image. The diamond's visual center is at +HALF_TILE_WIDTH, +HALF_TILE_HEIGHT
    ///     from that origin, so to map a click on the visual center back to the right tile, subtract those offsets from
    ///     the world coordinates before doing the iso-grid math.
    /// </summary>
    public static Point WorldToTile(float worldX, float worldY, int mapHeight)
    {
        var isoX = (worldX - HALF_TILE_WIDTH) / HALF_TILE_WIDTH;
        var isoY = (worldY - HALF_TILE_HEIGHT) / HALF_TILE_HEIGHT;

        var tileX = (int)MathF.Floor((isoX + isoY - mapHeight + 1) / 2f);
        var tileY = (int)MathF.Floor((isoY - isoX + mapHeight - 1) / 2f);

        return new Point(tileX, tileY);
    }

    private static Vector2 WorldToTileFractional(float worldX, float worldY, int mapHeight)
    {
        var isoX = worldX / HALF_TILE_WIDTH;
        var isoY = worldY / HALF_TILE_HEIGHT;

        var tileX = (isoX + isoY - mapHeight + 1) / 2f;
        var tileY = (isoY - isoX + mapHeight - 1) / 2f;

        return new Vector2(tileX, tileY);
    }
}