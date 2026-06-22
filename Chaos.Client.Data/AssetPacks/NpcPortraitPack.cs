#region
using System.IO.Compression;
using SkiaSharp;
#endregion

namespace Chaos.Client.Data.AssetPacks;

/// <summary>
///     An NPC-portrait asset pack backed by a <c>.datf</c> ZIP archive. Replaces the legacy <c>npc/npcbase.dat</c>
///     SPF illustrations with uniform square PNGs (e.g. 200x200) keyed by the literal value of the Hybrasyl XML
///     <c>Portrait</c> attribute as the server publishes it via NPCIllust — verbatim, no extension stripping or
///     normalization. Examples: <c>"inn.spf"</c> (innkeeper declaring a legacy SPF portrait) or <c>"Gobalt"</c>
///     (modern custom NPC declaring a bare portrait name). Lookup is case-insensitive, matching the legacy
///     <see cref="DALib.Data" /> npci.tbl semantics on string casing.
/// </summary>
public sealed class NpcPortraitPack : AssetPack
{
    private readonly Dictionary<string, string> Lookup;

    /// <summary>
    ///     Uniform pixel dimensions every portrait in this pack must match. Validated at decode time; mismatches are
    ///     logged and treated as misses so the renderer falls through to legacy. <c>(0, 0)</c> if the manifest's
    ///     <c>dimensions</c> field was missing, malformed, or non-square — in that case the pack registers but never
    ///     serves illustrations.
    /// </summary>
    public (int Width, int Height) Dimensions { get; }

    internal NpcPortraitPack(ZipArchive archive, AssetPackManifest manifest)
        : base(archive, manifest)
    {
        Lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var coverage = manifest.Covers.GetValueOrDefault("npc_portraits");
        var dims = coverage?.Dimensions;

        if (dims is null || dims.Length != 2 || dims[0] != dims[1] || dims[0] <= 0)
        {
            //phase 1 requires uniform square portraits — fail closed if the manifest doesn't promise that
            Console.Error.WriteLine($"[asset-pack] npc_portraits pack '{manifest.PackId}' has missing or non-square dimensions; pack will serve no illustrations");
            Dimensions = (0, 0);

            return;
        }

        Dimensions = (dims[0], dims[1]);

        var portraits = coverage?.Portraits;

        if (portraits is null)
            return;

        foreach (var (rawKey, fileName) in portraits)
        {
            var key = rawKey?.Trim();

            if (string.IsNullOrEmpty(key))
                continue;

            if (string.IsNullOrEmpty(fileName))
                continue;

            Lookup[key] = fileName;
        }
    }

    /// <summary>
    ///     Attempts to decode the portrait PNG for the given <paramref name="portraitKey" />. The key is the literal
    ///     value pulled from NPCIllust at runtime — e.g. <c>"inn.spf"</c> for Cotilde, <c>"Gobalt"</c> for Gobalt.
    ///     Whitespace on the input is trimmed; lookup is case-insensitive. Returns false if the key isn't in the
    ///     manifest, the PNG is missing or unreadable, the decoded image's size doesn't match
    ///     <see cref="Dimensions" />, or the pack was constructed with invalid dimensions.
    /// </summary>
    /// <param name="portraitKey">Literal NPCIllust value (matches the XML <c>Portrait</c> attribute verbatim).</param>
    /// <param name="image">Decoded image on success. Caller owns disposal.</param>
    public bool TryGetIllustration(string portraitKey, out SKImage? image)
    {
        image = null;

        if (Dimensions.Width == 0)
            return false;

        if (string.IsNullOrEmpty(portraitKey))
            return false;

        var key = portraitKey.Trim();

        if (!Lookup.TryGetValue(key, out var entryName))
            return false;

        if (!TryGetImage(entryName, out image) || image is null)
            return false;

        if ((image.Width != Dimensions.Width) || (image.Height != Dimensions.Height))
        {
            Console.Error.WriteLine($"[asset-pack] npc_portraits: '{entryName}' is {image.Width}x{image.Height}; expected {Dimensions.Width}x{Dimensions.Height}; ignoring");
            image.Dispose();
            image = null;

            return false;
        }

        return true;
    }
}
