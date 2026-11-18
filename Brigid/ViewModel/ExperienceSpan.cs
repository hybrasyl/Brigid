namespace Brigid.ViewModel;

/// <summary>
///     The experience at which the current level of a track started (BRIG-148): the level, or the ability level of a retail
///     master. The attributes packet gives the total and the experience still needed for the next level, not the start of
///     the level, so the client learns the start at a level-up that it sees. The start of the new level is the threshold of
///     the level before it: the total plus the experience to the next level, which stays the same for each reading in a
///     level. A gain of more than one level, or a reading at a level that is not the level of the start, makes the start
///     unknown until the next level-up.
/// </summary>
/// <remarks>
///     Packets can arrive in an order that gives a wrong threshold. An experience block of the new level before the level
///     block that raises it gives the threshold of the level after, so the threshold of a level is the lowest of its
///     readings. A level block before the experience block gives a reading at the new level with the total of the level
///     before, under the start, so a reading under the start gives no threshold. A reading with 0 experience to the next
///     level gives no threshold: a server sends it at the level cap, and Hybrasyl also when an experience formula changes
///     under a character.
/// </remarks>
public sealed class ExperienceSpan
{
    private readonly byte? ZeroStartLevel;
    private bool HasReading;
    private byte LastLevel;
    private ulong? LastLevelThreshold;

    private ExperienceSpan(byte? zeroStartLevel) => ZeroStartLevel = zeroStartLevel;

    /// <summary>The span of the level, where level 1 starts at 0 experience.</summary>
    public static ExperienceSpan ForLevel() => new(1);

    /// <summary>The span of the ability level of a retail master, which has no level with a known start.</summary>
    public static ExperienceSpan ForAbility() => new(null);

    /// <summary>The level of <see cref="Start" />.</summary>
    public byte Level { get; private set; }

    /// <summary>The experience at which <see cref="Level" /> started, or null when the client does not know it.</summary>
    public ulong? Start { get; private set; }

    /// <summary>Forgets the start and the readings, for the next character.</summary>
    public void Reset()
    {
        HasReading = false;
        LastLevel = 0;
        LastLevelThreshold = null;
        Level = 0;
        Start = null;
    }

    /// <summary>
    ///     Takes a saved start for <paramref name="level" />. It applies only while the client does not know a start, and
    ///     only when no reading of another level came before it.
    /// </summary>
    public void Restore(byte level, ulong start)
    {
        if (Start is not null || (HasReading && (LastLevel != level)))
            return;

        Level = level;
        Start = start;
    }

    /// <summary>
    ///     Takes a reading of the track. Returns true when the reading teaches a new start, so that the caller saves it.
    /// </summary>
    public bool Observe(byte level, uint total, uint toNext)
    {
        var learned = false;

        if (!HasReading || (level != LastLevel))
        {
            if (HasReading && (level == LastLevel + 1) && LastLevelThreshold is { } threshold)
            {
                Level = level;
                Start = threshold;
                learned = true;
            } else if (level != Level)
            {
                Level = level;
                Start = level == ZeroStartLevel ? 0 : null;
            }

            HasReading = true;
            LastLevel = level;
            LastLevelThreshold = null;
        }

        //a reading under the start of its level has the experience of the level before (its level block came first), so
        //its threshold is the threshold of the level before
        if ((toNext > 0) && !(Start is { } start && (total < start)))
        {
            var threshold = (ulong)total + toNext;

            if (LastLevelThreshold is not { } lowest || (threshold < lowest))
                LastLevelThreshold = threshold;
        }

        return learned;
    }

    /// <summary>
    ///     The experience gained in <paramref name="level" /> and the experience of the full level, or null when the start
    ///     of that level is unknown. A total under the start (a loss) gives 0 gained.
    /// </summary>
    public (ulong Gained, ulong Full)? Progress(byte level, uint total, uint toNext)
    {
        if (Start is not { } start || (level != Level))
            return null;

        var gained = total > start ? total - start : 0;

        return (gained, gained + toNext);
    }
}
