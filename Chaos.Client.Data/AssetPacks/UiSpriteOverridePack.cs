#region
using System.IO.Compression;
using SkiaSharp;
#endregion

namespace Chaos.Client.Data.AssetPacks;

/// <summary>
///     A generic UI-sprite-override asset pack backed by a <c>.datf</c> ZIP archive. Exposes per-frame lookup via
///     <see cref="TryGetSprite" /> against legacy EPF/SPF file names from setoa/cious. Layout convention:
///     <c>{filename-with-extension}/{frameIndex:D4}.png</c> — e.g. <c>butt001.epf/0000.png</c> for OK button
///     frame 0, <c>dlgback2.spf/0000.png</c> for the dialog background tile. Frames need not be contiguous; missing
///     frames fall through to the legacy art. Lookup is case-insensitive via the base class's
///     <see cref="StringComparer.OrdinalIgnoreCase" /> entry index. Decoded <see cref="SKImage" /> results must be
///     disposed by the caller.
/// </summary>
public sealed class UiSpriteOverridePack : AssetPack
{
    internal UiSpriteOverridePack(ZipArchive archive, AssetPackManifest manifest)
        : base(archive, manifest) { }

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

        return TryGetImage($"{fileName}/{frameIndex:D4}.png", out image);
    }
}
