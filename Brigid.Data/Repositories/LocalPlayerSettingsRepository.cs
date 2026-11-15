#region
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Brigid.Data.Models;
#endregion

namespace Brigid.Data.Repositories;

/// <summary>The per-character files that the client reads and saves.</summary>
public enum CharacterFile
{
    FamilyList,
    FriendList,
    Macros,
    SkillChants,
    SpellChants,
    ProfileText
}

/// <summary>
///     Manages per-character config files (macros, chants, friends, family) stored per-server, per-character under
///     <c>{AppPaths.ProfilesDir}/{serverKey}/{characterName}</c> — a writable per-user location, keyed by server so the
///     same character name on different servers no longer shares one folder. Legacy config from the old in-game-folder
///     location is imported once on init.
/// </summary>
/// <remarks>
///     Each file is a <see cref="GuardedFile" />, made again at each open: a file that another program holds, or that is
///     denied, loads as empty, and its save is refused until a read of it succeeds, so that empty data never replaces
///     good data (BRIG-177). The client does not support a hand edit of these files while it runs.
/// </remarks>
public sealed class LocalPlayerSettingsRepository
{
    private const string FAMILY_LIST_FILE = "Familylist.cfg";
    private const string FRIEND_LIST_FILE = "Friendlist.cfg";
    private const string MACRO_FILE = "Macro.cfg";
    private const string SKILL_BOOK_FILE = "SkillBook.cfg";
    private const string SPELL_BOOK_FILE = "SpellBook.cfg";
    private const string PROFILE_TEXT_FILE = "profile.txt";
    private string PlayerDirectory { get; set; } = null!;

    //the files of this character, made again at each open, so that a new character or session reads each file again
    private readonly Dictionary<CharacterFile, GuardedFile> Files = [];

    // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
    public bool IsInitialized => PlayerDirectory is not null;

    /// <summary>
    ///     Joins a file name to the character folder. Returns false before Initialize, and for a name that is not a
    ///     bare, cross-platform file name (see <see cref="FileNames.IsSafeName" />). A caller can pass a server-sent
    ///     name, and Path.Combine follows a rooted name or ".." out of the folder.
    /// </summary>
    public bool TryGetFilePath(string fileName, [NotNullWhen(true)] out string? path)
    {
        path = IsInitialized ? CombineBareName(PlayerDirectory, fileName) : null;

        return path is not null;
    }

    internal static string? CombineBareName(string directory, string fileName)
        => FileNames.IsSafeName(fileName) ? Path.Combine(directory, fileName) : null;

    private void EnsureFileExists(string fileName)
    {
        var path = Path.Combine(PlayerDirectory, fileName);

        if (!File.Exists(path))
            DataDiagnostics.Try(() => File.Create(path).Dispose(), $"create {fileName}");
    }

    /// <summary>
    ///     Initializes the repository for a specific character on a specific server. Character config lives under
    ///     <c>{AppPaths.ProfilesDir}/{serverKey}/{characterName}</c> (writable per-user location, keyed by server so the
    ///     same name on different servers doesn't collide). Creates the directory, imports any legacy config from the old
    ///     in-game-folder location on first use, and ensures the default files exist.
    /// </summary>
    public void Initialize(string characterName, string serverKey, bool importLegacy)
    {
        //the character name is server-supplied — sanitize it before it becomes a path segment so a hostile/odd name
        //can't escape the profiles root. The legacy import still reads the raw-named legacy folder as its source.
        Open(AppPaths.ProfileDir(serverKey, FileNames.ToSafeName(characterName)));

        //only pull in the old in-game-folder config if the user opted into the one-time import
        if (importLegacy)
            ImportLegacyCharacterFiles(characterName);

        EnsureFileExists(FAMILY_LIST_FILE);
        EnsureFileExists(FRIEND_LIST_FILE);
        EnsureFileExists(MACRO_FILE);
    }

    //a new character or session reads every file again, so a file that could not be read gets another try
    private void Open(string directory)
    {
        PlayerDirectory = directory;
        Files.Clear();

        foreach (var file in Enum.GetValues<CharacterFile>())
            Files[file] = new GuardedFile(Path.Combine(directory, FileNameOf(file)), DescriptionOf(file));

        DataDiagnostics.Try(() => Directory.CreateDirectory(PlayerDirectory), "create profile dir");
    }

    /// <summary>Opens the repository on a folder, with no legacy import and no default files, for the tests.</summary>
    internal void OpenForTests(string directory) => Open(directory);

    private static string FileNameOf(CharacterFile file)
        => file switch
        {
            CharacterFile.FamilyList  => FAMILY_LIST_FILE,
            CharacterFile.FriendList  => FRIEND_LIST_FILE,
            CharacterFile.Macros      => MACRO_FILE,
            CharacterFile.SkillChants => SKILL_BOOK_FILE,
            CharacterFile.SpellChants => SPELL_BOOK_FILE,
            CharacterFile.ProfileText => PROFILE_TEXT_FILE,
            _                         => throw new ArgumentOutOfRangeException(nameof(file), file, null)
        };

    private static string DescriptionOf(CharacterFile file)
        => file switch
        {
            CharacterFile.FamilyList  => "family list",
            CharacterFile.FriendList  => "friend list",
            CharacterFile.Macros      => "macros",
            CharacterFile.SkillChants => "skill chants",
            CharacterFile.SpellChants => "spell chants",
            CharacterFile.ProfileText => "profile text",
            _                         => throw new ArgumentOutOfRangeException(nameof(file), file, null)
        };

    /// <summary>
    ///     True when the old in-game-folder location holds per-character config worth importing — a subdirectory of
    ///     <c>DataPath</c> containing at least one recognizable config file. Drives the one-time import offer.
    /// </summary>
    public bool HasLegacyCharacterData()
    {
        if (string.IsNullOrEmpty(DataContext.DataPath) || !Directory.Exists(DataContext.DataPath))
            return false;

        string[] probe = [FAMILY_LIST_FILE, FRIEND_LIST_FILE, MACRO_FILE, SKILL_BOOK_FILE, SPELL_BOOK_FILE];
        var found = false;

        DataDiagnostics.Try(
            () =>
            {
                foreach (var dir in Directory.EnumerateDirectories(DataContext.DataPath))
                    if (probe.Any(f => File.Exists(Path.Combine(dir, f))))
                    {
                        found = true;

                        break;
                    }
            }, "scan legacy character data");

        return found;
    }

    /// <summary>
    ///     The legacy character folder, <c>{dataPath}/{characterName}</c>, or null when there is no data folder or the name
    ///     is not a bare file name. The name comes from the server, and the legacy folder is always a direct child of the
    ///     data folder, so a name that Path.Combine would follow out of the game folder does not import.
    /// </summary>
    internal static string? LegacyFolderFor(string? dataPath, string characterName)
        => !string.IsNullOrEmpty(dataPath) && FileNames.IsSafeName(characterName) ? Path.Combine(dataPath, characterName) : null;

    /// <summary>
    ///     One-time best-effort import of a character's legacy config from the old <c>{DataPath}/{characterName}</c>
    ///     location (where retail Dark Ages and older Brigid builds wrote it) into the new per-user profile directory.
    ///     Copies only files that don't already exist in the target, so it runs harmlessly on every init and does nothing
    ///     once migrated. The legacy files are left in place (the source may be read-only).
    /// </summary>
    private void ImportLegacyCharacterFiles(string characterName)
    {
        if (LegacyFolderFor(DataContext.DataPath, characterName) is not { } legacyDir)
            return;

        if (!Directory.Exists(legacyDir) || string.Equals(legacyDir, PlayerDirectory, StringComparison.OrdinalIgnoreCase))
            return;

        //materialize the listing under a guard — enumeration itself can throw (denied listing) on the legacy dir
        var sources = Array.Empty<string>();
        DataDiagnostics.Try(() => sources = Directory.GetFiles(legacyDir), "list legacy character files");

        foreach (var source in sources)
        {
            var target = Path.Combine(PlayerDirectory, Path.GetFileName(source));

            if (!File.Exists(target))
                DataDiagnostics.Try(() => File.Copy(source, target), $"import {Path.GetFileName(source)}");
        }
    }

    /// <summary>
    ///     Loads skill chant entries from a chant book config file.
    /// </summary>
    /// <remarks>
    ///     File format: LF-delimited, first line is a header, then repeating pairs of (skill name, "Skill:" + chant text).
    /// </remarks>
    private static List<SkillChantEntry> ParseChantBook(string text)
    {
        var entries = new List<SkillChantEntry>();
        var lines = text.Split('\n');

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
        var lines = Files[CharacterFile.FamilyList].Read(File.ReadAllLines, []);

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
        => Files[CharacterFile.FriendList].Read(File.ReadAllLines, [])
           .Where(line => !string.IsNullOrWhiteSpace(line))
           .Take(20)
           .ToList();

    /// <summary>
    ///     Loads macro text from Macro.cfg. Returns a 10-element array of macro strings.
    /// </summary>
    public string[] LoadMacros() => ParseMacros(Files[CharacterFile.Macros].Read(File.ReadAllLines, []));

    /// <summary>
    ///     Parses Macro.cfg lines into the 10 macro slots. A slot with no line is empty, never null: Initialize writes an
    ///     empty Macro.cfg for a new character, and the Options macro boxes take these values as their text.
    /// </summary>
    internal static string[] ParseMacros(IReadOnlyList<string> lines)
    {
        var macros = new string[10];
        Array.Fill(macros, string.Empty);

        for (var i = 0; i < Math.Min(lines.Count, 10); i++)
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

        return macros;
    }

    public List<SkillChantEntry> LoadSkillChants() => ParseChantBook(Files[CharacterFile.SkillChants].Read(File.ReadAllText, string.Empty));

    /// <summary>
    ///     Loads spell chant entries from SpellBook.cfg.
    /// </summary>
    /// <remarks>
    ///     File format: LF-delimited, first line is a header, then repeating groups of (spell name, 10 "SpellN:" chant lines).
    /// </remarks>
    public List<SpellChantEntry> LoadSpellChants()
    {
        var entries = new List<SpellChantEntry>();
        var lines = Files[CharacterFile.SpellChants].Read(File.ReadAllText, string.Empty)
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
    private static SaveResult SaveChantBook(GuardedFile file, List<SkillChantEntry> entries) => file.Save(
        path =>
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            writer.NewLine = "\n";

            writer.WriteLine("SkillbookUsed");

            foreach (var entry in entries)
            {
                writer.WriteLine(entry.Name);
                writer.WriteLine($"Skill:{entry.Chant}");
            }
        });

    /// <summary>
    ///     Saves the family list to Familylist.cfg.
    /// </summary>
    public SaveResult SaveFamilyList(FamilyList family)
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

        return Files[CharacterFile.FamilyList].Save(path => File.WriteAllLines(path, lines));
    }

    /// <summary>
    ///     Saves friend names to Friendlist.cfg.
    /// </summary>
    public SaveResult SaveFriendList(List<string> names)
        => Files[CharacterFile.FriendList].Save(path => File.WriteAllLines(path, names.Take(20)));

    /// <summary>
    ///     Saves macro text to Macro.cfg.
    /// </summary>
    public SaveResult SaveMacros(string[] macros)
    {
        var lines = FormatMacros(macros);

        return Files[CharacterFile.Macros].Save(path => File.WriteAllLines(path, lines));
    }

    /// <summary>Formats the 10 macro slots as Macro.cfg lines; <see cref="ParseMacros" /> reads them back.</summary>
    internal static string[] FormatMacros(string[] macros)
    {
        var lines = new string[10];

        for (var i = 0; i < 10; i++)
        {
            var label = i < 9 ? $"Macro{i + 1}" : "Macro0";
            var value = i < macros.Length ? macros[i] : string.Empty;
            lines[i] = $"{label}: \"{value}\"";
        }

        return lines;
    }

    public SaveResult SaveSkillChants(List<SkillChantEntry> entries) => SaveChantBook(Files[CharacterFile.SkillChants], entries);

    /// <summary>
    ///     Saves spell chant entries to SpellBook.cfg.
    /// </summary>
    public SaveResult SaveSpellChants(List<SpellChantEntry> entries) => Files[CharacterFile.SpellChants].Save(
        path =>
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            writer.NewLine = "\n";

            writer.WriteLine("SpellbookUsed");

            foreach (var entry in entries)
            {
                writer.WriteLine(entry.Name);

                for (var i = 0; i < 10; i++)
                    writer.WriteLine($"Spell{i}:{entry.Chants[i]}");
            }
        });

    /// <summary>Loads the player's own profile text from profile.txt, or an empty text.</summary>
    public string LoadProfileText() => IsInitialized ? Files[CharacterFile.ProfileText].Read(File.ReadAllText, string.Empty) : string.Empty;

    /// <summary>Saves the player's own profile text to profile.txt.</summary>
    public SaveResult SaveProfileText(string text)
    {
        if (IsInitialized)
            return Files[CharacterFile.ProfileText].Save(path => File.WriteAllText(path, text));

        DataDiagnostics.Log?.Invoke("[data] did not save profile.txt: no character is open");

        return SaveResult.Failed;
    }

    /// <summary>
    ///     Loads the player's own portrait, <c>{name}.jpg</c> or a file named <c>{name}</c>, from the character folder, as
    ///     retail does (BRIG-106). Gives no bytes when there is no portrait. Returns false when a portrait file exists but
    ///     cannot be read, so that the caller keeps the portrait it has. The name comes from the server, so
    ///     <see cref="TryGetFilePath" /> refuses a name that leaves the character folder.
    /// </summary>
    public bool TryLoadPortrait(string name, out byte[] portrait)
    {
        portrait = [];

        if (string.IsNullOrEmpty(name))
            return true;

        foreach (var fileName in new[] { $"{name}.jpg", name })
        {
            if (!TryGetFilePath(fileName, out var path))
                continue;

            byte[]? bytes = null;

            if (!DataDiagnostics.Try(() => bytes = File.Exists(path) ? File.ReadAllBytes(path) : null, $"read {fileName}"))
                return false;

            if (bytes is not null)
            {
                portrait = bytes;

                return true;
            }
        }

        return true;
    }
}