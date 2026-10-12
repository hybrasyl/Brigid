#region
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using DALib.Cryptography;
#endregion

namespace Brigid.Data;

/// <summary>
///     The local cache of server metafiles in <see cref="AppPaths.MetaFileDir" />. The server names each metafile, and
///     the client stores it under that name, so every method here refuses a name that <see cref="IsSafeName" />
///     refuses. A name that is not a bare file name lets a server write outside the directory.
/// </summary>
public static class MetaFileCache
{
    //the characters Windows refuses in a file name, including both separators. They are refused on every platform, so
    //a name that one platform accepts, every platform accepts.
    private static readonly char[] InvalidChars = ['"', '<', '>', '|', ':', '*', '?', '\\', '/', .. Enumerable.Range(0, 32).Select(i => (char)i)];

    //Windows opens a device, not a file, for these names, with or without an extension
    private static readonly FrozenSet<string> ReservedNames = new[]
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Returns true only for a bare file name that names the same file on every platform: not empty, no separator, no
    ///     character that Windows refuses, not rooted, no trailing dot or space, and not a Windows device name.
    /// </summary>
    public static bool IsSafeName([NotNullWhen(true)] string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || (name.IndexOfAny(InvalidChars) >= 0))
            return false;

        //Windows removes a trailing dot or space, so the file on disk gets another name. This also refuses "." and "..".
        if (name.EndsWith('.') || name.EndsWith(' '))
            return false;

        if (Path.IsPathRooted(name) || (Path.GetFileName(name) != name))
            return false;

        return !ReservedNames.Contains(name.Split('.')[0]);
    }

    /// <summary>
    ///     Returns the CRC-32 of the decompressed metafile. Returns 0 when the name is refused, or the file is absent,
    ///     unreadable or not valid zlib data. A 0 makes the sync download the file again.
    /// </summary>
    public static uint GetChecksum(string directory, string name)
    {
        if (!IsSafeName(name))
            return 0;

        var path = Path.Combine(directory, name);

        if (!File.Exists(path))
            return 0;

        try
        {
            using var file = File.OpenRead(path);
            using var zlib = new ZLibStream(file, CompressionMode.Decompress);
            using var data = new MemoryStream();

            zlib.CopyTo(data);

            //retail + Hybrasyl both send standard (inverted) CRC-32 — verified against the retail checksum routine
            return CRC32.Calculate(data.ToArray());
        }
        //rule 6 exemption: a corrupt or locked cache file reads as absent, and the sync downloads it again
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return 0;
        }
    }

    /// <summary>Writes a metafile. Returns false when the name is refused or the write fails.</summary>
    public static bool TryWrite(string directory, string name, byte[] data)
        => IsSafeName(name) && DataDiagnostics.Try(() => File.WriteAllBytes(Path.Combine(directory, name), data), $"write metafile {name}");

    /// <summary>
    ///     Deletes each file in <paramref name="directory" /> whose name is not in <paramref name="listedNames" />, and
    ///     returns the names it deleted. Subdirectories are not touched. A directory that cannot be read deletes nothing.
    /// </summary>
    /// <remarks>
    ///     A metafile that the server no longer lists is stale, but the repository still reads it. The server splits item
    ///     metadata across <c>ItemInfoN</c> files and the split moves when items change, so an old file can give an item
    ///     its old class or level. The comparison here is ordinal, but the client's pending set ignores case. So on a
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
