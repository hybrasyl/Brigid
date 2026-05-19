#region
using System.IO.Compression;
using SkiaSharp;
#endregion

namespace Chaos.Client.Data.AssetPacks;

/// <summary>
///     An NPC-portrait asset pack backed by a <c>.datf</c> ZIP archive. Replaces the legacy <c>npc/npcbase.dat</c>
///     SPF illustrations with uniform square PNGs (e.g. 200x200) keyed by the server-sent NPC name and an optional
///     variant index. Lookup uses an explicit <c>(name, variant) → filename</c> table declared in the manifest's
///     <c>covers.npc_portraits.portraits</c> block — there is no filename-derivation rule, so artist tooling owns
///     the mapping verbatim.
/// </summary>
public sealed class NpcPortraitPack : AssetPack
{
    private readonly Dictionary<(string Name, int Variant), string> Lookup;

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
        Lookup = new Dictionary<(string, int), string>();

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

        foreach (var (rawName, entry) in portraits)
        {
            var name = rawName?.Trim();

            if (string.IsNullOrEmpty(name))
                continue;

            if (string.IsNullOrEmpty(entry.Default))
                continue;

            Lookup[(name, 0)] = entry.Default;

            if (entry.Variants is null)
                continue;

            for (var i = 0; i < entry.Variants.Length; i++)
            {
                var fileName = entry.Variants[i];

                if (string.IsNullOrEmpty(fileName))
                    continue;

                Lookup[(name, i + 1)] = fileName;
            }
        }
    }

    /// <summary>
    ///     Attempts to decode the portrait PNG for the given (npcName, variant) pair. Whitespace on
    ///     <paramref name="npcName" /> is trimmed; the lookup is case-sensitive otherwise. When the requested variant
    ///     isn't declared, falls back to variant 0 (the default). Returns false if neither the requested variant nor
    ///     the default is present, the entry decodes to a size other than <see cref="Dimensions" />, or the pack was
    ///     constructed with invalid dimensions.
    /// </summary>
    /// <param name="npcName">NPC name as sent by the server.</param>
    /// <param name="variant">0 = default, 1+ = entries from <see cref="AssetPackPortraitEntry.Variants" />.</param>
    /// <param name="image">Decoded image on success. Caller owns disposal.</param>
    public bool TryGetIllustration(string npcName, int variant, out SKImage? image)
    {
        image = null;

        if (Dimensions.Width == 0)
            return false;

        if (string.IsNullOrEmpty(npcName))
            return false;

        var name = npcName.Trim();

        if (!Lookup.TryGetValue((name, variant), out var entryName) && ((variant == 0) || !Lookup.TryGetValue((name, 0), out entryName)))
            return false;

        if (!TryGetImage(entryName!, out image) || image is null)
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
