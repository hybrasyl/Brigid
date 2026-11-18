#region
using Brigid.Models;
#endregion

namespace Brigid.ViewModel;

/// <summary>An entry of the activity log: the local time and the text.</summary>
public readonly record struct ActivityEntry(DateTime Time, string Text);

/// <summary>
///     The activity log of the session (BRIG-151, D34). It reads its own sources: the level and the ability level of each
///     attributes packet that has a primary block, and the legend marks of each self profile. A gain of a level or of an
///     ability level is an entry, and so is a legend mark that a self profile adds after the first one. An invisible mark
///     is not an entry. The server sends no dated history, so the log holds the events of this session only; a Hybrasyl
///     history replaces it later. It keeps the newest <see cref="MAX_ENTRIES" />, newest first.
/// </summary>
public sealed class ActivityLog
{
    public const int MAX_ENTRIES = 50;

    private readonly List<ActivityEntry> Items = [];
    private readonly PlayerAttributes Attributes;
    private readonly SelfProfile Profile;
    private readonly Func<DateTime> Clock;
    private byte LastLevel;
    private byte LastAbility;
    private Dictionary<(string Key, string Text), int>? LastMarks;
    private IReadOnlyList<LegendMarkEntry>? ReadMarks;

    /// <param name="attributes">The player's attributes; the log reads the levels of each primary block.</param>
    /// <param name="profile">The player's profile; the log reads its legend marks.</param>
    /// <param name="clock">The local time; null for <see cref="DateTime.Now" />.</param>
    public ActivityLog(PlayerAttributes attributes, SelfProfile profile, Func<DateTime>? clock = null)
    {
        Attributes = attributes;
        Profile = profile;
        Clock = clock ?? (() => DateTime.Now);

        Attributes.Changed += OnAttributesChanged;
        Profile.Changed += OnProfileChanged;
    }

    /// <summary>The entries, newest first.</summary>
    public IReadOnlyList<ActivityEntry> Entries => Items;

    /// <summary>Raised after an entry is added, and after a clear.</summary>
    public event Action? Changed;

    /// <summary>
    ///     Reads a level and an ability level. The first reading of the character only sets the start; a higher level after
    ///     it is a gain.
    /// </summary>
    internal void ObserveLevels(byte level, byte ability)
    {
        if (level == 0)
            return;

        if ((LastLevel > 0) && (level > LastLevel))
            Add(Gain($"Reached level {level}", level - LastLevel));

        if ((LastLevel > 0) && (ability > LastAbility))
            Add(Gain($"Reached ability level {ability}", ability - LastAbility));

        LastLevel = level;
        LastAbility = ability;
    }

    /// <summary>
    ///     Reads the legend marks of a self profile. The first profile of the character only sets the start; a mark that a
    ///     later profile adds is an entry. A mark is its key and its text, so a second mark with the same text is an entry
    ///     too. An invisible mark is read too, because the player sees their own invisible marks.
    /// </summary>
    internal void ObserveLegendMarks(IEnumerable<LegendMarkEntry> marks)
    {
        var current = new Dictionary<(string Key, string Text), int>();

        foreach (var mark in marks)
            current[(mark.Key, mark.Text)] = current.GetValueOrDefault((mark.Key, mark.Text)) + 1;

        if (LastMarks is not null)
            foreach (var (mark, count) in current)
                for (var i = LastMarks.GetValueOrDefault(mark); i < count; i++)
                    Add($"Legend: {mark.Text}");

        LastMarks = current;
    }

    /// <summary>Forgets the session: a logout or a character switch.</summary>
    public void Clear()
    {
        Items.Clear();
        LastLevel = 0;
        LastAbility = 0;
        LastMarks = null;
        ReadMarks = null;
        Changed?.Invoke();
    }

    //a packet with no primary block keeps the levels of the character before a logout, so only a primary block is read
    private void OnAttributesChanged()
    {
        if (Attributes.HasPrimary)
            ObserveLevels(Attributes.Level, Attributes.Ability);
    }

    //the profile changes for its name, portrait and text too; a list of marks that was read already adds nothing, so the
    //log skips it and does not count the marks again
    private void OnProfileChanged()
    {
        if (!Profile.HasProfile || ReferenceEquals(Profile.LegendMarks, ReadMarks))
            return;

        ReadMarks = Profile.LegendMarks;
        ObserveLegendMarks(Profile.LegendMarks);
    }

    private static string Gain(string text, int steps) => steps == 1 ? text : $"{text} (+{steps})";

    private void Add(string text)
    {
        Items.Insert(0, new ActivityEntry(Clock(), text));

        if (Items.Count > MAX_ENTRIES)
            Items.RemoveAt(Items.Count - 1);

        Changed?.Invoke();
    }
}
