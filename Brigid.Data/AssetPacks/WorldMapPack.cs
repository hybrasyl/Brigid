#region
using System.IO.Compression;
using SkiaSharp;
#endregion

namespace Brigid.Data.AssetPacks;

/// <summary>
///     A world-map (overworld field) asset pack backed by a <c>.datf</c> ZIP archive. Exposes per-field lookup via
///     <see cref="TryGetFieldImage" />. Filename convention: <c>{fieldName}.png</c> at the archive root, where
///     <c>fieldName</c> is the server-sent <c>ClientMap</c> string verbatim (e.g. <c>field001</c>) — a named, not
///     numeric, identifier, so no zero-padding. Case-insensitive. These replace the legacy <c>{field}.epf</c> +
///     <c>{field}.pal</c> background drawn full-screen behind the clickable world-map nodes; a present field replaces
///     the legacy art, a missing one falls back. Decoded <see cref="SKImage" /> results must be disposed by the caller
///     (typically the renderer's field-image cache).
/// </summary>
public sealed class WorldMapPack : AssetPack
{
    internal WorldMapPack(ZipArchive archive, AssetPackManifest manifest)
        : base(archive, manifest) { }

    /// <summary>
    ///     Attempts to decode the PNG for the given <paramref name="fieldName" />. Returns false if the entry isn't
    ///     present, decode fails, or the entry is malformed — caller falls back to the legacy EPF+PAL field image.
    /// </summary>
    public bool TryGetFieldImage(string fieldName, out SKImage? image) => TryGetImage($"{fieldName}.png", out image);
}
