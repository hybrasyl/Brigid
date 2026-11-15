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

/// <summary>What a save of a per-character file did.</summary>
public enum SaveResult
{
    /// <summary>The file was written.</summary>
    Saved,

    /// <summary>
    ///     Nothing was written: the last read of the file failed, so the client holds no good copy of it, and empty data
    ///     must not replace the file.
    /// </summary>
    Unreadable,

    /// <summary>
    ///     Nothing was written: the file changed on disk after the client read it, so a hand edit is kept. The caller reads
    ///     the file again into its window.
    /// </summary>
    ChangedOnDisk,

    /// <summary>The write failed, and the failure was logged.</summary>
    Failed
}

/// <summary>
///     Manages per-character config files (macros, chants, friends, family) stored per-server, per-character under
///     <c>{AppPaths.ProfilesDir}/{serverKey}/{characterName}</c> — a writable per-user location, keyed by server so the
///     same character name on different servers no longer shares one folder. Legacy config from the old in-game-folder
///     location is imported once on init.
/// </summary>
/// <remarks>
///     Each read goes through <see cref="DataDiagnostics.Try" />: a file that another program holds, or that is denied,
///     loads as empty and logs one line (BRIG-177). The repository records each file's last-write time when it reads or
///     saves the file. A save refuses a file whose last read failed, until a read of it succeeds, so that empty data
///     never replaces good data; and it refuses a file that changed on disk after the read, so that a hand edit is not
///     lost. <see cref="HasChangedOnDisk" /> reports a hand edit, and also a file whose last read failed, so that each
///     place that uses the data tries that file again.
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

    //the read state of each file of this character, by file name
    private readonly Dictionary<string, FileState> States = new(StringComparer.Ordinal);

    private sealed class FileState
    {
        public bool Loaded;
        public bool Unreadable;
        public DateTime? WriteTimeUtc;
    }

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
        States.Clear();
        DataDiagnostics.Try(() => Directory.CreateDirectory(PlayerDirectory), "create profile dir");
    }

    /// <summary>Opens the repository on a folder, with no legacy import and no default files, for the tests.</summary>
    internal void OpenForTests(string directory) => Open(directory);

    /// <summary>
    ///     True when the file's last-write time on disk differs from the time that the last read or save of it recorded,
    ///     so a hand edit changed it, and true while the last read of it failed, so that the caller tries it again (a lock
    ///     that another program held at world entry can end with no write). False for a file that was not read yet.
    /// </summary>
    public bool HasChangedOnDisk(CharacterFile file)
    {
        if (!IsInitialized)
            return false;

        var name = FileNameOf(file);

        return States.TryGetValue(name, out var state)
               && state.Loaded
               && (state.Unreadable || (WriteTimeOf(PathOf(name)) != state.WriteTimeUtc));
    }

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

    private string PathOf(string fileName) => Path.Combine(PlayerDirectory, fileName);

    private FileState StateOf(string fileName)
    {
        if (!States.TryGetValue(fileName, out var state))
        {
            state = new FileState();
            States[fileName] = state;
        }

        return state;
    }

    //null when the file is missing or its time cannot be read
    private static DateTime? WriteTimeOf(string path)
    {
        DateTime? time = null;
        DataDiagnostics.Try(() => time = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null, $"time of {Path.GetFileName(path)}");

        return time;
    }

    //reads one file through DataDiagnostics.Try. A missing file reads as empty; a file that cannot be read reads as empty
    //and is marked unreadable, so its save is refused. The time is taken before the read: a hand edit during the read then
    //shows as a change, and the next use reads the file again
    private T Read<T>(string fileName, Func<string, T> read, T empty)
    {
        var path = PathOf(fileName);
        var timeBeforeRead = WriteTimeOf(path);
        var value = empty;
        var ok = DataDiagnostics.Try(
            () =>
            {
                if (File.Exists(path))
                    value = read(path);
            }, $"read {fileName}");

        var state = StateOf(fileName);
        state.Loaded = true;
        state.Unreadable = !ok;
        state.WriteTimeUtc = timeBeforeRead;

        return ok ? value : empty;
    }

    private SaveResult Write(string fileName, Action<string> write)
    {
        var path = PathOf(fileName);
        var state = StateOf(fileName);

        if (state.Unreadable)
        {
            DataDiagnostics.Log?.Invoke($"[data] did not save {fileName}: the last read of it failed");

            return SaveResult.Unreadable;
        }

        if (state.Loaded && (WriteTimeOf(path) != state.WriteTimeUtc))
        {
            DataDiagnostics.Log?.Invoke($"[data] did not save {fileName}: it changed on disk after the client read it");

            return SaveResult.ChangedOnDisk;
        }

        if (!DataDiagnostics.Try(() => write(path), $"save {fileName}"))
            return SaveResult.Failed;

        state.Loaded = true;
        state.WriteTimeUtc = WriteTimeOf(path);

        return SaveResult.Saved;
    }

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
        var lines = Read(FAMILY_LIST_FILE, File.ReadAllLines, []);

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
        => Read(FRIEND_LIST_FILE, File.ReadAllLines, [])
           .Where(line => !string.IsNullOrWhiteSpace(line))
           .Take(20)
           .ToList();

    /// <summary>
    ///     Loads macro text from Macro.cfg. Returns a 10-element array of macro strings.
    /// </summary>
    public string[] LoadMacros() => ParseMacros(Read(MACRO_FILE, File.ReadAllLines, []));

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

    public List<SkillChantEntry> LoadSkillChants() => ParseChantBook(Read(SKILL_BOOK_FILE, File.ReadAllText, string.Empty));

    /// <summary>
    ///     Loads spell chant entries from SpellBook.cfg.
    /// </summary>
    /// <remarks>
    ///     File format: LF-delimited, first line is a header, then repeating groups of (spell name, 10 "SpellN:" chant lines).
    /// </remarks>
    public List<SpellChantEntry> LoadSpellChants()
    {
        var entries = new List<SpellChantEntry>();
        var lines = Read(SPELL_BOOK_FILE, File.ReadAllText, string.Empty)
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
    private SaveResult SaveChantBook(string fileName, List<SkillChantEntry> entries) => Write(
        fileName,
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

        return Write(FAMILY_LIST_FILE, path => File.WriteAllLines(path, lines));
    }

    /// <summary>
    ///     Saves friend names to Friendlist.cfg.
    /// </summary>
    public SaveResult SaveFriendList(List<string> names)
        => Write(FRIEND_LIST_FILE, path => File.WriteAllLines(path, names.Take(20)));

    /// <summary>
    ///     Saves macro text to Macro.cfg.
    /// </summary>
    public SaveResult SaveMacros(string[] macros)
    {
        var lines = FormatMacros(macros);

        return Write(MACRO_FILE, path => File.WriteAllLines(path, lines));
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

    public SaveResult SaveSkillChants(List<SkillChantEntry> entries) => SaveChantBook(SKILL_BOOK_FILE, entries);

    /// <summary>
    ///     Saves spell chant entries to SpellBook.cfg.
    /// </summary>
    public SaveResult SaveSpellChants(List<SpellChantEntry> entries) => Write(
        SPELL_BOOK_FILE,
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
    public string LoadProfileText() => IsInitialized ? Read(PROFILE_TEXT_FILE, File.ReadAllText, string.Empty) : string.Empty;

    /// <summary>Saves the player's own profile text to profile.txt.</summary>
    public SaveResult SaveProfileText(string text)
    {
        if (IsInitialized)
            return Write(PROFILE_TEXT_FILE, path => File.WriteAllText(path, text));

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