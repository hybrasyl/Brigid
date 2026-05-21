#region
using System.IO.Compression;
using SkiaSharp;
#endregion

namespace Chaos.Client.Data.AssetPacks;

/// <summary>
///     An ability-icon asset pack backed by a <c>.datf</c> ZIP archive. Exposes per-ID lookup via
///     <see cref="TryGetIconImage" />. Filename convention: <c>{prefix}{spriteId:D4}.png</c> at the archive root
///     (e.g. <c>skill0001.png</c>, matching legacy EPF naming). Decoded <see cref="SKImage" /> results must be
///     disposed by the caller; typical pattern is to run them through
///     <c>Chaos.Client.Rendering.TextureConverter.ToTexture2D</c> and cache the resulting <c>Texture2D</c>.
/// </summary>
public sealed class IconPack : AssetPack
{
    internal IconPack(ZipArchive archive, AssetPackManifest manifest)
        : base(archive, manifest) { }

    /// <summary>
    ///     Attempts to decode the PNG for the given (prefix, spriteId) pair. Case-insensitive.
    /// </summary>
    /// <param name="prefix">Typically <c>"skill"</c> or <c>"spell"</c>.</param>
    /// <param name="spriteId">The 1-based sprite ID, matching the legacy EPF slot numbering.</param>
    /// <param name="image">Decoded image on success. Caller owns disposal.</param>
    /// <returns>True if the entry was found and decoded successfully.</returns>
    public bool TryGetIconImage(string prefix, int spriteId, out SKImage? image)
    {
        image = null;

        if (spriteId <= 0)
            return false;

        return TryGetImage($"{prefix}{spriteId:D4}.png", out image);
    }
}
