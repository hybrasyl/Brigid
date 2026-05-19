#region
using Chaos.Client.Collections;
using Chaos.Client.Data;
using Chaos.Client.Systems;
using Chaos.DarkAges.Definitions;
using Chaos.Geometry;
using Chaos.Geometry.Abstractions;
using Chaos.Networking.Entities.Server;
using Chaos.Pathfinding;
using DALib.Cryptography;
using DALib.Data;
using DALib.Extensions;
using Pathfinder = Chaos.Pathfinding.Pathfinder;
using TileFlags = DALib.Definitions.TileFlags;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
#endregion

namespace Chaos.Client.Screens;

public sealed partial class WorldScreen
{
    #region Map Assembly
    private void HandleUserId(uint id) => WorldState.PlayerEntityId = id;

    private void HandleMapInfo(MapInfoArgs args)
    {
        WorldMap.HideMap();
        //same map (refresh) — skip expensive teardown, just clear transient entity state. checksum must
        //also match: a server-side map edit reuses the same id but changes the bytes, so a mismatch means
        //the cached MapFile is stale and we have to take the full reload path.
        if ((args.MapId == CurrentMapId) && (args.CheckSum == CurrentMapCheckSum) && MapFile is not null)
        {
            ClearTransientState();

            //re-evaluate darkness and weather only if the flag actually changed
            var newFlags = (MapFlags)args.Flags;

            if (newFlags != CurrentMapFlags)
            {
                CurrentMapFlags = newFlags;
                DarknessRenderer.OnMapChanged(args.MapId, CurrentMapFlags.HasFlag(MapFlags.Darkness));
                WeatherRenderer.OnMapChanged(CurrentMapFlags);
            }

            UpdateHuds(HudOps.SetZoneName, args.Name);

            return;
        }

        //new map — dispose old caches, load fresh mapfile from local files
        TownMapControl.Hide();
        MapRenderer.Dispose();
        MapRenderer = new MapRenderer();

        MapFile = LoadMapFile(
            args.MapId,
            args.Width,
            args.Height,
            args.CheckSum);
        MapPreloaded = false;
        AwaitingMapData = false;
        CurrentMapId = args.MapId;
        CurrentMapCheckSum = args.CheckSum;
        CurrentMapFlags = (MapFlags)args.Flags;
        MapLoading.Show();

        //local file missing, corrupt, or checksum mismatch — request from server
        if (MapFile is null)
        {
            MapFile = new MapFile(args.Width, args.Height);
            InitializeEmptyTiles(MapFile);
            AwaitingMapData = true;
            Game.Connection.RequestMapData();
        }

        //clear entity + renderer caches for the new map
        ClearTransientState();
        Game.CreatureRenderer.Clear();
        Game.AislingRenderer.ClearCache();
        Game.AislingRenderer.ClearTintedCache();
        Game.AislingRenderer.ClearLayerImageCache();
        Game.ItemRenderer.Clear();

        //reset darkness state and load hea light map for the new map
        DarknessRenderer.OnMapChanged(args.MapId, CurrentMapFlags.HasFlag(MapFlags.Darkness));
        WeatherRenderer.OnMapChanged(CurrentMapFlags);

        UpdateHuds(HudOps.SetZoneName, args.Name);
        UpdateHuds(HudOps.ShowPersistentMessage, string.Empty);
    }

    private void ClearTransientState()
    {
        WorldState.Clear();
        Game.AislingRenderer.ClearCompositeCache();
        Overlays.Clear();
        DebugRenderer.Clear();
        NpcSession.HideAll();
        Pathfinding.Clear();
        GroupHighlightedIds.Clear();
        Game.AislingRenderer.ClearGroupTintCache();
        Game.CreatureRenderer.ClearTintCaches();
    }

    private void HandleMapData(MapDataArgs args)
    {
        if (MapFile is null)
            return;

        var y = args.CurrentYIndex;

        if (y >= MapFile.Height)
            return;

        //each tile is 6 bytes: bg(2 be), lfg(2 be), rfg(2 be)
        var data = args.MapData;
        var tileCount = Math.Min(data.Length / 6, MapFile.Width);

        for (var x = 0; x < tileCount; x++)
        {
            var offset = x * 6;
            var background = (short)((data[offset] << 8) | data[offset + 1]);
            var leftForeground = (short)((data[offset + 2] << 8) | data[offset + 3]);
            var rightForeground = (short)((data[offset + 4] << 8) | data[offset + 5]);

            MapFile.Tiles[x, y] = new MapTile
            {
                Background = background,
                LeftForeground = leftForeground,
                RightForeground = rightForeground
            };
        }

        //last row received — save to disk and finalize
        if (AwaitingMapData && (y >= (MapFile.Height - 1)))
        {
            AwaitingMapData = false;
            SaveMapFile(CurrentMapId);
            FinalizeMapLoad();
        }
    }

    private void HandleMapLoadComplete()
    {
        //when awaiting server map data, ignore this — finalizemapload will be called from handlemapdata
        if (AwaitingMapData)
            return;

        FinalizeMapLoad();
    }

    private void FinalizeMapLoad()
    {
        if (MapFile is null)
            return;

        if (!MapPreloaded)
        {
            MapRenderer.PreloadMapTiles(
                Device,
                MapFile,
                MapLoading.SetProgress,
                static id => DoorTable.GetVariants((short)id).Select(static v => (int)v));
            TabMapRenderer.Generate(Device, MapFile);
            (MapPathfinder, MapWaterTiles) = BuildPathfinder(MapFile);
            MapPreloaded = true;
        }

        MapLoading.Hide();
        FollowPlayerCamera();
        Game.GcRequested = true;
    }

    private static (Pathfinder Pathfinder, List<IPoint> WaterTiles) BuildPathfinder(MapFile mapFile)
    {
        var gndAttrs = DataContext.Tiles.GroundAttributes;
        var walls = new List<IPoint>();
        var waterTiles = new List<IPoint>();

        for (var y = 0; y < mapFile.Height; y++)
            for (var x = 0; x < mapFile.Width; x++)
            {
                var tile = mapFile.Tiles[x, y];

                if (IsTileWall(tile.LeftForeground) || IsTileWall(tile.RightForeground))
                    walls.Add(new Point(x, y));
                else if (gndAttrs.TryGetValue(tile.Background, out var gndAttr) && gndAttr.IsWalkBlocking)
                    waterTiles.Add(new Point(x, y));
            }

        var pathfinder = new Pathfinder(
            new GridDetails
            {
                Width = mapFile.Width,
                Height = mapFile.Height,
                Walls = walls,
                BlockingReactors = []
            });

        return (pathfinder, waterTiles);
    }

    /// <summary>
    ///     Returns the current set of blocked points for pathfinding: entity positions, plus water tiles when the swim gate
    ///     is active and the player can't swim. GMs bypass all blocking.
    /// </summary>
    private List<IPoint> GetPathfindingBlockedPoints()
    {
        if (IsGameMaster)
            return [];

        var blocked = WorldState.GetBlockedPoints();

        if (GlobalSettings.RequireSwimmingSkill && !WorldState.SkillBook.HasSkillByName("swimming"))
            blocked.AddRange(MapWaterTiles);

        return blocked;
    }

    private bool TileHasForeground(int tileX, int tileY)
    {
        if (MapFile is null)
            return false;

        if ((tileX < 0) || (tileY < 0) || (tileX >= MapFile.Width) || (tileY >= MapFile.Height))
            return false;

        var tile = MapFile.Tiles[tileX, tileY];

        return tile.LeftForeground.IsRenderedTileIndex() || tile.RightForeground.IsRenderedTileIndex();
    }

    /// <summary>
    ///     True when the foreground is a walk-blocking wall. Open door tile IDs are treated as walkable even when SOTP still
    ///     has their Wall bit set — <see cref="DoorTable" /> is the source of truth for "is this foreground a known open
    ///     door?" because <c>sotp.dat</c> is inconsistent about clearing the flag on some open variants.
    /// </summary>
    private static bool IsTileWall(int fgIndex)
    {
        if (fgIndex <= 0)
            return false;

        //an open door foreground is walkable regardless of SOTP
        if (DoorTable.GetClosedTileId((short)fgIndex).HasValue)
            return false;

        var sotpIndex = fgIndex - 1;
        var sotpData = DataContext.Tiles.SotpData;

        if (sotpIndex >= sotpData.Length)
            return false;

        return (sotpData[sotpIndex] & TileFlags.Wall) != 0;
    }

    private bool IsTileWallBlocked(int tileX, int tileY)
    {
        if (MapFile is null)
            return false;

        if ((tileX < 0) || (tileY < 0) || (tileX >= MapFile.Width) || (tileY >= MapFile.Height))
            return false;

        var tile = MapFile.Tiles[tileX, tileY];

        return IsTileWall(tile.LeftForeground) || IsTileWall(tile.RightForeground);
    }

    private bool IsTilePassable(int tileX, int tileY)
    {
        if (MapFile is null)
            return true;

        //check wall tiles (foreground sotp data)
        var tile = MapFile.Tiles[tileX, tileY];

        if (IsTileWall(tile.LeftForeground) || IsTileWall(tile.RightForeground))
            return false;

        //check gndattr walk-blocking (deep water tiles) — only when swim gate active and player can't swim
        if (GlobalSettings.RequireSwimmingSkill
            && !IsGameMaster
            && !WorldState.SkillBook.HasSkillByName("swimming")
            && DataContext.Tiles.GroundAttributes.TryGetValue(tile.Background, out var gndAttr)
            && gndAttr.IsWalkBlocking)
            return false;

        //check entities at the destination tile
        if (WorldState.HasBlockingEntityAt(tileX, tileY, WorldState.PlayerEntityId))
            return false;

        return true;
    }

    private static MapFile? LoadMapFile(
        int mapId,
        int width,
        int height,
        ushort serverCheckSum)
    {
        var path = Path.Combine(DataContext.DataPath, "maps", $"lod{mapId}.map");

        if (!File.Exists(path))
            return null;

        try
        {
            var fileBytes = File.ReadAllBytes(path);

            if (fileBytes.Length != (width * height * 6))
                return null;

            if (CRC16.Calculate(fileBytes) != serverCheckSum)
                return null;

            //parse in-place — file format is le int16 x3 per tile, y-major x-minor
            var mapFile = new MapFile(width, height);
            var index = 0;

            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var background = (short)(fileBytes[index] | (fileBytes[index + 1] << 8));
                    var leftForeground = (short)(fileBytes[index + 2] | (fileBytes[index + 3] << 8));
                    var rightForeground = (short)(fileBytes[index + 4] | (fileBytes[index + 5] << 8));
                    index += 6;

                    mapFile.Tiles[x, y] = new MapTile
                    {
                        Background = background,
                        LeftForeground = leftForeground,
                        RightForeground = rightForeground
                    };
                }

            return mapFile;
        } catch
        {
            return null;
        }
    }

    private void SaveMapFile(int mapId)
    {
        var path = Path.Combine(DataContext.DataPath, "maps", $"lod{mapId}.map");
        MapFile!.Save(path);
    }

    private void HandleLocationChanged(int x, int y)
    {
        UpdateHuds(HudOps.SetCoords, x, y);

        var player = WorldState.GetPlayerEntity();

        if (player is null)
            return;

        //if the server position matches, nothing to reconcile
        if ((player.TileX == x) && (player.TileY == y))
            return;

        //server-authoritative position correction — snap and reset visuals.
        //InFlightWalkAcks is intentionally NOT touched: any acks the server still owes us for walks
        //we predicted will arrive after this Location and drain the counter naturally as no-ops.
        //Clearing the counter here would turn those legitimate acks into "unmatched" acks that fall
        //through HandleClientWalkResponse's rubberband path and undo the snap we just applied —
        //which is exactly the F5-during-walk symptom.
        QueuedWalkDirection = null;
        player.TileX = x;
        player.TileY = y;
        WorldState.MarkSortDirty();
        AnimationSystem.ResetToIdle(player);
        Pathfinding.Clear();
        FollowPlayerCamera();
    }

    /// <summary>
    ///     Sets Camera.Offset for the current HUD. Both HUD MAP rects share the same X origin and width; only the height
    ///     differs (large is ~116px taller, extending downward). The small HUD is calibrated to (-28, 24). The large HUD uses
    ///     (-28, -4) so the player appears ~30px lower than the small HUD on screen — not the naive ~58px that a fixed
    ///     viewport-relative offset would produce.
    /// </summary>
    private void UpdateCameraOffset(XnaRectangle viewport)
    {
        _ = viewport;
        Camera.Offset = WorldHud == SmallHud ? new XnaVector2(-28, 24) : new XnaVector2(-28, -4);
    }

    /// <summary>
    ///     Updates camera position to follow the player entity's visual position, including walk interpolation offset. In
    ///     rough scroll mode, only updates at fixed intervals for a choppier look.
    /// </summary>
    private void FollowPlayerCamera()
    {
        if (MapFile is null)
            return;

        var player = WorldState.GetPlayerEntity();

        if (player is null)
            return;

        var tileWorld = Camera.TileToWorld(player.TileX, player.TileY, MapFile.Height);
        Camera.Position = tileWorld + player.VisualOffset;
    }

    private static void InitializeEmptyTiles(MapFile mapFile)
    {
        for (var y = 0; y < mapFile.Height; y++)
            for (var x = 0; x < mapFile.Width; x++)
                mapFile.Tiles[x, y] = new MapTile();
    }
    #endregion
}
