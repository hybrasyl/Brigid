#region
using System.IO.Compression;
using SkiaSharp;
#endregion

namespace Brigid.Data.AssetPacks;

/// <summary>
///     Common base for typed <c>.datf</c> asset packs. Owns the backing <see cref="ZipArchive" />, the
///     case-insensitive entry index, the manifest, and the shared PNG decode helper. Subclasses add their
///     type-specific naming convention (e.g. <c>item{id:D5}.png</c>, <c>skill{id:D4}.png</c>) and any per-type
///     metadata extracted from the manifest.
/// </summary>
public abstract class AssetPack : IAssetPack
{
    private readonly ZipArchive Archive;
    private readonly Dictionary<string, ZipArchiveEntry> EntryIndex;

    /// <inheritdoc />
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
    ///     Cheap presence check — dictionary lookup only, no decode. Used by callers that need to make a routing
    ///     decision (e.g. cache-key normalization) before paying decode cost.
    /// </summary>
    protected bool HasEntry(string entryName) => EntryIndex.ContainsKey(entryName);

    /// <summary>
    ///     The full names of every entry in the archive. Lets subclasses with an extension-agnostic naming convention
    ///     (e.g. audio packs keyed by an integer id where the file extension varies) build their own lookup index.
    /// </summary>
    protected IReadOnlyCollection<string> EntryNames => EntryIndex.Keys;

    /// <summary>
    ///     Reads the raw, undecoded bytes of the entry with the given name. Swallows read failures (treats them as
    ///     "not present") so callers can fall back to legacy content cleanly. For non-image content (e.g. audio handed
    ///     straight to SDL_mixer) where <see cref="TryGetImage" />'s PNG decode does not apply.
    /// </summary>
    protected bool TryGetEntryBytes(string entryName, out byte[]? bytes)
    {
        bytes = null;

        if (!EntryIndex.TryGetValue(entryName, out var entry))
            return false;

        try
        {
            using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            bytes = ms.ToArray();

            return true;
        }
        catch
        {
            bytes = null;

            return false;
        }
    }

    /// <summary>
    ///     Decodes the PNG entry with the given name. Swallows decode failures (treats corrupt or non-PNG entries as
    ///     "not present") so the caller can fall back to legacy art cleanly. Caller owns disposal of the returned
    ///     image.
    /// </summary>
    protected bool TryGetImage(string entryName, out SKImage? image)
    {
        image = null;

        //share the single entry-read-and-swallow path; SKImage.FromEncodedData returns null on non-PNG/corrupt
        //data, so a decode failure is reported as "not present" just like a read failure
        if (!TryGetEntryBytes(entryName, out var bytes) || bytes is null)
            return false;

        image = SKImage.FromEncodedData(bytes);

        return image is not null;
    }

    public virtual void Dispose() => Archive.Dispose();
}
