#region
using System.IO.Compression;
using SkiaSharp;
#endregion

namespace Chaos.Client.Data.AssetPacks;

/// <summary>
///     A generic UI-sprite-override asset pack backed by a <c>.datf</c> ZIP archive. Exposes per-frame lookup via
///     <see cref="TryGetSprite" /> against legacy EPF/SPF file names from setoa/cious. Layout convention:
///     <c>{filename-with-extension-lowercased}/{frameIndex:D4}.png</c> — e.g. <c>butt001.epf/0000.png</c> for OK button
///     frame 0, <c>dlgback2.spf/0000.png</c> for the dialog background tile. Frames need not be contiguous; missing
///     frames fall through to the legacy art. Decoded <see cref="SKImage" /> results must be disposed by the caller.
/// </summary>
public sealed class UiSpriteOverridePack : IDisposable
{
    private readonly ZipArchive Archive;
    private readonly Dictionary<string, ZipArchiveEntry> EntryIndex;

    public AssetPackManifest Manifest { get; }

    internal UiSpriteOverridePack(ZipArchive archive, AssetPackManifest manifest)
    {
        Archive = archive;
        Manifest = manifest;

        EntryIndex = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
            EntryIndex[entry.FullName] = entry;
    }

    /// <summary>
    ///     Attempts to decode the override PNG for the given source asset name and frame index. Returns false if no
    ///     override entry is present, decode fails, or the entry is malformed — caller should fall back to the legacy
    ///     EPF/SPF frame.
    /// </summary>
    public bool TryGetSprite(string fileName, int frameIndex, out SKImage? image)
    {
        image = null;

        if (string.IsNullOrEmpty(fileName) || (frameIndex < 0))
            return false;

        var key = $"{fileName.ToLowerInvariant()}/{frameIndex:D4}.png";

        if (!EntryIndex.TryGetValue(key, out var entry))
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
