#region
using System.IO.Compression;
using SkiaSharp;
#endregion

namespace Chaos.Client.Data.AssetPacks;

/// <summary>
///     An item-icon asset pack backed by a <c>.datf</c> ZIP archive. Exposes per-ID lookup via
///     <see cref="TryGetItemImage" /> and a manifest-driven dye-eligibility check via <see cref="IsDyeable" />.
///     Filename convention: <c>item{spriteId:D5}.png</c> at the archive root (e.g. <c>item00001.png</c>,
///     <c>item13688.png</c>) — 5-digit zero-padded, 1-based. Decoded <see cref="SKImage" /> results must be disposed
///     by the caller; typical pattern is to run them through
///     <c>Chaos.Client.Rendering.TextureConverter.ToTexture2D</c> and cache the resulting <c>Texture2D</c>.
/// </summary>
public sealed class ItemPack : AssetPack
{
    private readonly HashSet<int> DyeableSet;

    internal ItemPack(ZipArchive archive, AssetPackManifest manifest)
        : base(archive, manifest)
    {
        //fail closed: absent covers entry or null Dyeable list means no item is treated as dyeable
        var dyeable = manifest.Covers.TryGetValue("item_icons", out var coverage) ? coverage.Dyeable : null;
        DyeableSet = dyeable is null ? new HashSet<int>() : new HashSet<int>(dyeable);
    }

    /// <summary>
    ///     Returns true if this pack contains a PNG entry for the given <paramref name="spriteId" />. Cheap (dictionary
    ///     lookup, no decode); intended for the renderer's pre-decode cache-key normalization path.
    /// </summary>
    public bool HasItem(int spriteId)
    {
        if (spriteId <= 0)
            return false;

        return HasEntry($"item{spriteId:D5}.png");
    }

    /// <summary>
    ///     Returns true if the manifest's <c>covers.item_icons.dyeable</c> list includes <paramref name="spriteId" />.
    ///     The renderer uses this to decide whether to run the find-and-replace dye pass or skip it.
    /// </summary>
    public bool IsDyeable(int spriteId) => DyeableSet.Contains(spriteId);

    /// <summary>
    ///     Attempts to decode the PNG for the given <paramref name="spriteId" />. Case-insensitive.
    /// </summary>
    /// <param name="spriteId">The 1-based item sprite ID.</param>
    /// <param name="image">Decoded image on success. Caller owns disposal.</param>
    /// <returns>True if the entry was found and decoded successfully.</returns>
    public bool TryGetItemImage(int spriteId, out SKImage? image)
    {
        image = null;

        if (spriteId <= 0)
            return false;

        return TryGetImage($"item{spriteId:D5}.png", out image);
    }
}
