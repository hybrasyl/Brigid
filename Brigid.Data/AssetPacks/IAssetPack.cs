namespace Brigid.Data.AssetPacks;

/// <summary>
///     The minimum contract every <c>.datf</c> asset pack exposes to the registry. Subclasses of
///     <see cref="AssetPack" /> satisfy this automatically. The interface is the type the registry stores and
///     dispatches through — keeping the contract narrow lets the registry handle every content type uniformly
///     without coupling to any particular pack's per-asset accessor surface.
/// </summary>
public interface IAssetPack : IDisposable
{
    /// <summary>
    ///     The parsed <c>_manifest.json</c> for this pack. Carries the pack's identity (<c>pack_id</c>) and any
    ///     per-content-type metadata under <see cref="AssetPackManifest.Covers" />.
    /// </summary>
    AssetPackManifest Manifest { get; }

    /// <summary>
    ///     The keys this pack covers, as a lookup asks for them. The registry compares them between two packs of one
    ///     content type to warn about an overlap. Case is ignored.
    /// </summary>
    IEnumerable<string> CoverageKeys { get; }
}
