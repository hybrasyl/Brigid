#region
using Brigid.Collections;
using Brigid.Data;
using Brigid.Models;
using Brigid.Networking;
using Brigid.Systems;
using Brigid.Systems.Keybinds;
using Chaos.DarkAges.Definitions;
using Chaos.Geometry;
using Chaos.Geometry.Abstractions;
using Chaos.Pathfinding;
using DALib.Data;
using DALib.Networking.Packets.Server;
using DALib.Extensions;
using Pathfinder = Chaos.Pathfinding.Pathfinder;
using TileFlags = DALib.Definitions.TileFlags;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
#endregion

namespace Brigid.Screens;

public sealed partial class WorldScreen
{
    //a map-data wait with no new row for this long ends: a server that drops or short-streams the rows must not keep the
    //loading bar on the screen with no limit
    private const float MAP_DATA_TIMEOUT_MS = 10_000f;

    #region Map Assembly
    private void HandleUserId(uint id) => WorldState.PlayerEntityId = id;

    private void HandleMapInfo(MapInfoPacket args)
    {
        WorldMap.HideMap();
        //same map (refresh) — skip expensive teardown, just clear transient entity state. checksum must
        //also match: a server-side map edit reuses the same id but changes the bytes, so a mismatch means
        //the cached MapFile is stale and we have to take the full reload path. A map whose data timed out also takes the
        //full reload path, so a refresh asks for it again.
        if ((args.MapId == CurrentMapId) && (args.Checksum == CurrentMapCheckSum) && MapFile is not null && !MapDataIncomplete)
        {
            ClearTransientState();

            //re-evaluate darkness and weather only if the flag actually changed
            var newFlags = (MapFlags)args.Flags;

            if (newFlags != CurrentMapFlags)
            {
                CurrentMapFlags = newFlags;
                DarknessRenderer.OnMapChanged((short)args.MapId, CurrentMapFlags.HasFlag(MapFlags.Darkness));
                WeatherRenderer.OnMapChanged(CurrentMapFlags);
                UpdateWeatherAmbience();
            }

            UpdateHuds(HudOps.SetZoneName, args.Name);

            return;
        }

        //new map — dispose old caches, load fresh mapfile from local files
        TownMapControl.Hide();
        MapRenderer.Dispose();
        MapRenderer = new MapRenderer();

        MapFile = MapCache.TryLoad(
            AppPaths.MapsDir,
            args.MapId,
            args.Width,
            args.Height,
            args.Checksum);
        MapPreloaded = false;
        MapDataIncomplete = false;
        MapRows = null;
        CurrentMapId = (short)args.MapId;
        CurrentMapCheckSum = args.Checksum;
        CurrentMapFlags = (MapFlags)args.Flags;
        MapLoading.Show();

        //local file missing, corrupt, or checksum mismatch — request from server
        if (MapFile is null)
        {
            MapFile = new MapFile(args.Width, args.Height);
            InitializeEmptyTiles(MapFile);
            MapDataIdleMs = 0f;
            MapRows = new MapRowTracker(MapFile);
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
        DarknessRenderer.OnMapChanged((short)args.MapId, CurrentMapFlags.HasFlag(MapFlags.Darkness));
        WeatherRenderer.OnMapChanged(CurrentMapFlags);
        UpdateWeatherAmbience();

        UpdateHuds(HudOps.SetZoneName, args.Name);
        UpdateHuds(HudOps.ShowPersistentMessage, string.Empty);
    }

    //a rain map gets the rain bed; any other weather nibble stops it. StartAmbient does nothing for the id that
    //is already the bed, so a rain-to-rain change has no gap. the id resolves only from an ambient_sounds pack
    private void UpdateWeatherAmbience()
    {
        if (WeatherRenderer.IsRain(CurrentMapFlags))
            Game.SoundSystem.StartAmbient(RAIN_AMBIENT_ID);
        else
            Game.SoundSystem.StopAmbient();
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

    private void HandleMapData(MapDataPacket args)
    {
        //as on retail, a row is used only during a map-data wait: a row that arrives after the last row, after a timeout,
        //or for a map from the cache changes nothing
        if (MapRows is null || MapFile is null)
            return;

        MapDataIdleMs = 0f;

        var y = args.RowIndex;
        MapRows.Apply(y, args.RowData);

        if (!MapRows.IsLastRow(y))
            return;

        if (!MapRows.IsComplete)
        {
            EndMapDataIncomplete($"the last row arrived with {MapRows.ReceivedCount} of {MapFile.Height} rows complete");

            return;
        }

        if (MapRows.Problems is { } problems)
            NoticeDebugLog.Write($"[map] map {CurrentMapId} is complete, with rows that were not accepted: {problems}");

        MapRows = null;

        //a complete map that does not match the checksum still loads, and a refresh does not request it again: a server
        //map file with bytes after its tiles never matches, so an orange-bar message would show at each entry to that map
        if (MapCache.ToCacheBytes(MapFile, CurrentMapCheckSum) is { } bytes)
            MapCache.TrySave(AppPaths.MapsDir, CurrentMapId, bytes);
        else
            NoticeDebugLog.Write($"[map] map {CurrentMapId} does not match the checksum the server sent; it is used but not saved");

        FinalizeMapLoad();
    }

    /// <summary>
    ///     Ends a map-data wait that received no row for <see cref="MAP_DATA_TIMEOUT_MS" />.
    /// </summary>
    private void TickMapDataWatchdog(float elapsedMs)
    {
        if (MapRows is null)
            return;

        MapDataIdleMs += elapsedMs;

        if (MapDataIdleMs < MAP_DATA_TIMEOUT_MS)
            return;

        EndMapDataIncomplete($"map data stopped after {MapRows.ReceivedCount} of {MapFile?.Height} rows");
    }

    /// <summary>
    ///     Ends a map-data wait that did not give a complete map: the stream stopped, or the last row arrived with rows
    ///     missing or short (BRIG-113). The map loads with the rows that arrived and is not saved. A refresh or the next
    ///     entry to the map requests it again.
    /// </summary>
    private void EndMapDataIncomplete(string reason)
    {
        var problems = MapRows?.Problems is { } rows ? $"; rows not accepted: {rows}" : string.Empty;
        MapDataIncomplete = true;
        MapRows = null;

        NoticeDebugLog.Write($"[map] map {CurrentMapId} did not load completely: {reason}{problems}. The map loads with the rows that arrived.");
        var refreshKey = KeybindCatalog.Format(Keybinds.Effective(CommandId.World_Refresh).Primary);
        WorldState.Chat.AddOrangeBarMessage($"The map did not load completely. Press {refreshKey} to load it again.");
        FinalizeMapLoad();
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
                static id => DoorTable.GetVariants((ushort)id).Select(static v => (int)v));
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

        return ((int)tile.LeftForeground).IsRenderedTileIndex() || ((int)tile.RightForeground).IsRenderedTileIndex();
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
        if (DoorTable.GetClosedTileId((ushort)fgIndex).HasValue)
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
