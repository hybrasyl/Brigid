#region
using System.Collections.Frozen;
using Chaos.DarkAges.Definitions;
#endregion

namespace Brigid.Data.Models;

/// <summary>
///     Lookup table that determines whether a given armor sprite is allowed to play a specific body animation.
/// </summary>
public sealed class AbilityAnimationTable
{
    private const byte CLASS_ANIM_BASE = 128;

    /// <summary>
    ///     Allowed armor sprite IDs keyed by skill animation entry index.
    /// </summary>
    /// <remarks>
    ///     Entry index = (byte)BodyAnimation - 128. Overcoat armor IDs are stored with a +1000 offset.
    /// </remarks>
    private readonly FrozenDictionary<byte, FrozenSet<int>> AllowedArmorsByEntry;

    internal AbilityAnimationTable(Dictionary<byte, HashSet<int>> mutableData)
        => AllowedArmorsByEntry = mutableData.ToFrozenDictionary(kvp => kvp.Key, kvp => kvp.Value.ToFrozenSet());

    /// <summary>
    ///     Returns true if the given armor sprite is allowed to play the specified body animation.
    /// </summary>
    /// <remarks>
    ///     Peasant and emote animations (below 0x80) are always allowed. Animations not present in the table are also allowed
    ///     defensively.
    /// </remarks>
    public bool IsAbilityAnimationAllowed(BodyAnimation anim, int spriteId)
    {
        var animByte = (byte)anim;

        if (animByte < CLASS_ANIM_BASE)
            return true;

        var entryIndex = (byte)(animByte - CLASS_ANIM_BASE);

        if (!AllowedArmorsByEntry.TryGetValue(entryIndex, out var allowedIds))
            return true;

        return allowedIds.Contains(spriteId);
    }
}