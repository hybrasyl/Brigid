#region
using System.IO.Compression;
using SkiaSharp;
#endregion

namespace Chaos.Client.Data.AssetPacks;

/// <summary>
///     Common base for typed <c>.datf</c> asset packs. Owns the backing <see cref="ZipArchive" />, the
///     case-insensitive entry index, the manifest, and the shared PNG decode helper. Subclasses add their type-specific
///     naming convention (e.g. <c>item{id:D5}.png</c>, <c>skill{id:D4}.png</c>) and any per-type metadata extracted from
///     the manifest.
/// </summary>
public abstract class AssetPack : IDisposable
{
    private readonly ZipArchive Archive;
    private readonly Dictionary<string, ZipArchiveEntry> EntryIndex;

    public AssetPackManifest Manifest { get; }

    protected AssetPack(ZipArchive archive, AssetPackManifest manifest)
    {
        Archive = archive;
        Manifest = manifest;

        //pre-index entries by full name for case-insensitive, O(1) lookup; subclasses query via HasEntry / TryGetImage
        EntryIndex = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
            EntryIndex[entry.FullName] = entry;
    }

    /// <summary>
    ///     Cheap presence check — dictionary lookup only, no decode. Used by callers that need to make a routing decision
    ///     (e.g. cache-key normalization) before paying decode cost.
    /// </summary>
    protected bool HasEntry(string entryName) => EntryIndex.ContainsKey(entryName);

    /// <summary>
    ///     Decodes the PNG entry with the given name. Swallows decode failures (treats corrupt or non-PNG entries as
    ///     "not present") so the caller can fall back to legacy art cleanly. Caller owns disposal of the returned image.
    /// </summary>
    protected bool TryGetImage(string entryName, out SKImage? image)
    {
        image = null;

        if (!EntryIndex.TryGetValue(entryName, out var entry))
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

    public virtual void Dispose() => Archive.Dispose();
}
