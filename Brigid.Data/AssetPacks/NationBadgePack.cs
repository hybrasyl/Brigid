#region
using System.IO.Compression;
using SkiaSharp;
#endregion

namespace Brigid.Data.AssetPacks;

/// <summary>
///     A nation-badge asset pack backed by a <c>.datf</c> ZIP archive. Exposes per-nation lookup via
///     <see cref="TryGetBadgeImage" />. Filename convention: <c>nation{nationId:D4}.png</c> at the archive root
///     (1-based, matching the legacy <c>_nui_nat.spf</c> frame-index-plus-one convention). Decoded
///     <see cref="SKImage" /> results must be disposed by the caller.
/// </summary>
public sealed class NationBadgePack : AssetPack
{
    internal NationBadgePack(ZipArchive archive, AssetPackManifest manifest)
        : base(archive, manifest) { }

    /// <summary>
    ///     Attempts to decode the PNG for the given nation ID. Returns false if the entry isn't present, decode
    ///     fails, or the entry is malformed — caller should fall back to the legacy <c>_nui_nat.spf</c> frame.
    /// </summary>
    public bool TryGetBadgeImage(byte nationId, out SKImage? image)
    {
        image = null;

        if (nationId == 0)
            return false;

        return TryGetImage($"nation{nationId:D4}.png", out image);
    }
}
