#region
using System.IO.Compression;
using SkiaSharp;
#endregion

namespace Chaos.Client.Data.AssetPacks;

/// <summary>
///     A legend-mark-icon asset pack backed by a <c>.datf</c> ZIP archive. Exposes per-icon lookup via
///     <see cref="TryGetLegendMarkImage" />. Filename convention: <c>legend{iconId:D4}.png</c> at the archive root.
///     Unlike <see cref="IconPack" /> and <see cref="NationBadgePack" /> which are 1-based, legend mark IDs are
///     <b>0-based</b> — the byte in the server's legend packet is used directly as the frame index into legacy
///     <c>legends.epf</c>, so <c>legend0000.png</c> replaces EPF frame 0 and corresponds to server icon ID 0.
///     Decoded <see cref="SKImage" /> results must be disposed by the caller.
/// </summary>
public sealed class LegendMarkIconPack : IDisposable
{
    private readonly ZipArchive Archive;
    private readonly Dictionary<string, ZipArchiveEntry> EntryIndex;

    public AssetPackManifest Manifest { get; }

    internal LegendMarkIconPack(ZipArchive archive, AssetPackManifest manifest)
    {
        Archive = archive;
        Manifest = manifest;

        EntryIndex = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
            EntryIndex[entry.FullName] = entry;
    }

    /// <summary>
    ///     Attempts to decode the PNG for the given legend mark icon ID. Returns false if the entry isn't present,
    ///     decode fails, or the entry is malformed — caller should fall back to the legacy <c>legends.epf</c> frame.
    /// </summary>
    public bool TryGetLegendMarkImage(byte iconId, out SKImage? image)
    {
        image = null;

        var name = $"legend{iconId:D4}.png";

        if (!EntryIndex.TryGetValue(name, out var entry))
            return false;

        try
        {
            using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            ms.Position = 0;
            image = SKImage.FromEncodedData(ms);

            return image is not null;
        }
        catch
        {
            image?.Dispose();
            image = null;

            return false;
        }
    }

    public void Dispose() => Archive.Dispose();
}
