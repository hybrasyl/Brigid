#region
using Brigid.Models;
#endregion

namespace Brigid.ViewModel;

/// <summary>
///     The player's own profile (BRIG-151): the facts of the last self profile (server <c>0x39</c>), the name from the
///     player's display packet, and the portrait and the profile text from the character folder. The Details and the
///     Legend windows of the new HUD read it. It raises <see cref="Changed" /> at each change, and
///     <see cref="LegendMarksChanged" /> only when the legend marks change.
/// </summary>
public sealed class SelfProfile
{
    /// <summary>The species until the server sends it (HS-1724, Sabrael 2026-11-18).</summary>
    public const string DEFAULT_SPECIES = "Human";

    /// <summary>The player's name, from the display packet of the player. The Details window reads the name here.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The name of the player's class.</summary>
    public string ClassName { get; private set; } = string.Empty;

    /// <summary>The name of the player's guild, or empty.</summary>
    public string GuildName { get; private set; } = string.Empty;

    /// <summary>The player's rank in the guild, or empty.</summary>
    public string GuildRank { get; private set; } = string.Empty;

    /// <summary>The current title.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>The player's species. No packet sends it yet (HS-1724), so it is <see cref="DEFAULT_SPECIES" />.</summary>
    public string Species => DEFAULT_SPECIES;

    /// <summary>The titles that the player has. The retail profile sends only the current one (HS-1724).</summary>
    public IReadOnlyList<string> Titles => [Title];

    /// <summary>The name of the player's nation, or empty.</summary>
    public string NationName { get; private set; } = string.Empty;

    /// <summary>The legend marks, in the order of the server.</summary>
    public IReadOnlyList<LegendMarkEntry> LegendMarks { get; private set; } = [];

    /// <summary>True after the first self profile of the character.</summary>
    public bool HasProfile { get; private set; }

    /// <summary>The bytes of the portrait file, or empty. The client sends them when the server asks (0x4F).</summary>
    public byte[] Portrait { get; private set; } = [];

    /// <summary>The profile text. The client sends it when the server asks (0x4F).</summary>
    public string ProfileText { get; private set; } = string.Empty;

    /// <summary>Raised after each change.</summary>
    public event Action? Changed;

    /// <summary>
    ///     Raised after <see cref="Changed" /> when the legend marks change, or when <see cref="HasProfile" /> changes. Each
    ///     self profile reply (a group action asks for one) gives a new list; a list with the same marks does not raise it.
    /// </summary>
    public event Action? LegendMarksChanged;

    /// <summary>Sets the player's name.</summary>
    public void SetName(string name)
    {
        if (Name == name)
            return;

        Name = name;
        Changed?.Invoke();
    }

    /// <summary>Sets the facts of a self profile.</summary>
    public void Update(
        string className,
        string guildName,
        string guildRank,
        string title,
        string nationName,
        IReadOnlyList<LegendMarkEntry> legendMarks)
    {
        var marksChanged = !HasProfile || !LegendMarks.SequenceEqual(legendMarks);

        ClassName = className.Trim();
        GuildName = guildName.Trim();
        GuildRank = guildRank.Trim();
        Title = title.Trim();
        NationName = nationName;
        LegendMarks = [.. legendMarks];
        HasProfile = true;
        Changed?.Invoke();

        if (marksChanged)
            LegendMarksChanged?.Invoke();
    }

    /// <summary>Sets the portrait file; returns false, with no change, for the same bytes.</summary>
    public bool SetPortrait(byte[] portrait)
    {
        if (portrait.AsSpan().SequenceEqual(Portrait))
            return false;

        Portrait = portrait;
        Changed?.Invoke();

        return true;
    }

    /// <summary>Sets the profile text.</summary>
    public void SetProfileText(string text)
    {
        if (ProfileText == text)
            return;

        ProfileText = text;
        Changed?.Invoke();
    }

    /// <summary>Forgets the character: a logout or a character switch.</summary>
    public void Clear()
    {
        var marksChanged = HasProfile || (LegendMarks.Count > 0);

        Name = string.Empty;
        ClassName = string.Empty;
        GuildName = string.Empty;
        GuildRank = string.Empty;
        Title = string.Empty;
        NationName = string.Empty;
        LegendMarks = [];
        HasProfile = false;
        Portrait = [];
        ProfileText = string.Empty;
        Changed?.Invoke();

        if (marksChanged)
            LegendMarksChanged?.Invoke();
    }
}
