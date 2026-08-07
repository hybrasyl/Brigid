#region
using Brigid.Controls.Components;
using Brigid.Models;
using Brigid.Rendering.Models;
using DALib.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Renders debug overlays for the world viewport: foreground tile outlines, entity tile rects with color coding,
///     entity click-detection hitboxes, player crosshair, mouse hover tile highlight, and per-entity name/position labels.
///     All visualization is opt-in via DebugOverlay.IsActive.
/// </summary>
public sealed class WorldDebugRenderer
{
    private readonly Dictionary<uint, DebugLabel> LabelCache = [];
    private readonly List<(TextElement Text, Vector2 Position)> PendingLabels = [];

    //per-entity cached label — string is rebuilt only when the identity/position inputs actually change
    private sealed class DebugLabel
    {
        public readonly TextElement Text = new();
        public int TileX = int.MinValue;
        public int TileY = int.MinValue;
        public string Name = string.Empty;
    }

    /// <summary>
    ///     Clears all cached debug labels. Call on map change or unload.
    /// </summary>
    public void Clear() => LabelCache.Clear();

    /// <summary>
    ///     Draws the pixel-texture geometry. Call within a SpriteBatch Begin/End block with camera transform applied.
    ///     <para>
    ///         Entity labels are <em>not</em> drawn here. They are queued and drawn by <see cref="DrawLabels" /> from
    ///         the native pass, because text rasterized into the 640x480 virtual target is point-upscaled with the
    ///         world and renders blurry. Positions need no conversion: both passes carry the same viewport
    ///         translation and differ only by the native scale.
    ///     </para>
    /// </summary>
    public void Draw(
        SpriteBatch spriteBatch,
        Camera camera,
        MapFile mapFile,
        int foregroundExtraMargin,
        IReadOnlyList<WorldEntity> sortedEntities,
        WorldEntity? player,
        IReadOnlyList<EntityHitBox> entityHitBoxes,
        Point? hoveredTile)
    {
        PendingLabels.Clear();

        var pixel = UIElement.GetPixel();

        DrawForegroundTileOutlines(
            spriteBatch,
            pixel,
            camera,
            mapFile,
            foregroundExtraMargin);

        DrawEntityTileRects(
            spriteBatch,
            pixel,
            camera,
            mapFile,
            sortedEntities);

        DrawPlayerCrosshair(
            spriteBatch,
            pixel,
            camera,
            mapFile,
            player);

        if (hoveredTile is { } tile)
            DrawMouseHoverTile(
                spriteBatch,
                pixel,
                camera,
                mapFile,
                tile);

        DrawEntityClickHitboxes(spriteBatch, pixel, entityHitBoxes);
    }

    /// <summary>
    ///     Draws the entity labels queued by the preceding <see cref="Draw" />. Call from the native pass, inside a
    ///     batch carrying the viewport translation and native scale.
    /// </summary>
    /// <remarks>
    ///     Drains the queue, so a frame in which <see cref="Draw" /> did not run — the overlay was toggled off, the
    ///     screen changed — draws nothing rather than repeating the last frame's labels.
    /// </remarks>
    public void DrawLabels(SpriteBatch spriteBatch)
    {
        foreach ((var text, var pos) in PendingLabels)
            text.Draw(spriteBatch, pos);

        PendingLabels.Clear();
    }

    private static void DrawEntityClickHitboxes(SpriteBatch spriteBatch, Texture2D pixel, IReadOnlyList<EntityHitBox> entityHitBoxes)
    {
        for (var i = 0; i < entityHitBoxes.Count; i++)
            DrawRectOutline(
                spriteBatch,
                pixel,
                entityHitBoxes[i].ScreenRect,
                Color.Orange * 0.8f);
    }

    private void DrawEntityTileRects(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        Camera camera,
        MapFile mapFile,
        IReadOnlyList<WorldEntity> sortedEntities)
    {
        for (var i = 0; i < sortedEntities.Count; i++)
        {
            var entity = sortedEntities[i];
            var tileWorld = Camera.TileToWorld(entity.TileX, entity.TileY, mapFile.Height);
            var tileCenterX = tileWorld.X + DaLibConstants.HALF_TILE_WIDTH;
            var topLeft = camera.WorldToScreen(new Vector2(tileWorld.X, tileWorld.Y));

            var tileRect = new Rectangle(
                (int)topLeft.X,
                (int)topLeft.Y,
                DaLibConstants.HALF_TILE_WIDTH * 2,
                DaLibConstants.HALF_TILE_HEIGHT * 2);

            var color = entity.Type switch
            {
                ClientEntityType.Aisling    => Color.Lime,
                ClientEntityType.Creature   => Color.Red,
                ClientEntityType.GroundItem => Color.Yellow,
                _                           => Color.White
            };

            DrawRectOutline(
                spriteBatch,
                pixel,
                tileRect,
                color * 0.6f);
            spriteBatch.Draw(pixel, tileRect, color * 0.15f);

            //entity name/info label (cached, deferred to draw after all pixel-texture geometry)
            if (!LabelCache.TryGetValue(entity.Id, out var cachedLabel))
            {
                cachedLabel = new DebugLabel();
                LabelCache[entity.Id] = cachedLabel;
            }

            //only rebuild the interpolated string when one of its inputs actually changed
            if ((cachedLabel.TileX != entity.TileX)
                || (cachedLabel.TileY != entity.TileY)
                || !ReferenceEquals(cachedLabel.Name, entity.Name))
            {
                cachedLabel.TileX = entity.TileX;
                cachedLabel.TileY = entity.TileY;
                cachedLabel.Name = entity.Name;

                cachedLabel.Text.Update($"{entity.Name} [{entity.Id}] ({entity.TileX},{entity.TileY})", color);
            }

            if (cachedLabel.Text.HasContent)
            {
                var labelPos = camera.WorldToScreen(new Vector2(tileCenterX - cachedLabel.Text.Width / 2f, tileWorld.Y - 12));
                PendingLabels.Add((cachedLabel.Text, labelPos));
            }
        }
    }

    private void DrawForegroundTileOutlines(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        Camera camera,
        MapFile mapFile,
        int foregroundExtraMargin)
    {
        (var fgMinX, var fgMinY, var fgMaxX, var fgMaxY) = camera.GetVisibleTileBounds(
            mapFile.Width,
            mapFile.Height,
            foregroundExtraMargin);

        for (var tileY = fgMinY; tileY <= fgMaxY; tileY++)
            for (var tileX = fgMinX; tileX <= fgMaxX; tileX++)
            {
                var tile = mapFile.Tiles[tileX, tileY];

                if (tile is { LeftForeground: 0, RightForeground: 0 })
                    continue;

                var tileWorld = Camera.TileToWorld(tileX, tileY, mapFile.Height);
                var topLeft = camera.WorldToScreen(new Vector2(tileWorld.X, tileWorld.Y));

                var tileRect = new Rectangle(
                    (int)topLeft.X,
                    (int)topLeft.Y,
                    DaLibConstants.HALF_TILE_WIDTH * 2,
                    DaLibConstants.HALF_TILE_HEIGHT * 2);

                DrawRectOutline(
                    spriteBatch,
                    pixel,
                    tileRect,
                    Color.Cyan * 0.3f);
            }
    }

    private static void DrawMouseHoverTile(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        Camera camera,
        MapFile mapFile,
        Point hoveredTile)
    {
        var hoverWorld = Camera.TileToWorld(hoveredTile.X, hoveredTile.Y, mapFile.Height);
        var hoverScreen = camera.WorldToScreen(new Vector2(hoverWorld.X, hoverWorld.Y));

        var hoverRect = new Rectangle(
            (int)hoverScreen.X,
            (int)hoverScreen.Y,
            DaLibConstants.HALF_TILE_WIDTH * 2,
            DaLibConstants.HALF_TILE_HEIGHT * 2);

        spriteBatch.Draw(pixel, hoverRect, Color.Magenta * 0.3f);

        DrawRectOutline(
            spriteBatch,
            pixel,
            hoverRect,
            Color.Magenta);
    }

    private static void DrawPlayerCrosshair(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        Camera camera,
        MapFile mapFile,
        WorldEntity? player)
    {
        if (player is null)
            return;

        var playerWorld = Camera.TileToWorld(player.TileX, player.TileY, mapFile.Height);

        var playerCenter = camera.WorldToScreen(
            new Vector2(
                playerWorld.X + DaLibConstants.HALF_TILE_WIDTH + player.VisualOffset.X,
                playerWorld.Y + DaLibConstants.HALF_TILE_HEIGHT + player.VisualOffset.Y));

        spriteBatch.Draw(
            pixel,
            new Rectangle(
                (int)playerCenter.X - 5,
                (int)playerCenter.Y,
                11,
                1),
            Color.White);

        spriteBatch.Draw(
            pixel,
            new Rectangle(
                (int)playerCenter.X,
                (int)playerCenter.Y - 5,
                1,
                11),
            Color.White);
    }

    private static void DrawRectOutline(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        Rectangle rect,
        Color color)
    {
        spriteBatch.Draw(
            pixel,
            new Rectangle(
                rect.X,
                rect.Y,
                rect.Width,
                1),
            color);

        spriteBatch.Draw(
            pixel,
            new Rectangle(
                rect.X,
                rect.Bottom - 1,
                rect.Width,
                1),
            color);

        spriteBatch.Draw(
            pixel,
            new Rectangle(
                rect.X,
                rect.Y,
                1,
                rect.Height),
            color);

        spriteBatch.Draw(
            pixel,
            new Rectangle(
                rect.Right - 1,
                rect.Y,
                1,
                rect.Height),
            color);
    }

    /// <summary>
    ///     Removes a single entity's cached debug label. Call when an entity is removed from the world.
    /// </summary>
    public void RemoveEntity(uint entityId) => LabelCache.Remove(entityId);
}