namespace Brigid.Data;

/// <summary>
///     The kind of entry that <see cref="DirectoryEntries.FindName" /> looks at.
/// </summary>
internal enum DirectoryEntryKind
{
    /// <summary>Files and directories.</summary>
    Any,

    /// <summary>Files only.</summary>
    Files,

    /// <summary>Directories only.</summary>
    Directories
}

/// <summary>
///     The one match of a directory entry by name (BRIG-111). Each caller gives its own case rule: a Dark Ages data
///     directory does not agree on letter case (<see cref="DataPathResolver" />), and the folder migration of
///     <see cref="AppPaths" /> must tell <c>erisco</c> from <c>Erisco</c>.
/// </summary>
internal static class DirectoryEntries
{
    /// <summary>
    ///     Returns the name on disk of the entry in <paramref name="directory" /> that matches <paramref name="name" />
    ///     under <paramref name="comparison" />, or null when there is none or the directory does not exist. A listing
    ///     that fails throws; the caller guards it.
    /// </summary>
    public static string? FindName(
        string directory,
        string name,
        StringComparison comparison,
        DirectoryEntryKind kind = DirectoryEntryKind.Any)
    {
        if (!Directory.Exists(directory))
            return null;

        var entries = kind switch
        {
            DirectoryEntryKind.Files       => Directory.EnumerateFiles(directory),
            DirectoryEntryKind.Directories => Directory.EnumerateDirectories(directory),
            _                              => Directory.EnumerateFileSystemEntries(directory)
        };

        return FirstMatch(entries.Select(Path.GetFileName), name, comparison);
    }

    /// <summary>
    ///     Returns the first of <paramref name="names" /> in ordinal order that matches <paramref name="name" />, or
    ///     null. When more than one matches (a case-sensitive file system with <c>Legend.dat</c> and
    ///     <c>legend.dat</c>), the ordinal order makes the choice the same whatever the order of the listing.
    /// </summary>
    public static string? FirstMatch(IEnumerable<string?> names, string name, StringComparison comparison)
        => names.Order(StringComparer.Ordinal).FirstOrDefault(entry => string.Equals(entry, name, comparison));
}
