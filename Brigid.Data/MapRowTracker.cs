#region
using System.Collections;
using DALib.Data;
#endregion

namespace Brigid.Data;

/// <summary>What <see cref="MapRowTracker.Apply" /> did with one row of a map that the server streams.</summary>
public enum MapRowResult
{
    /// <summary>A new row with a tile for each column of the map.</summary>
    Accepted,

    /// <summary>
    ///     A row with fewer bytes than the map's width needs. Its tiles are written, but the row is not received, also when
    ///     a full copy of it arrived before.
    /// </summary>
    Short,

    /// <summary>A row that was already received. Its tiles are written again.</summary>
    Duplicate,

    /// <summary>A row index at or past the map's height. Nothing is written.</summary>
    OutOfRange
}

/// <summary>Where a map stream stands after a row (<see cref="MapRowTracker.Receive" />).</summary>
public enum MapStreamStatus
{
    /// <summary>The row with the last index has not arrived.</summary>
    Open,

    /// <summary>The row with the last index arrived, and every row arrived with its full width.</summary>
    Complete,

    /// <summary>The row with the last index arrived, and a row is missing or short.</summary>
    Incomplete
}

/// <summary>
///     Writes the rows of a map that the server streams (0x3C, one row in each packet) into a <see cref="MapFile" />, and
///     records which rows arrived with their full width (BRIG-113). Retail completes a map on the row with the last index
///     and saves it, whatever arrived before. Brigid saves a map only when <see cref="IsComplete" /> is true.
/// </summary>
public sealed class MapRowTracker(MapFile map)
{
    //each tile is 6 bytes: the background, the left foreground and the right foreground, each a big-endian u16
    private const int TILE_BYTES = 6;

    private readonly BitArray Received = new(map.Height);

    /// <summary>The number of rows that arrived with their full width, each row counted once.</summary>
    public int ReceivedCount { get; private set; }

    /// <summary>True when every row arrived with its full width.</summary>
    public bool IsComplete => ReceivedCount == map.Height;

    /// <summary>True for the row with the last index. Retail completes a map on this row, so a server sends it last.</summary>
    public bool IsLastRow(int rowIndex) => rowIndex == (map.Height - 1);

    /// <summary>The number of rows that <see cref="Apply" /> did not accept, for each result.</summary>
    public int ShortCount { get; private set; }

    /// <inheritdoc cref="ShortCount" />
    public int DuplicateCount { get; private set; }

    /// <inheritdoc cref="ShortCount" />
    public int OutOfRangeCount { get; private set; }

    /// <summary>
    ///     The rows that were not accepted, for example "2 short, 1 duplicate", or null when every row was accepted.
    /// </summary>
    public string? Problems
    {
        get
        {
            var parts = new List<string>(3);

            if (ShortCount > 0)
                parts.Add($"{ShortCount} short");

            if (DuplicateCount > 0)
                parts.Add($"{DuplicateCount} duplicate");

            if (OutOfRangeCount > 0)
                parts.Add($"{OutOfRangeCount} out of range");

            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }
    }

    /// <summary>
    ///     Writes one row (<see cref="Apply" />) and returns where the stream stands. The row with the last index ends the
    ///     stream, as on retail: <see cref="MapStreamStatus.Complete" /> when every row arrived with its full width, else
    ///     <see cref="MapStreamStatus.Incomplete" />. A row after that changes the map but not the result.
    /// </summary>
    public MapStreamStatus Receive(int rowIndex, ReadOnlySpan<byte> rowData)
    {
        Apply(rowIndex, rowData);

        if (!IsLastRow(rowIndex))
            return MapStreamStatus.Open;

        return IsComplete ? MapStreamStatus.Complete : MapStreamStatus.Incomplete;
    }

    /// <summary>
    ///     Writes the tiles of one row into the map. Bytes after the map's width are ignored, as on retail.
    /// </summary>
    public MapRowResult Apply(int rowIndex, ReadOnlySpan<byte> rowData)
    {
        if ((rowIndex < 0) || (rowIndex >= map.Height))
        {
            OutOfRangeCount++;

            return MapRowResult.OutOfRange;
        }

        var tileCount = Math.Min(rowData.Length / TILE_BYTES, map.Width);

        for (var x = 0; x < tileCount; x++)
        {
            var tile = rowData.Slice(x * TILE_BYTES, TILE_BYTES);

            map.Tiles[x, rowIndex] = new MapTile
            {
                Background = (ushort)((tile[0] << 8) | tile[1]),
                LeftForeground = (ushort)((tile[2] << 8) | tile[3]),
                RightForeground = (ushort)((tile[4] << 8) | tile[5])
            };
        }

        if (tileCount < map.Width)
        {
            //the short copy wrote over part of the row, so a full copy that arrived before is no longer what the map holds
            if (Received[rowIndex])
            {
                Received[rowIndex] = false;
                ReceivedCount--;
            }

            ShortCount++;

            return MapRowResult.Short;
        }

        if (Received[rowIndex])
        {
            DuplicateCount++;

            return MapRowResult.Duplicate;
        }

        Received[rowIndex] = true;
        ReceivedCount++;

        return MapRowResult.Accepted;
    }
}
