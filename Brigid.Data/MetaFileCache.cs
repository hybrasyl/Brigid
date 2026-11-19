#region
using System.IO.Compression;
using DALib.Cryptography;
#endregion

namespace Brigid.Data;

/// <summary>
///     The local cache of server metafiles in <see cref="AppPaths.MetaFileDir" />. The server names each metafile, and
///     the client stores it under that name, so <see cref="GetChecksum" /> and <see cref="TryWrite" /> refuse a name
///     that <see cref="FileNames.IsSafeName" /> refuses, and <see cref="MetaFileSync" /> drops such a name before
///     <see cref="PruneUnlisted" />. A name that is not a bare file name lets a server write outside the directory.
/// </summary>
public static class MetaFileCache
{
    /// <summary>
    ///     Returns the CRC-32 of the decompressed metafile. Returns null when the name is refused, or the file is absent,
    ///     unreadable or not valid zlib data. Null never equals a listed checksum, so the sync downloads the file again; a
    ///     CRC-32 can be 0 (BRIG-166).
    /// </summary>
    public static uint? GetChecksum(string directory, string name)
    {
        if (!FileNames.IsSafeName(name))
            return null;

        var path = Path.Combine(directory, name);

        if (!File.Exists(path))
            return null;

        try
        {
            using var file = File.OpenRead(path);

            return GetDecompressedChecksum(file);
        }
        //rule 6 exemption: a corrupt or locked cache file reads as absent, and the sync downloads it again
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Returns true when the CRC-32 of the decompressed payload is <paramref name="checksum" />. An empty payload, or one
    ///     that is not valid zlib data, is not valid.
    /// </summary>
    /// <remarks>Retail decompresses the payload, compares its CRC-32 with the checksum in the same packet, and requests the
    /// file again on a mismatch.</remarks>
    public static bool IsValidPayload(byte[] payload, uint checksum) => (payload.Length > 0) && (GetPayloadChecksum(payload) == checksum);

    //null when the payload is not valid zlib data, or holds no data
    private static uint? GetPayloadChecksum(byte[] payload)
    {
        try
        {
            using var stream = new MemoryStream(payload, false);

            return GetDecompressedChecksum(stream);
        }
        //rule 6 exemption: zlib reports bad data only by an exception, and bad data is a payload to request again
        catch (InvalidDataException)
        {
            return null;
        }
    }

    //null when the data decompresses to nothing: data that is not valid can end with no error and no bytes, and the
    //CRC-32 of no bytes is 0. A metafile holds at least its entry count.
    private static uint? GetDecompressedChecksum(Stream compressed)
    {
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var data = new MemoryStream();

        zlib.CopyTo(data);

        //retail + Hybrasyl both send standard (inverted) CRC-32 — verified against the retail checksum routine
        return data.Length > 0 ? CRC32.Calculate(data.ToArray()) : null;
    }

    /// <summary>Writes a metafile, and makes the directory when it is not there. Returns false when the name is refused or the write fails.</summary>
    public static bool TryWrite(string directory, string name, byte[] data)
        => FileNames.IsSafeName(name)
           && DataDiagnostics.Try(
               () =>
               {
                   Directory.CreateDirectory(directory);
                   File.WriteAllBytes(Path.Combine(directory, name), data);
               }, $"write metafile {name}");

    /// <summary>
    ///     Deletes each file in <paramref name="directory" /> whose name is not in <paramref name="listedNames" />, and
    ///     returns the names it deleted. Subdirectories are not touched. A directory that cannot be read deletes nothing.
    /// </summary>
    /// <remarks>
    ///     A metafile that the server no longer lists is stale. The repository reads only the listed files (BRIG-167), but
    ///     a stale file on disk is read again before the first list of the next session. The server splits item metadata
    ///     across <c>ItemInfoN</c> files and the split moves when items change, so an old file can give an item its old
    ///     class or level. The comparison here is ordinal, but the client's pending set ignores case. So on a
    ///     case-insensitive file system, this method deletes a file whose name changed case, and the sync downloads it
    ///     again.
    /// </remarks>
    public static IReadOnlyList<string> PruneUnlisted(string directory, IEnumerable<string> listedNames)
    {
        if (!Directory.Exists(directory))
            return [];

        string[] files = [];

        if (!DataDiagnostics.Try(() => files = Directory.GetFiles(directory), "list metafiles"))
            return [];

        var listed = new HashSet<string>(listedNames, StringComparer.Ordinal);
        var deleted = new List<string>();

        foreach (var path in files)
        {
            var name = Path.GetFileName(path);

            if (listed.Contains(name))
                continue;

            if (DataDiagnostics.Try(() => File.Delete(path), $"delete stale metafile {name}"))
                deleted.Add(name);
        }

        return deleted;
    }
}
