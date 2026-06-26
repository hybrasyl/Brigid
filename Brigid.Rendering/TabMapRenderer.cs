#region
using Brigid.Data;
using Brigid.Rendering.Models;
using Chaos.DarkAges.Definitions;
using DALib.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TileFlags = DALib.Definitions.TileFlags;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Renders the Tab map overlay: a simplified isometric view of the current map showing walls as diamond outlines and
///     entities as colored diamonds. Supports PageUp/PageDown zoom. Centered on the player.
/// </summary>
/// <remarks>
///     Wall tiles use 20x10 scaled diamonds (~36% of 56x27). Adjacent walls collapse shared borders via a 4-bit neighbor
///     mask mapped to 16 pre-baked atlas variants. Entity colors: yellow (player), red (monsters), green (merchants),
///     blue (aislings). Entity overlap is handled via stencil masking.
/// </remarks>
public sealed class TabMapRenderer : IDisposable
{
    //~33% of original 56x27 tile, rounded to clean staircase (20x10)
    private const int TILE_W = 20;
    private const int TILE_H = 10;
    private const int HALF_TILE_W = 10;
    private const int HALF_TILE_H = 5;
    private const int TOP_HALF_MAX_ROW = 4;
    private const float ZOOM_MIN = 0.25f;
    private const float ZOOM_MAX = 4.0f;
    private const float ZOOM_STEP = 0.25f;
    private const float ZOOM_DEFAULT = 1.0f;

    //neighbor mask bits — which edges have adjacent tab-map tiles
    private const int MASK_TOP_LEFT = 0x1;
    private const int MASK_TOP_RIGHT = 0x2;
    private const int MASK_BOTTOM_RIGHT = 0x4;
    private const int MASK_BOTTOM_LEFT = 0x8;

    //atlas indices for entity diamonds (after the 16 border-collapse variants)
    private const int ATLAS_PLAYER = 16;
    private const int ATLAS_CREATURE = 17;
    private const int ATLAS_AISLING = 18;
    private const int ATLAS_MERCHANT = 19;

    //fill is 25% transparent black; border is fully opaque white
    private static readonly Color FILL_COLOR = Color.LightGray * 0.25f;
    private static readonly Color BORDER_COLOR = Color.White;
    private static readonly Color COLOR_PLAYER = new(255, 231, 57);
    private static readonly Color COLOR_CREATURE = new(206, 0, 16);
    private static readonly Color COLOR_MERCHANT = new(0, 255, 0);
    private static readonly Color COLOR_AISLING = new(123, 166, 247);

    //diamond row bounds for 20x10 tile (staircase: 4px at top, +4px/row, full 20px at center)
    private static readonly (int StartX, int EndX)[] DiamondRows = ComputeDiamondRows();

    //stencil states for entity overlap masking
    private static readonly DepthStencilState StencilWrite = new()
    {
        StencilEnable = true,
        StencilFunction = CompareFunction.Always,
        StencilPass = StencilOperation.IncrementSaturation,
        ReferenceStencil = 0,
        DepthBufferEnable = false
    };

    private static readonly DepthStencilState StencilTestSingleCoverage = new()
    {
        StencilEnable = true,
        StencilFunction = CompareFunction.Equal,
        ReferenceStencil = 1,
        StencilPass = StencilOperation.Keep,
        DepthBufferEnable = false
    };

    private static readonly RasterizerState ScissorRasterizer = new()
    {
        ScissorTestEnable = true
    };

        private AlphaTestEffect? AlphaTest;
    private TextureAtlas? Atlas;
    private (int X, int Y, byte Mask)[] Entries = [];

    //precomputed: which tiles are walls, and their neighbor masks
    private bool[,]? IsWallTile;
    private bool[,]? VisibilityScratch;
    private readonly Dictionary<(int, int), int> OccupiedByTile = [];
    private int MapHeight;
    private int MapWidth;

    public float Zoom { get; private set; } = ZOOM_DEFAULT;


    /// <inheritdoc />
    public void Dispose()
    {
        Atlas?.Dispose();
        Atlas = null;
        AlphaTest?.Dispose();
        AlphaTest = null;
    }

    /// <summary>
    ///     Builds a texture atlas: 16 border-collapse wall variants + 4 solid entity diamonds. All colors fully opaque —
    ///     overall transparency applied via SpriteBatch tint at draw time.
    /// </summary>
    private void BuildAtlas(GraphicsDevice device)
    {
        Atlas = new TextureAtlas(
            device,
            PackingMode.Grid,
            TILE_W,
            TILE_H);

        //16 border-collapse variants (indices 0-15)
        for (var mask = 0; mask < 16; mask++)
        {
            var hasTopLeft = (mask & MASK_TOP_LEFT) != 0;
            var hasTopRight = (mask & MASK_TOP_RIGHT) != 0;
            var hasBottomRight = (mask & MASK_BOTTOM_RIGHT) != 0;
            var hasBottomLeft = (mask & MASK_BOTTOM_LEFT) != 0;

            var tilePixels = new Color[TILE_W * TILE_H];

            for (var row = 0; row < TILE_H; row++)
            {
                (var sx, var ex) = DiamondRows[row];
                var isTopHalf = row <= TOP_HALF_MAX_ROW;

                for (var x = sx; x <= ex; x++)
                {
                    var isLeftBorder = (x - sx) < 2;
                    var isRightBorder = (ex - x) < 2;
                    var drawBorder = false;

                    if (isLeftBorder)
                    {
                        var hasNeighbor = isTopHalf ? hasTopLeft : hasBottomLeft;

                        if (!hasNeighbor)
                            drawBorder = true;
                    }

                    if (isRightBorder)
                    {
                        var hasNeighbor = isTopHalf ? hasTopRight : hasBottomRight;

                        if (!hasNeighbor)
                            drawBorder = true;
                    }

                    tilePixels[row * TILE_W + x] = drawBorder ? BORDER_COLOR : FILL_COLOR;
                }
            }

            Atlas.Add(
                mask,
                tilePixels,
                TILE_W,
                TILE_H);
        }

        //entity diamonds: solid fill (indices 16-19)
        Color[] entityColors =
        [
            COLOR_PLAYER,
            COLOR_CREATURE,
            COLOR_AISLING,
            COLOR_MERCHANT
        ];

        int[] entityIndices =
        [
            ATLAS_PLAYER,
            ATLAS_CREATURE,
            ATLAS_AISLING,
            ATLAS_MERCHANT
        ];

        for (var i = 0; i < entityColors.Length; i++)
        {
            var tilePixels = new Color[TILE_W * TILE_H];
            var color = entityColors[i];

            for (var row = 0; row < TILE_H; row++)
            {
                (var sx, var ex) = DiamondRows[row];

                for (var x = sx; x <= ex; x++)
                    tilePixels[row * TILE_W + x] = color;
            }

            Atlas.Add(
                entityIndices[i],
                tilePixels,
                TILE_W,
                TILE_H);
        }

        Atlas.Build();
    }

    /// <summary>
    ///     Diamond row bounds for 20x10 tile. Row 0: 4px centered, +4px/row, full 20px at rows 4-5.
    /// </summary>
    private static (int StartX, int EndX)[] ComputeDiamondRows()
    {
        var rows = new (int, int)[TILE_H];

        for (var r = 0; r < TILE_H; r++)
        {
            int expandRow;

            if (r <= TOP_HALF_MAX_ROW)
                expandRow = r;
            else
                expandRow = TILE_H - 1 - r;

            var startX = HALF_TILE_W - 2 - expandRow * 2;
            var endX = HALF_TILE_W + 1 + expandRow * 2;

            rows[r] = (Math.Max(0, startX), Math.Min(TILE_W - 1, endX));
        }

        return rows;
    }

    /// <summary>
    ///     Draws the tab map overlay. Manages its own SpriteBatch Begin/End blocks. Walls drawn normally. Entities drawn via
    ///     stencil: overlapping pixels become transparent.
    /// </summary>
    public void Draw(
        SpriteBatch spriteBatch,
        GraphicsDevice device,
        Rectangle viewport,
        int playerTileX,
        int playerTileY,
        TabMapEntity[] entities,
        int entityCount,
        uint playerEntityId,
        bool isFullBlackDark,
        ReadOnlySpan<LightSource> lightSources,
        (int Dx, int Dy)[] baselineOffsets)
    {
        if (Atlas is null || (Entries.Length == 0))
            return;

        //defensive: if scratch wasn't allocated (Generate not yet called), force off
        var effectiveFullBlackDark = isFullBlackDark && (VisibilityScratch is not null);

        if (effectiveFullBlackDark)
            ComputeVisibility(playerTileX, playerTileY, lightSources, baselineOffsets);

        //player's tile center in iso space
        var playerIsoX = (MapHeight - 1 + playerTileX - playerTileY) * HALF_TILE_W + HALF_TILE_W;
        var playerIsoY = (playerTileX + playerTileY) * HALF_TILE_H + HALF_TILE_H;

        //offset so player's iso position maps to viewport center
        var centerX = viewport.X + viewport.Width / 2f;
        var centerY = viewport.Y + viewport.Height / 2f;
        var offsetX = centerX - playerIsoX * Zoom;
        var offsetY = centerY - playerIsoY * Zoom;

        var scaledTileW = (int)(TILE_W * Zoom);
        var scaledTileH = (int)(TILE_H * Zoom);

        //pass 1: draw wall tiles (normal alpha blend)
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, rasterizerState: ScissorRasterizer);
        device.ScissorRectangle = viewport;

        foreach ((var tx, var ty, var mask) in Entries)
        {
            if (effectiveFullBlackDark && !VisibilityScratch![tx, ty])
                continue;

            var region = Atlas.TryGetRegion(mask);

            if (!region.HasValue)
                continue;

            var isoX = (MapHeight - 1 + tx - ty) * HALF_TILE_W;
            var isoY = (tx + ty) * HALF_TILE_H;
            var screenX = (int)(offsetX + isoX * Zoom);
            var screenY = (int)(offsetY + isoY * Zoom);

            spriteBatch.Draw(
                region.Value.Atlas,
                new Rectangle(
                    screenX,
                    screenY,
                    scaledTileW,
                    scaledTileH),
                region.Value.SourceRect,
                Color.White);
        }

        spriteBatch.End();

        //alphatesteffect discards transparent pixels so only diamond-shaped pixels write to stencil
        AlphaTest ??= new AlphaTestEffect(device)
        {
            VertexColorEnabled = true
        };

        var vp = device.Viewport;

        AlphaTest.Projection = Matrix.CreateOrthographicOffCenter(
            0,
            vp.Width,
            vp.Height,
            0,
            0,
            1);

        //deduplicate entities sharing a tile — keep highest priority per tile so the stencil
        //overlap system (which rejects pixels with stencil > 1) doesn't blank out same-tile entities
        var deduplicatedCount = DeduplicateEntitiesByTile(entities, entityCount, playerEntityId);

        //pass 2: stamp entity diamonds into stencil buffer (no color output)
        //only opaque diamond pixels increment stencil — transparent rectangle areas are discarded
        device.Clear(
            ClearOptions.Stencil,
            Color.Transparent,
            0,
            0);

        spriteBatch.Begin(
            blendState: BlendStates.NoColorWrite,
            depthStencilState: StencilWrite,
            samplerState: SamplerState.PointClamp,
            rasterizerState: ScissorRasterizer,
            effect: AlphaTest);
        device.ScissorRectangle = viewport;

        DrawEntityDiamonds(
            spriteBatch,
            entities,
            deduplicatedCount,
            playerEntityId,
            offsetX,
            offsetY,
            scaledTileW,
            scaledTileH,
            effectiveFullBlackDark);
        spriteBatch.End();

        //pass 3: draw entity diamonds with color, but only where stencil == 1 (single coverage)
        //pixels where multiple entities overlap (stencil > 1) stay transparent
        spriteBatch.Begin(
            depthStencilState: StencilTestSingleCoverage,
            samplerState: SamplerState.PointClamp,
            rasterizerState: ScissorRasterizer,
            effect: AlphaTest);
        device.ScissorRectangle = viewport;

        DrawEntityDiamonds(
            spriteBatch,
            entities,
            deduplicatedCount,
            playerEntityId,
            offsetX,
            offsetY,
            scaledTileW,
            scaledTileH,
            effectiveFullBlackDark);
        spriteBatch.End();
    }

    private void DrawEntityDiamonds(
        SpriteBatch spriteBatch,
        TabMapEntity[] entities,
        int entityCount,
        uint playerEntityId,
        float offsetX,
        float offsetY,
        int scaledTileW,
        int scaledTileH,
        bool isFullBlackDark)
    {
        for (var i = 0; i < entityCount; i++)
        {
            var entity = entities[i];

            if (entity.Type == ClientEntityType.GroundItem)
                continue;

            //entity coords come from the network and may race against a map transition.
            //treat out-of-bounds as hidden rather than throwing.
            if (isFullBlackDark
                && (((uint)entity.TileX >= (uint)MapWidth)
                    || ((uint)entity.TileY >= (uint)MapHeight)
                    || !VisibilityScratch![entity.TileX, entity.TileY]))
                continue;

            int atlasIndex;

            if (entity.Id == playerEntityId)
                atlasIndex = ATLAS_PLAYER;
            else if (entity.Type == ClientEntityType.Aisling)
                atlasIndex = ATLAS_AISLING;
            else if (entity.CreatureType == CreatureType.Merchant)
                atlasIndex = ATLAS_MERCHANT;
            else
                atlasIndex = ATLAS_CREATURE;

            var region = Atlas!.TryGetRegion(atlasIndex);

            if (!region.HasValue)
                continue;

            var entIsoX = (MapHeight - 1 + entity.TileX - entity.TileY) * HALF_TILE_W;
            var entIsoY = (entity.TileX + entity.TileY) * HALF_TILE_H;
            var entScreenX = (int)(offsetX + entIsoX * Zoom);
            var entScreenY = (int)(offsetY + entIsoY * Zoom);

            spriteBatch.Draw(
                region.Value.Atlas,
                new Rectangle(
                    entScreenX,
                    entScreenY,
                    scaledTileW,
                    scaledTileH),
                region.Value.SourceRect,
                Color.White);
        }
    }

    /// <summary>
    ///     Generates the tab map wall data from a MapFile. Call when the map changes. Builds the diamond atlas on first call.
    /// </summary>
    public void Generate(GraphicsDevice device, MapFile mapFile)
    {
        if (Atlas is null)
            BuildAtlas(device);

        MapWidth = mapFile.Width;
        MapHeight = mapFile.Height;

        //determine which tiles are walls via sotp (matching chaosassetmanager's detection)
        IsWallTile = new bool[MapWidth, MapHeight];
        VisibilityScratch = new bool[MapWidth, MapHeight];

        for (var y = 0; y < MapHeight; y++)
            for (var x = 0; x < MapWidth; x++)
            {
                var tile = mapFile.Tiles[x, y];

                if (IsWall(tile.LeftForeground) || IsWall(tile.RightForeground))
                    IsWallTile[x, y] = true;
            }

        //precompute entries with neighbor masks for border collapse
        var entries = new List<(int, int, byte)>();

        for (var y = 0; y < MapHeight; y++)
            for (var x = 0; x < MapWidth; x++)
            {
                if (!IsWallTile[x, y])
                    continue;

                byte mask = 0;

                //top-left edge neighbor: (x-1, y)
                if ((x > 0) && IsWallTile[x - 1, y])
                    mask |= MASK_TOP_LEFT;

                //top-right edge neighbor: (x, y-1)
                if ((y > 0) && IsWallTile[x, y - 1])
                    mask |= MASK_TOP_RIGHT;

                //bottom-right edge neighbor: (x+1, y)
                if ((x < (MapWidth - 1)) && IsWallTile[x + 1, y])
                    mask |= MASK_BOTTOM_RIGHT;

                //bottom-left edge neighbor: (x, y+1)
                if ((y < (MapHeight - 1)) && IsWallTile[x, y + 1])
                    mask |= MASK_BOTTOM_LEFT;

                entries.Add((x, y, mask));
            }

        Entries = entries.ToArray();
    }

    /// <summary>
    ///     Per-frame visibility computation for full-black-darkness maps. Clears the scratch grid then
    ///     stamps the baseline (around the player) and every light source's tile offsets.
    /// </summary>
    private void ComputeVisibility(
        int playerTileX,
        int playerTileY,
        ReadOnlySpan<LightSource> lightSources,
        (int Dx, int Dy)[] baselineOffsets)
    {
        var scratch = VisibilityScratch!;
        Array.Clear(scratch);

        StampOffsets(scratch, playerTileX, playerTileY, baselineOffsets);

        for (var i = 0; i < lightSources.Length; i++)
        {
            var source = lightSources[i];
            StampOffsets(scratch, source.TileX, source.TileY, source.TileOffsets);
        }
    }

    /// <summary>
    ///     Stamps a relative offset array centered at (centerX, centerY) into the visibility scratch grid.
    ///     Out-of-bounds offsets are silently dropped via a single unsigned-cast bounds check.
    /// </summary>
    private void StampOffsets(bool[,] scratch, int centerX, int centerY, (int Dx, int Dy)[] offsets)
    {
        for (var i = 0; i < offsets.Length; i++)
        {
            var (dx, dy) = offsets[i];
            var tx = centerX + dx;
            var ty = centerY + dy;

            if (((uint)tx < (uint)MapWidth) && ((uint)ty < (uint)MapHeight))
                scratch[tx, ty] = true;
        }
    }

    /// <summary>
    ///     Checks if a foreground tile index is a wall via SOTP flags. Index 0 = no tile. Otherwise index-1 into SOTP, check
    ///     HasFlag(Wall). Matches ChaosAssetManager's MapEditorRenderUtil.IsWall().
    /// </summary>
    private bool IsWall(int fgIndex)
    {
        if (fgIndex == 0)
            return false;

        var sotpIndex = fgIndex - 1;
        var sotpData = DataContext.Tiles.SotpData;

        if (sotpIndex >= sotpData.Length)
            return false;

        return (sotpData[sotpIndex] & TileFlags.Wall) != 0;
    }

    /// <summary>
    ///     Removes duplicate entities sharing a tile, keeping the highest priority one. Compacts in-place.
    ///     Priority: player > creature > aisling > merchant.
    /// </summary>
    private int DeduplicateEntitiesByTile(TabMapEntity[] entities, int count, uint playerEntityId)
    {
        OccupiedByTile.Clear();
        var occupied = OccupiedByTile;
        var writeIndex = 0;

        for (var i = 0; i < count; i++)
        {
            var entity = entities[i];

            if (entity.Type == ClientEntityType.GroundItem)
                continue;

            var key = (entity.TileX, entity.TileY);
            var priority = GetEntityPriority(entity, playerEntityId);

            if (occupied.TryGetValue(key, out var existingWriteIndex))
            {
                var existingPriority = GetEntityPriority(entities[existingWriteIndex], playerEntityId);

                if (priority <= existingPriority)
                    continue;

                entities[existingWriteIndex] = entity;
            } else
            {
                occupied[key] = writeIndex;
                entities[writeIndex] = entity;
                writeIndex++;
            }
        }

        return writeIndex;
    }

    private static int GetEntityPriority(TabMapEntity entity, uint playerEntityId)
    {
        if (entity.Id == playerEntityId)
            return 3;

        if ((entity.Type == ClientEntityType.Creature) && (entity.CreatureType != CreatureType.Merchant))
            return 2;

        if (entity.Type == ClientEntityType.Aisling)
            return 1;

        return 0;
    }

    public void ZoomIn() => Zoom = MathF.Min(Zoom + ZOOM_STEP, ZOOM_MAX);

    public void ZoomOut() => Zoom = MathF.Max(Zoom - ZOOM_STEP, ZOOM_MIN);
}