#region
using DALib.Data;
using DALib.Networking.Wire;
#endregion

namespace Brigid.Data;

/// <summary>
///     The local cache of server-sent maps (the client uses <see cref="AppPaths.MapsDir" />), one <c>lod{mapId}.map</c>
///     file for each map. A file holds <c>width * height</c> tiles, rows first, each tile three little-endian u16 values
///     (background, left foreground, right foreground). The server's MapInfo gives the size and the CRC-CCITT of the file,
///     so a cached map is used only when both match.
/// </summary>
public static class MapCache
{
    private const int TILE_BYTES = 6;

    private static string PathFor(string directory, int mapId) => Path.Combine(directory, $"lod{mapId}.map");

    /// <summary>
    ///     Returns the cached map, or null when the file is absent or unreadable, or its size or CRC-CCITT is not the one
    ///     the server sent. A null makes the client request the map.
    /// </summary>
    public static MapFile? TryLoad(string directory, int mapId, int width, int height, ushort checksum)
    {
        var path = PathFor(directory, mapId);

        if (!File.Exists(path))
            return null;

        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(path);
        }
        //rule 6 exemption: a locked or unreadable cache file reads as absent, and the client requests the map again
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if ((bytes.Length != (width * height * TILE_BYTES)) || (CrcCcitt.Compute(bytes) != checksum))
            return null;

        var map = new MapFile(width, height);
        var index = 0;

        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                map.Tiles[x, y] = new MapTile
                {
                    Background = (ushort)(bytes[index] | (bytes[index + 1] << 8)),
                    LeftForeground = (ushort)(bytes[index + 2] | (bytes[index + 3] << 8)),
                    RightForeground = (ushort)(bytes[index + 4] | (bytes[index + 5] << 8))
                };
                index += TILE_BYTES;
            }

        return map;
    }

    /// <summary>
    ///     Returns the map in the cache file format, or null when its CRC-CCITT is not <paramref name="checksum" />.
    ///     <see cref="TryLoad" /> refuses a file with another checksum, so there is no reason to save one.
    /// </summary>
    public static byte[]? ToCacheBytes(MapFile map, ushort checksum)
    {
        using var buffer = new MemoryStream(map.Width * map.Height * TILE_BYTES);
        map.Save(buffer);
        var bytes = buffer.ToArray();

        return CrcCcitt.Compute(bytes) == checksum ? bytes : null;
    }

    /// <summary>Writes the cache file of a map. Returns false when the write fails.</summary>
    public static bool TrySave(string directory, int mapId, byte[] bytes)
    {
        var path = PathFor(directory, mapId);

        return DataDiagnostics.Try(
            () =>
            {
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(path, bytes);
            }, $"save map {mapId}");
    }
}
