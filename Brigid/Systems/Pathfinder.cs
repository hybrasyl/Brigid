#region
using Chaos.Geometry;
using Chaos.Geometry.Abstractions;
using Chaos.Geometry.Abstractions.Definitions;
using Chaos.Pathfinding;
#endregion

namespace Brigid.Systems;

/// <summary>
///     Pure path computation: finds A* paths to tiles or entities. Returns results without mutating any state.
/// </summary>
public static class Pathfinder
{
    /// <summary>
    ///     Returns the cardinal direction from one tile to an adjacent tile, or null if not adjacent.
    /// </summary>
    public static Direction? DirectionToward(
        int fromX,
        int fromY,
        int toX,
        int toY)
        => (toX - fromX, toY - fromY) switch
        {
            (0, -1) => Direction.Up,
            (1, 0)  => Direction.Right,
            (0, 1)  => Direction.Down,
            (-1, 0) => Direction.Left,
            _       => null
        };

    /// <summary>
    ///     Finds an A* path from the player to the best adjacent tile around the target entity. Returns null if no path exists
    ///     or if already adjacent. Sets <paramref name="alreadyAdjacent" /> to true if the player is already next to the
    ///     target. Map dimensions are required because the external pathfinder does not bounds-check start/end and throws
    ///     <see cref="IndexOutOfRangeException" /> if either coordinate falls outside the grid. The external A* also
    ///     unconditionally accepts the end node even if it's a wall or blocked, so <paramref name="isTileWalkable" /> is
    ///     required to filter candidate adjacent tiles — pass null to skip filtering (GM case).
    /// </summary>
    public static Stack<IPoint>? FindPathToEntity(
        Chaos.Pathfinding.Pathfinder pathfinder,
        int fromX,
        int fromY,
        int targetX,
        int targetY,
        int mapWidth,
        int mapHeight,
        IReadOnlyCollection<IPoint> blockedPoints,
        bool ignoreWalls,
        Func<int, int, bool>? isTileWalkable,
        out bool alreadyAdjacent)
    {
        alreadyAdjacent = false;

        if (!IsInGrid(
                fromX,
                fromY,
                mapWidth,
                mapHeight))
            return null;

        if (IsAdjacent(
                fromX,
                fromY,
                targetX,
                targetY))
        {
            alreadyAdjacent = true;

            return null;
        }

        Stack<IPoint>? bestPath = null;

        ReadOnlySpan<(int Dx, int Dy)> adjacentOffsets =
        [
            (0, -1),
            (1, 0),
            (0, 1),
            (-1, 0)
        ];

        foreach ((var dx, var dy) in adjacentOffsets)
        {
            var adjX = targetX + dx;
            var adjY = targetY + dy;

            if (!IsInGrid(
                    adjX,
                    adjY,
                    mapWidth,
                    mapHeight))
                continue;

            if ((adjX == fromX) && (adjY == fromY))
            {
                alreadyAdjacent = true;

                return null;
            }

            //external A* accepts any end tile even if it's a wall or blocked, so filter candidate adjacents
            //here. without this, an adjacent wall tile becomes the shortest "path" and the player walks into it.
            if ((isTileWalkable is not null) && !isTileWalkable(adjX, adjY))
                continue;

            var path = pathfinder.FindPath(
                new Point(fromX, fromY),
                new Point(adjX, adjY),
                new PathOptions
                {
                    BlockedPoints = blockedPoints,
                    IgnoreWalls = ignoreWalls,
                    LimitRadius = null
                });

            if ((path.Count > 0) && (bestPath is null || (path.Count < bestPath.Count)))
                bestPath = path;
        }

        return bestPath;
    }

    /// <summary>
    ///     Finds an A* path from the player to the target tile. Returns null if no path exists. Map dimensions are required
    ///     because the external pathfinder does not bounds-check start/end and throws
    ///     <see cref="IndexOutOfRangeException" /> if either coordinate falls outside the grid.
    /// </summary>
    public static Stack<IPoint>? FindPathToTile(
        Chaos.Pathfinding.Pathfinder pathfinder,
        int fromX,
        int fromY,
        int toX,
        int toY,
        int mapWidth,
        int mapHeight,
        IReadOnlyCollection<IPoint> blockedPoints,
        bool ignoreWalls)
    {
        if (!IsInGrid(
                fromX,
                fromY,
                mapWidth,
                mapHeight)
            || !IsInGrid(
                toX,
                toY,
                mapWidth,
                mapHeight))
            return null;

        var path = pathfinder.FindPath(
            new Point(fromX, fromY),
            new Point(toX, toY),
            new PathOptions
            {
                BlockedPoints = blockedPoints,
                IgnoreWalls = ignoreWalls,
                LimitRadius = null
            });

        return path.Count > 0 ? path : null;
    }

    private static bool IsInGrid(int x, int y, int width, int height)
        => (x >= 0) && (x < width) && (y >= 0) && (y < height);

    /// <summary>
    ///     Returns true if two tile positions are cardinally adjacent (Manhattan distance == 1).
    /// </summary>
    public static bool IsAdjacent(
        int x1,
        int y1,
        int x2,
        int y2)
        => (Math.Abs(x1 - x2) + Math.Abs(y1 - y2)) == 1;
}