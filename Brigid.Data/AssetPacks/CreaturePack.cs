#region
using System.IO.Compression;
using SkiaSharp;
#endregion

namespace Brigid.Data.AssetPacks;

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
///           n_001.png    drawn facing N (Up). Engine renders un-flipped for direction Up and horizontally
///                        mirrored for direction Left.
///           e_001.png    drawn facing E (Right). Engine renders un-flipped for direction Right and
///                        horizontally mirrored for direction Down.
///     </code>
///     <para>
///     Names match the engine's un-flipped (master) direction. The engine's flip rule is
///     <c>flip = direction is Down or Left</c>, so the pairs are N↔W (N un-flipped, W mirrored) and E↔S (E
///     un-flipped, S mirrored). Artists author the master image facing N or E; the engine handles W and S
///     automatically by horizontal mirror at draw time.
///     </para>
///     <para>
///     A pack may supply only one of the two pair-masters per creature; in that case the single image renders for
///     all four directions of that pair, and the other pair falls through to the legacy MPF path. For full
///     coverage with a single source image (typical of viewer-facing static portraits), the same PNG can be
///     copied into both <c>n_001.png</c> and <c>e_001.png</c> slots.
///     </para>
/// </summary>
public sealed class CreaturePack : AssetPack
{
    private readonly Dictionary<int, CreatureEntry> Coverage;

    internal CreaturePack(ZipArchive archive, AssetPackManifest manifest)
        : base(archive, manifest)
    {
        Coverage = new Dictionary<int, CreatureEntry>();

        //pre-scan ZIP entries for `creature_sprites/creature_{id:D5}/stand/{n|e}_001.png` so per-creature lookup at runtime is O(1)
        foreach (var entry in archive.Entries)
        {
            if (!TryParsePhase1Entry(entry.FullName, out var spriteId, out var isE))
                continue;

            ref var creature = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(Coverage, spriteId, out _);

            if (isE)
                creature.EEntry = entry.FullName;
            else
                creature.NEntry = entry.FullName;
        }
    }

    /// <summary>
    ///     True if this pack contains at least one pair-master PNG for the given <paramref name="spriteId" />.
    ///     Cheap (dictionary lookup, no decode).
    /// </summary>
    public bool Covers(int spriteId) => Coverage.TryGetValue(spriteId, out var entry) && entry.PairCount > 0;

    /// <summary>
    ///     The number of pair-masters this pack supplies for the creature: 1 (single-pair, used for all directions)
    ///     or 2 (n + e masters, the runtime picks per direction). Returns 0 if the creature isn't covered.
    /// </summary>
    public int GetPairCount(int spriteId) => Coverage.TryGetValue(spriteId, out var entry) ? entry.PairCount : 0;

    /// <summary>
    ///     Loads the PNG for the given creature and pair-index. <paramref name="pairIndex" /> is 0 for the first
    ///     authored pair (n if present, else e) and 1 for the second authored pair (e, only valid when both pairs
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
    ///     Parses a ZIP entry name like <c>creature_sprites/creature_00123/stand/n_001.png</c>. Returns true if the
    ///     entry is a phase-1 stand-stance pair-master. Other stances (walk, attack, idle, attack2, attack3, death,
    ///     cast, hurt) and other frame indices are recognized by the format but not consumed at runtime in phase 1.
    /// </summary>
    private static bool TryParsePhase1Entry(string entryName, out int spriteId, out bool isE)
    {
        spriteId = 0;
        isE = false;

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
        if (fileSpan.Equals("n_001.png", StringComparison.OrdinalIgnoreCase))
        {
            isE = false;

            return true;
        }

        if (fileSpan.Equals("e_001.png", StringComparison.OrdinalIgnoreCase))
        {
            isE = true;

            return true;
        }

        return false;
    }

    private struct CreatureEntry
    {
        public string? NEntry;
        public string? EEntry;

        public int PairCount => (NEntry is null ? 0 : 1) + (EEntry is null ? 0 : 1);

        public string? GetEntryName(int pairIndex)
        {
            //pairIndex 0 = first available pair (n preferred, then e)
            //pairIndex 1 = second pair (e, only valid when both are present)
            if (pairIndex == 0)
                return NEntry ?? EEntry;

            if ((pairIndex == 1) && (NEntry is not null))
                return EEntry;

            return null;
        }
    }
}
