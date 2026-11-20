using System.IO.Compression;
using Brigid.Data;
using DALib.Data;

namespace Brigid.Tests;

/// <summary>The helpers of the metafile tests: a compressed payload, a metafile on disk and the time limit of the sync.</summary>
internal static class MetaFileTestData
{
    /// <summary>The time limit of <see cref="MetaFileSync" />, in milliseconds of a <see cref="ManualClock" />.</summary>
    public const long TIMEOUT_MS = MetaFileSync.REQUEST_TIMEOUT_SECONDS * 1000;

    /// <summary>The zlib data of <paramref name="data" />, as the server sends a metafile.</summary>
    public static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();

        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
            zlib.Write(data);

        return output.ToArray();
    }

    /// <summary>Writes a metafile with <paramref name="entries" /> to <paramref name="root" />.</summary>
    public static void WriteMetaFile(string root, string name, params MetaFileEntry[] entries)
    {
        var file = new MetaFile();

        foreach (var entry in entries)
            file.Add(entry);

        file.Save(Path.Combine(root, name));
    }
}
