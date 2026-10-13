#region
using System.IO.Compression;
using SkiaSharp;
#endregion

namespace Brigid.Data.AssetPacks;

/// <summary>
///     An item-icon asset pack backed by a <c>.datf</c> ZIP archive. Exposes per-ID lookup via
///     <see cref="TryGetItemImage" /> and a dye-eligibility check via <see cref="IsDyeable" />.
///     Filename convention: <c>item{spriteId:D5}.png</c> at the archive root (e.g. <c>item00001.png</c>,
///     <c>item13688.png</c>) — 5-digit zero-padded, 1-based. Decoded <see cref="SKImage" /> results must be disposed
///     by the caller; typical pattern is to run them through
///     <c>Brigid.Rendering.TextureConverter.ToTexture2D</c> and cache the resulting <c>Texture2D</c>.
/// </summary>
public sealed class ItemPack : AssetPack
{
    //the exact dyeable set when the manifest has a dyeable list; null means detect from the art
    private readonly HashSet<int>? ExplicitDyeable;
    private readonly HashSet<int> NoDye;
    //the detected answer for each id, kept after the first decode of the id
    private readonly Dictionary<int, bool> DetectedDyeable = [];

    internal ItemPack(ZipArchive archive, AssetPackManifest manifest)
        : base(archive, manifest)
    {
        //a null covers value is legal JSON. treat it as no entries
        var coverage = manifest.Covers?.GetValueOrDefault("item_icons");
        ExplicitDyeable = coverage?.Dyeable is { } dyeable ? [..dyeable] : null;
        NoDye = coverage?.NoDye is { } noDye ? [..noDye] : [];
    }

    /// <summary>
    ///     Returns true if this pack contains a PNG entry for the given <paramref name="spriteId" />. Cheap (dictionary
    ///     lookup, no decode); the item resolver asks it before it decodes.
    /// </summary>
    public bool HasItem(int spriteId)
    {
        if (spriteId <= 0)
            return false;

        return HasEntry($"item{spriteId:D5}.png");
    }

    /// <summary>
    ///     Returns true when the item takes the server's dye color through the find-and-replace dye pass (BRIG-41):
    ///     <list type="bullet">
    ///         <item>A modern id (<see cref="ItemSpriteIds.MODERN_FIRST" /> and up) is never dyeable by this pass. Its art is
    ///         painted, and a painted color can match the ramp by chance. Mask dye for modern icons is BRIG-112.</item>
    ///         <item>When the manifest has a <c>dyeable</c> list, that list is the exact set.</item>
    ///         <item>Otherwise an id in <c>no_dye</c> is not dyeable, and any other id is dyeable when
    ///         <paramref name="image" /> contains a pixel of the canonical ramp (<see cref="ItemDyeRamp" />). The answer
    ///         for each id is kept after the first call.</item>
    ///     </list>
    /// </summary>
    /// <param name="spriteId">The 1-based item sprite ID.</param>
    /// <param name="image">The undyed image of this pack's entry for <paramref name="spriteId" />.</param>
    public bool IsDyeable(int spriteId, SKImage image)
    {
        if (spriteId >= ItemSpriteIds.MODERN_FIRST)
            return false;

        if (ExplicitDyeable is not null)
            return ExplicitDyeable.Contains(spriteId);

        if (NoDye.Contains(spriteId))
            return false;

        if (!DetectedDyeable.TryGetValue(spriteId, out var dyeable))
        {
            dyeable = ItemDyeRamp.ContainsRampPixel(image);
            DetectedDyeable[spriteId] = dyeable;
        }

        return dyeable;
    }

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
