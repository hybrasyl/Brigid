#region
using System.IO.Compression;
using SkiaSharp;
#endregion

namespace Chaos.Client.Data.AssetPacks;

/// <summary>
///     A static-tile asset pack backed by a <c>.datf</c> ZIP archive. Exposes per-tile-ID lookup for two namespaces:
///     <see cref="TryGetFloorImage" /> for background (floor) tiles and <see cref="TryGetWallImage" /> for
///     foreground (wall) tiles. Filename conventions: <c>floor{tileId:D5}.png</c> and <c>wall{tileId:D5}.png</c> at the
///     archive root, where <c>tileId</c> is the value stored in <c>MapTile.Background</c> / <c>LeftForeground</c> /
///     <c>RightForeground</c> directly (no offset).
///     "Static" excludes palette-cycled and frame-animated tiles — the consuming renderer is expected to skip pack
///     lookup for those IDs (cycling overlays would visually overwrite the pack PNG; partial coverage of an animation
///     would produce mixed-frame glitches). Decoded <see cref="SKImage" /> results must be disposed by the caller —
///     typically by the renderer's image cache.
/// </summary>
public sealed class StaticTilePack : IDisposable
{
    private readonly ZipArchive Archive;
    private readonly Dictionary<string, ZipArchiveEntry> EntryIndex;

    public AssetPackManifest Manifest { get; }

    internal StaticTilePack(ZipArchive archive, AssetPackManifest manifest)
    {
        Archive = archive;
        Manifest = manifest;

        EntryIndex = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
            EntryIndex[entry.FullName] = entry;
    }

    /// <summary>
    ///     Attempts to decode the PNG for the given background (floor) tile ID. Returns false if the entry isn't
    ///     present, decode fails, or the entry is malformed — caller falls back to legacy tileset.
    /// </summary>
    public bool TryGetFloorImage(int tileId, out SKImage? image) => TryGetImage($"floor{tileId:D5}.png", out image);

    /// <summary>
    ///     Attempts to decode the PNG for the given foreground (wall) tile ID. Returns false if the entry isn't
    ///     present, decode fails, or the entry is malformed — caller falls back to legacy <c>stc{tileId:D5}.hpf</c>.
    /// </summary>
    public bool TryGetWallImage(int tileId, out SKImage? image) => TryGetImage($"wall{tileId:D5}.png", out image);

    private bool TryGetImage(string name, out SKImage? image)
    {
        image = null;

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
