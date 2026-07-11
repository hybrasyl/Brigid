#region
using System.IO.Compression;
using SkiaSharp;
#endregion

namespace Brigid.Data.AssetPacks;

/// <summary>
///     A town-map asset pack backed by a <c>.datf</c> ZIP archive. Exposes per-map-number lookup via
///     <see cref="TryGetTownMapImage" />. Filename convention: <c>town_{mapId:D5}.png</c> at the archive root, where
///     <c>mapId</c> is the numeric map identifier the client tracks as <c>CurrentMapId</c>.
///     <para>
///     Each entry is a complete, pre-composited town-map panel (background frame, stylized art, and name baked into a
///     single image) drawn full-panel in place of the legacy <c>national.dat</c> <c>_t*</c> five-layer composite. A
///     present map replaces the legacy town map; a missing one falls back. The live player-position marker is not part
///     of this pack (it is dropped for pack-provided maps) — a full interactive town map is planned for a later
///     redesign. Decoded <see cref="SKImage" /> results must be disposed by the caller (typically the renderer's
///     town-map image cache).
///     </para>
/// </summary>
public sealed class TownMapPack : AssetPack
{
    internal TownMapPack(ZipArchive archive, AssetPackManifest manifest)
        : base(archive, manifest) { }

    /// <summary>
    ///     Attempts to decode the full-panel PNG for the given map ID. Returns false if the ID is non-positive, the
    ///     entry isn't present, decode fails, or the entry is malformed — caller falls back to the legacy
    ///     <c>national.dat</c> town-map composite.
    /// </summary>
    public bool TryGetTownMapImage(short mapId, out SKImage? image)
    {
        image = null;

        if (mapId <= 0)
            return false;

        return TryGetImage($"town_{mapId:D5}.png", out image);
    }
}
