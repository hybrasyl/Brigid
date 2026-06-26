#region
using System.Text;
using Brigid.Data.Models;
#endregion

namespace Brigid.Data.Repositories;

/// <summary>
///     Manages per-character config files (macros, chants, friends, family) stored in a subdirectory of DataPath named
///     after the character.
/// </summary>
public sealed class LocalPlayerSettingsRepository
{
    private const string FAMILY_LIST_FILE = "Familylist.cfg";
    private const string FRIEND_LIST_FILE = "Friendlist.cfg";
    private const string MACRO_FILE = "Macro.cfg";
    private const string SKILL_BOOK_FILE = "SkillBook.cfg";
    private const string SPELL_BOOK_FILE = "SpellBook.cfg";
    private string PlayerDirectory { get; set; } = null!;

    public string FamilyListPath => Path.Combine(PlayerDirectory, FAMILY_LIST_FILE);
    public string FriendListPath => Path.Combine(PlayerDirectory, FRIEND_LIST_FILE);

    // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
    public bool IsInitialized => PlayerDirectory is not null;
    public string MacroPath => Path.Combine(PlayerDirectory, MACRO_FILE);

    public string GetFilePath(string fileName) => Path.Combine(PlayerDirectory, fileName);
    public string SkillBookPath => Path.Combine(PlayerDirectory, SKILL_BOOK_FILE);
    public string SpellBookPath => Path.Combine(PlayerDirectory, SPELL_BOOK_FILE);

    private void EnsureFileExists(string fileName)
    {
        var path = Path.Combine(PlayerDirectory, fileName);

        if (!File.Exists(path))
            File.Create(path)
                .Dispose();
    }

    /// <summary>
    ///     Initializes the repository for a specific character. Creates the directory and default config files if they don't
    ///     exist.
    /// </summary>
    public void Initialize(string characterName)
    {
        PlayerDirectory = Path.Combine(DataContext.DataPath, characterName);
        Directory.CreateDirectory(PlayerDirectory);

        EnsureFileExists(FAMILY_LIST_FILE);
        EnsureFileExists(FRIEND_LIST_FILE);
        EnsureFileExists(MACRO_FILE);
    }

    /// <summary>
    ///     Loads skill chant entries from a chant book config file.
    /// </summary>
    /// <remarks>
    ///     File format: LF-delimited, first line is a header, then repeating pairs of (skill name, "Skill:" + chant text).
    /// </remarks>
    private static List<SkillChantEntry> LoadChantBook(string path)
    {
        var entries = new List<SkillChantEntry>();

        if (!File.Exists(path))
            return entries;

        var lines = File.ReadAllText(path)
                        .Split('\n');

        //skip first line (header), then read pairs
        for (var i = 1; i < (lines.Length - 1); i += 2)
        {
            var name = lines[i]
                .TrimEnd('\r');

            var chantLine = lines[i + 1]
                .TrimEnd('\r');

            //strip "skill:" prefix
            if (chantLine.StartsWith("Skill:", StringComparison.Ordinal))
                chantLine = chantLine[6..];

            //trim leading space after colon if present
            if (chantLine.StartsWith(' '))
                chantLine = chantLine[1..];

            entries.Add(
                new SkillChantEntry
                {
                    Name = name,
                    Chant = chantLine
                });
        }

        return entries;
    }

    /// <summary>
    ///     Loads the family list from Familylist.cfg.
    /// </summary>
    public FamilyList LoadFamilyList()
    {
        var family = new FamilyList();
        var lines = File.ReadAllLines(FamilyListPath);

        if (lines.Length > 0)
            family.Mother = lines[0];

        if (lines.Length > 1)
            family.Father = lines[1];

        if (lines.Length > 2)
            family.Son1 = lines[2];

        if (lines.Length > 3)
            family.Son2 = lines[3];

        if (lines.Length > 4)
            family.Brother1 = lines[4];

        if (lines.Length > 5)
            family.Brother2 = lines[5];

        if (lines.Length > 6)
            family.Brother3 = lines[6];

        if (lines.Length > 7)
            family.Brother4 = lines[7];

        if (lines.Length > 8)
            family.Brother5 = lines[8];

        if (lines.Length > 9)
            family.Brother6 = lines[9];

        return family;
    }

    /// <summary>
    ///     Loads friend names from Friendlist.cfg. Returns the first 20 non-empty lines.
    /// </summary>
    public List<string> LoadFriendList()
        => File.ReadAllLines(FriendListPath)
               .Where(line => !string.IsNullOrWhiteSpace(line))
               .Take(20)
               .ToList();

    /// <summary>
    ///     Loads macro text from Macro.cfg. Returns a 10-element array of macro strings.
    /// </summary>
    public string[] LoadMacros()
    {
        var macros = new string[10];

        try
        {
            if (!File.Exists(MacroPath))
                return macros;

            var lines = File.ReadAllLines(MacroPath);

            for (var i = 0; i < Math.Min(lines.Length, 10); i++)
            {
                var line = lines[i];

                //strip 'macron: ' prefix if present
                var colonIndex = line.IndexOf(':');

                if (colonIndex >= 0)
                    line = line[(colonIndex + 1)..]
                        .Trim();

                //strip surrounding double quotes
                if (line is ['"', _, ..] && (line[^1] == '"'))
                    line = line[1..^1];

                macros[i] = line;
            }
        } catch
        {
            //return whatever was parsed so far
        }

        return macros;
    }

    public List<SkillChantEntry> LoadSkillChants() => LoadChantBook(SkillBookPath);

    /// <summary>
    ///     Loads spell chant entries from SpellBook.cfg.
    /// </summary>
    /// <remarks>
    ///     File format: LF-delimited, first line is a header, then repeating groups of (spell name, 10 "SpellN:" chant lines).
    /// </remarks>
    public List<SpellChantEntry> LoadSpellChants()
    {
        var entries = new List<SpellChantEntry>();

        if (!File.Exists(SpellBookPath))
            return entries;

        var lines = File.ReadAllText(SpellBookPath)
                        .Split('\n');

        //skip first line (header), then read groups of 11 (name + 10 chant lines)
        var i = 1;

        while (i < lines.Length)
        {
            var name = lines[i]
                .TrimEnd('\r');
            i++;

            if (string.IsNullOrEmpty(name))
                break;

            var entry = new SpellChantEntry
            {
                Name = name
            };

            for (var j = 0; (j < 10) && (i < lines.Length); j++, i++)
            {
                var chantLine = lines[i]
                    .TrimEnd('\r');

                //strip "spelln:" prefix
                var colonIndex = chantLine.IndexOf(':');

                if (colonIndex >= 0)
                    chantLine = chantLine[(colonIndex + 1)..];

                if (chantLine.StartsWith(' '))
                    chantLine = chantLine[1..];

                entry.Chants[j] = chantLine;
            }

            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    ///     Saves skill chant entries to a chant book config file.
    /// </summary>
    private static void SaveChantBook(string path, List<SkillChantEntry> entries)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.NewLine = "\n";

        writer.WriteLine("SkillbookUsed");

        foreach (var entry in entries)
        {
            writer.WriteLine(entry.Name);
            writer.WriteLine($"Skill:{entry.Chant}");
        }
    }

    /// <summary>
    ///     Saves the family list to Familylist.cfg.
    /// </summary>
    public void SaveFamilyList(FamilyList family)
    {
        var lines = new[]
        {
            family.Mother,
            family.Father,
            family.Son1,
            family.Son2,
            family.Brother1,
            family.Brother2,
            family.Brother3,
            family.Brother4,
            family.Brother5,
            family.Brother6
        };

        File.WriteAllLines(FamilyListPath, lines);
    }

    /// <summary>
    ///     Saves friend names to Friendlist.cfg.
    /// </summary>
    public void SaveFriendList(List<string> names) => File.WriteAllLines(FriendListPath, names.Take(20));

    /// <summary>
    ///     Saves macro text to Macro.cfg.
    /// </summary>
    public void SaveMacros(string[] macros)
    {
        var lines = new string[10];

        for (var i = 0; i < 10; i++)
        {
            var label = i < 9 ? $"Macro{i + 1}" : "Macro0";
            var value = i < macros.Length ? macros[i] : string.Empty;
            lines[i] = $"{label}: \"{value}\"";
        }

        File.WriteAllLines(MacroPath, lines);
    }

    public void SaveSkillChants(List<SkillChantEntry> entries) => SaveChantBook(SkillBookPath, entries);

    /// <summary>
    ///     Saves spell chant entries to SpellBook.cfg.
    /// </summary>
    public void SaveSpellChants(List<SpellChantEntry> entries)
    {
        using var writer = new StreamWriter(SpellBookPath, false, Encoding.UTF8);
        writer.NewLine = "\n";

        writer.WriteLine("SpellbookUsed");

        foreach (var entry in entries)
        {
            writer.WriteLine(entry.Name);

            for (var i = 0; i < 10; i++)
                writer.WriteLine($"Spell{i}:{entry.Chants[i]}");
        }
    }
}