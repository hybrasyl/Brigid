namespace Chaos.Client.Data.AssetPacks;

/// <summary>
///     The minimum contract every <c>.datf</c> asset pack exposes to the registry. Subclasses of
///     <see cref="AssetPack" /> satisfy this automatically. The interface is the type the registry stores and
///     dispatches through — keeping the contract narrow lets the registry handle every content type uniformly
///     without coupling to any particular pack's per-asset accessor surface.
/// </summary>
public interface IAssetPack : IDisposable
{
    /// <summary>
    ///     The parsed <c>_manifest.json</c> for this pack. Carries the pack's identity (<c>pack_id</c>), its
    ///     priority (used by the registry for conflict resolution when two packs cover the same content type), and
    ///     any per-content-type metadata under <see cref="AssetPackManifest.Covers" />.
    /// </summary>
    AssetPackManifest Manifest { get; }
}
