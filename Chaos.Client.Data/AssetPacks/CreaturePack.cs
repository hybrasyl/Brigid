#region
using System.IO.Compression;
using SkiaSharp;
#endregion

namespace Chaos.Client.Data.AssetPacks;

/// <summary>
///     A creature-sprite asset pack backed by a <c>.datf</c> ZIP archive. Phase 1 covers <b>static</b> creatures —
///     one or two direction-pair masters per creature, used as a single frame across every animation state (walk,
///     attack, idle, stand). No temporal animation, no walk/attack frame differentiation.
///     <para>
///     Expected ZIP layout (phase 1 reads only the <c>stand/</c> stance — other stances are reserved per the scoping
///     doc but ignored at runtime):
///     </para>
///     <code>
///     creature_sprites/
///       creature_{spriteId:D5}/
///         stand/
///           nw_001.png   master for N (Up); mirrored at render time to W (Left)
///           es_001.png   master for E (Right); mirrored at render time to S (Down)
///     </code>
///     <para>
///     A pack may supply only one of the two pair-masters per creature; in that case the single image renders for
///     all four directions (with horizontal mirroring on the half the pair was authored to cover). Both
///     pair-masters present → the runtime picks the correct one per direction using the existing
///     <c>AnimationSystem.GetCreatureFrame</c> N/W vs E/S frame-math.
///     </para>
/// </summary>
public sealed class CreaturePack : AssetPack
{
    private readonly Dictionary<int, CreatureEntry> Coverage;

    internal CreaturePack(ZipArchive archive, AssetPackManifest manifest)
        : base(archive, manifest)
    {
        Coverage = new Dictionary<int, CreatureEntry>();

        //pre-scan ZIP entries for `creature_sprites/creature_{id:D5}/stand/{nw|es}_001.png` so per-creature lookup at runtime is O(1)
        foreach (var entry in archive.Entries)
        {
            if (!TryParsePhase1Entry(entry.FullName, out var spriteId, out var isEs))
                continue;

            ref var creature = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(Coverage, spriteId, out _);

            if (isEs)
                creature.EsEntry = entry.FullName;
            else
                creature.NwEntry = entry.FullName;
        }
    }

    /// <summary>
    ///     True if this pack contains at least one pair-master PNG for the given <paramref name="spriteId" />.
    ///     Cheap (dictionary lookup, no decode).
    /// </summary>
    public bool Covers(int spriteId) => Coverage.TryGetValue(spriteId, out var entry) && entry.PairCount > 0;

    /// <summary>
    ///     The number of pair-masters this pack supplies for the creature: 1 (single-pair, used for all directions)
    ///     or 2 (nw + es masters, the runtime picks per direction). Returns 0 if the creature isn't covered.
    /// </summary>
    public int GetPairCount(int spriteId) => Coverage.TryGetValue(spriteId, out var entry) ? entry.PairCount : 0;

    /// <summary>
    ///     Loads the PNG for the given creature and pair-index. <paramref name="pairIndex" /> is 0 for the first
    ///     authored pair (nw if present, else es) and 1 for the second authored pair (es, only valid when both pairs
    ///     are present). Returns false if the pack doesn't cover this creature, the pair-index is out of range, or
    ///     the PNG entry is missing or fails to decode. Caller owns the returned <see cref="SKImage" />.
    /// </summary>
    public bool TryGetFrame(int spriteId, int pairIndex, out SKImage? image)
    {
        image = null;

        if (!Coverage.TryGetValue(spriteId, out var entry))
            return false;

        var entryName = entry.GetEntryName(pairIndex);

        if (entryName is null)
            return false;

        return TryGetImage(entryName, out image);
    }

    /// <summary>
    ///     Parses a ZIP entry name like <c>creature_sprites/creature_00123/stand/nw_001.png</c>. Returns true if the
    ///     entry is a phase-1 stand-stance pair-master. Other stances (walk, attack, idle, attack2, attack3, death,
    ///     cast, hurt) and other frame indices are recognized by the format but not consumed at runtime in phase 1.
    /// </summary>
    private static bool TryParsePhase1Entry(string entryName, out int spriteId, out bool isEs)
    {
        spriteId = 0;
        isEs = false;

        //case-insensitive prefix match — the entry index in the base class is already case-insensitive
        const string PREFIX = "creature_sprites/creature_";
        const string STAND_INFIX = "/stand/";

        if (!entryName.StartsWith(PREFIX, StringComparison.OrdinalIgnoreCase))
            return false;

        var idStart = PREFIX.Length;
        var infixStart = entryName.IndexOf(STAND_INFIX, idStart, StringComparison.OrdinalIgnoreCase);

        if (infixStart < 0)
            return false;

        var idSpan = entryName.AsSpan(idStart, infixStart - idStart);

        if (!int.TryParse(idSpan, out spriteId) || (spriteId <= 0))
            return false;

        var fileSpan = entryName.AsSpan(infixStart + STAND_INFIX.Length);

        //phase 1 reads only the _001.png frame of each pair; higher frame indices are reserved for future stages
        if (fileSpan.Equals("nw_001.png", StringComparison.OrdinalIgnoreCase))
        {
            isEs = false;

            return true;
        }

        if (fileSpan.Equals("es_001.png", StringComparison.OrdinalIgnoreCase))
        {
            isEs = true;

            return true;
        }

        return false;
    }

    private struct CreatureEntry
    {
        public string? NwEntry;
        public string? EsEntry;

        public int PairCount => (NwEntry is null ? 0 : 1) + (EsEntry is null ? 0 : 1);

        public string? GetEntryName(int pairIndex)
        {
            //pairIndex 0 = first available pair (nw preferred, then es)
            //pairIndex 1 = second pair (es, only valid when both are present)
            if (pairIndex == 0)
                return NwEntry ?? EsEntry;

            if ((pairIndex == 1) && (NwEntry is not null))
                return EsEntry;

            return null;
        }
    }
}
