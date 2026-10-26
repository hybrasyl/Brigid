namespace Brigid.Data;

/// <summary>
///     The kind of entry that <see cref="DirectoryEntries.FindName" /> looks at.
/// </summary>
public enum DirectoryEntryKind
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
public static class DirectoryEntries
{
    /// <summary>
    ///     Returns the name on disk of the entry in <paramref name="directory" /> that matches <paramref name="name" />
    ///     under <paramref name="comparison" />, or null when there is none or the directory does not exist. When more than
    ///     one entry matches (a case-sensitive file system with <c>Legend.dat</c> and <c>legend.dat</c>), the first in
    ///     ordinal order is the result, so the choice does not depend on the order of the listing.
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

        return entries.Select(Path.GetFileName)
                      .Order(StringComparer.Ordinal)
                      .FirstOrDefault(entry => string.Equals(entry, name, comparison));
    }
}
