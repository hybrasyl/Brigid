#region
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Text;
using Brigid.Data.Models;
using Chaos.Extensions.Common;
using DALib.Data;
using DALib.Extensions;
#endregion

namespace Brigid.Data.Repositories;

public sealed class MetaFileRepository
{
    private static readonly Encoding KoreanEncoding = Encoding.GetEncoding(949);

    private static readonly FileStreamOptions ReadOptions = new()
    {
        Access = FileAccess.Read,
        Mode = FileMode.Open,
        Options = FileOptions.SequentialScan,
        Share = FileShare.ReadWrite
    };

    //reads the server-pushed metafile cache that MetaFileSync writes; must match its write location
    //(AppPaths.MetaFileDir), which lives under %LOCALAPPDATA% and is scoped per server. Resolved per access, not
    //captured, because this repository is constructed before the connection target is known.
    private readonly Func<string> DirectoryOf;
    private string MetaFileDirectory => DirectoryOf();
    //item name hash to the name of the ItemInfo file that holds it
    private FrozenDictionary<int, string>? ItemIndex;
    private NpcIllustrationMetadata? NpcIllustrationMetadataCache;

    //the names of the last checksum list, or null before the first list of a server
    private volatile ListedFiles? Listed;

    //a scan of the folder compares a file name exactly, as the prune does, so the repository reads the files that the
    //prune keeps. A lookup by a fixed name (Light, SClass1) finds the listed name in any case, and reads the file under
    //the listed name, which is the name that the sync writes
    private sealed record ListedFiles(FrozenSet<string> Exact, FrozenDictionary<string, string> AnyCase);

    public MetaFileRepository() : this(() => AppPaths.MetaFileDir) { }

    /// <summary>A repository that reads the metafiles in <paramref name="directory" />, for the tests.</summary>
    internal MetaFileRepository(string directory) : this(() => directory) { }

    private MetaFileRepository(Func<string> directory) => DirectoryOf = directory;

    /// <summary>
    ///     Sets the names of the server's last checksum list. The repository then reads only those files, so a stale file
    ///     that the sync could not delete is not read (BRIG-167).
    /// </summary>
    public void SetListedNames(IReadOnlyCollection<string> names)
        => Listed = new ListedFiles(
            names.ToFrozenSet(StringComparer.Ordinal),
            names.DistinctBy(name => name, StringComparer.OrdinalIgnoreCase)
                 .ToFrozenDictionary(name => name, name => name, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    ///     Forgets the names of the last checksum list, so the repository reads every file in the folder, as before the
    ///     first list of a server. Call it at a server change.
    /// </summary>
    public void ForgetListedNames() => Listed = null;

    //for a scan of the folder
    private bool IsListed(string fileName) => Listed is not { } listed || listed.Exact.Contains(fileName);

    //for a lookup by name: the name of the file to read, or null when the list does not name it
    private string? ListedNameFor(string name)
        => Listed is not { } listed ? name
            : listed.AnyCase.TryGetValue(name, out var listedName) ? listedName
            : null;

    /// <summary>
    ///     Builds the item name-to-file index for fast lookups. Must be called once after metadata files are synced to disk.
    /// </summary>
    public void BuildItemIndex()
    {
        var index = new ConcurrentDictionary<int, string>();

        if (!Directory.Exists(MetaFileDirectory))
        {
            ItemIndex = index.ToFrozenDictionary();

            return;
        }

        var files = Directory.GetFiles(MetaFileDirectory)
                             .Select(f => (Path: f, Name: Path.GetFileName(f)))
                             .Where(f => f.Name.StartsWithI("ItemInfo") && IsListed(f.Name))
                             .Where(f => ushort.TryParse(f.Name.AsSpan("ItemInfo".Length), out _))
                             .ToArray();

        Parallel.ForEach(
            files,
            file =>
            {
                if (!TryLoadMetaFile(file.Path, out var metaFile))
                    return;

                foreach (var entry in metaFile)
                    index.TryAdd(StringComparer.OrdinalIgnoreCase.GetHashCode(entry.Key), file.Name);
            });

        ItemIndex = index.ToFrozenDictionary();
    }

    /// <summary>
    ///     Clears the item index and the NPC illustration metadata, which the metafiles give. Call it when the
    ///     metafiles change: after a sync, and when the client changes server (BRIG-105).
    /// </summary>
    public void ClearCaches()
    {
        ItemIndex = null;
        NpcIllustrationMetadataCache = null;
    }

    /// <summary>
    ///     Loads an unparsed MetaFile from the metafile directory by filename.
    /// </summary>
    public MetaFile? Get(string name)
    {
        if (ListedNameFor(name) is not { } fileName)
            return null;

        var filePath = Path.Combine(MetaFileDirectory, fileName);

        if (!File.Exists(filePath))
            return null;

        try
        {
            return MetaFile.FromFile(filePath, true);
        }
        //rule 6 exemption: corrupt asset -> graceful null fallback (no validate-before-parse path)
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Loads and parses skill/spell metadata for a base class.
    /// </summary>
    public AbilityMetadata? GetAbilityMetadata(byte baseClass)
    {
        var metaFile = Get($"SClass{baseClass}");

        if (metaFile is null or { Count: 0 })
            return null;

        return AbilityMetadata.Parse(metaFile);
    }

    /// <summary>
    ///     Returns all metadata files whose names start with the given prefix (e.g., "ItemInfo", "SClass").
    /// </summary>
    public IReadOnlyList<MetaFile> GetAll(string prefix)
    {
        if (!Directory.Exists(MetaFileDirectory))
            return [];

        var results = new List<MetaFile>();

        foreach (var filePath in Directory.GetFiles(MetaFileDirectory))
        {
            var fileName = Path.GetFileName(filePath);

            if (!fileName.StartsWithI(prefix) || !IsListed(fileName))
                continue;

            if (!TryLoadMetaFile(filePath, out var metaFile))
                continue;

            results.Add(metaFile);
        }

        return results;
    }

    /// <summary>
    ///     Loads and parses all event metadata (SEvent1, SEvent2, ...).
    /// </summary>
    public IReadOnlyList<EventMetadataEntry> GetEventMetadata() => EventMetadataEntry.ParseAll(GetAll("SEvent"));

    /// <summary>
    ///     Looks up item metadata for the given item names. Returns a dictionary of found entries keyed by name
    ///     (case-insensitive). Requires <see cref="BuildItemIndex" /> to have been called.
    /// </summary>
    public IDictionary<string, ItemMetadataEntry> GetItemMetadata(params ReadOnlySpan<string> itemNames)
    {
        if (ItemIndex is null || (itemNames.Length == 0))
            return new Dictionary<string, ItemMetadataEntry>(StringComparer.OrdinalIgnoreCase);

        //group requested names by file. An index from an earlier list can name a file that the last list drops, and that
        //file is not read
        var fileGroups = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var name in itemNames)
        {
            var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(name);

            if (!ItemIndex.TryGetValue(hash, out var fileName) || !IsListed(fileName))
                continue;

            if (!fileGroups.TryGetValue(fileName, out var names))
            {
                names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                fileGroups[fileName] = names;
            }

            names.Add(name);
        }

        var results = new ConcurrentDictionary<string, ItemMetadataEntry>(StringComparer.OrdinalIgnoreCase);

        Parallel.ForEach(
            fileGroups,
            group =>
            {
                var filePath = Path.Combine(MetaFileDirectory, group.Key);

                if (!File.Exists(filePath))
                    return;

                try
                {
                    ScanFile(filePath, group.Value, results);
                } catch
                {
                    //skip corrupt files
                }
            });

        return results;
    }

    /// <summary>
    ///     Loads per-map darkness overlay configuration from the "Light" metadata file.
    /// </summary>
    public LightMetadata? GetLightMetadata()
    {
        var metaFile = Get("Light");

        if (metaFile is null or { Count: 0 })
            return null;

        return LightMetadata.Parse(metaFile);
    }

    /// <summary>
    ///     Loads nation ID-to-name mappings from the "NationDesc" metadata file.
    /// </summary>
    public NationMetadata? GetNationMetadata()
    {
        var metaFile = Get("NationDesc");

        if (metaFile is null or { Count: 0 })
            return null;

        return NationMetadata.Parse(metaFile);
    }

    /// <summary>
    ///     Returns merged NPC illustration metadata from both <c>npci.tbl</c> (inside <c>npcbase.dat</c>) and the
    ///     server-pushed <c>NPCIllust</c> metafile. <c>npci.tbl</c> variants occupy the low indices; metafile
    ///     variants are appended after them. Cached after first call, until <see cref="ClearCaches" />.
    /// </summary>
    public NpcIllustrationMetadata GetNpcIllustrationMetadata()
        => NpcIllustrationMetadataCache ??= NpcIllustrationMetadata.Build(DatArchives.Npcbase, Get("NPCIllust"));

    /// <summary>
    ///     Scans a single MetaFile on disk for entries matching the requested names, adding matches to the results dictionary.
    /// </summary>
    private static void ScanFile(string filePath, HashSet<string> names, ConcurrentDictionary<string, ItemMetadataEntry> results)
    {
        using var stream = File.Open(filePath, ReadOptions);
        using var decompressor = new ZLibStream(stream, CompressionMode.Decompress);
        using var reader = new BinaryReader(decompressor, KoreanEncoding, true);

        var remaining = names.Count;
        var entryCount = reader.ReadUInt16(true);

        for (var i = 0; i < entryCount; i++)
        {
            var entryName = reader.ReadString8(KoreanEncoding);
            var propertyCount = reader.ReadUInt16(true);

            if (!names.Contains(entryName))
            {
                for (var j = 0; j < propertyCount; j++)
                {
                    var propLength = reader.ReadUInt16(true);
                    reader.ReadBytes(propLength);
                }

                continue;
            }

            var properties = new string[propertyCount];

            for (var j = 0; j < propertyCount; j++)
                properties[j] = reader.ReadString16(KoreanEncoding, true);

            var parsed = ItemMetadataEntry.ParseEntry(entryName, properties);

            if (parsed is not null)
                results.TryAdd(entryName, parsed);

            if (--remaining == 0)
                return;
        }
    }

    //rule 6 exemption: corrupt asset -> graceful null fallback (no validate-before-parse path)
    private static bool TryLoadMetaFile(string path, [NotNullWhen(true)] out MetaFile? metaFile)
    {
        try
        {
            metaFile = MetaFile.FromFile(path, true);

            return true;
        } catch
        {
            metaFile = null;

            return false;
        }
    }
}