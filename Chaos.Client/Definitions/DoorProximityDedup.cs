namespace Chaos.Client.Definitions;

/// <summary>
///     Collapses a proximity-sorted list of door-tile hits into one entry per connected door. Real doors
///     span 1–4 tiles along a single axis (see <see cref="DoorTable" /> and docs/doors.md), so a 5x5 tile
///     scan around the cursor can match every panel of a multi-tile door. 4-neighbor adjacency merges
///     those panels while keeping distinct doors separate — diagonal tiles are treated as separate doors.
/// </summary>
public static class DoorProximityDedup
{
    public readonly record struct DoorHit<TPayload>(int DistanceSq, int TileX, int TileY, TPayload Payload);

    /// <summary>
    ///     Input must be sorted ascending by <see cref="DoorHit{T}.DistanceSq" />. Returns a new list where each
    ///     tile is kept only if no already-kept tile is its 4-neighbor (Manhattan distance of 1). Sort order is
    ///     preserved, so callers can safely truncate the tail (e.g. to MAX_ENTRIES).
    /// </summary>
    public static List<DoorHit<TPayload>> CollapseAdjacent<TPayload>(IReadOnlyList<DoorHit<TPayload>> sortedHits)
    {
        var kept = new List<DoorHit<TPayload>>(sortedHits.Count);

        //all tiles we've processed so far (kept entries + tiles merged into them). a new tile is a
        //separate door only if it is not 4-adjacent to ANY of these — otherwise it joins an existing
        //cluster transitively (handles 3- and 4-tile strips where the newest tile only touches the
        //previously merged tile, not the originally kept one).
        var claimed = new List<(int X, int Y)>(sortedHits.Count);

        foreach (var hit in sortedHits)
        {
            var merged = false;

            for (var i = 0; i < claimed.Count; i++)
            {
                var other = claimed[i];
                var dx = Math.Abs(hit.TileX - other.X);
                var dy = Math.Abs(hit.TileY - other.Y);

                if ((dx + dy) == 1)
                {
                    merged = true;

                    break;
                }
            }

            claimed.Add((hit.TileX, hit.TileY));

            if (!merged)
                kept.Add(hit);
        }

        return kept;
    }
}
