namespace Brigid.Data;

/// <summary>
///     Finds a file under the Dark Ages data directory by name, whatever the case of that name on disk. Dark Ages data
///     directories do not agree on case: the official installer writes <c>Legend.dat</c>, and an unpacker can write
///     <c>legend.dat</c>. Windows and macOS hide the difference, but a case-sensitive Linux file system does not.
/// </summary>
public static class DataPathResolver
{
    /// <summary>
    ///     Joins <paramref name="segments" /> under <paramref name="root" />. A segment that exists as given is kept as
    ///     given, so on Windows and macOS the path does not change. Otherwise the segment takes the spelling of the first
    ///     entry on disk that matches it when case is ignored. A segment that does not exist is kept as given, so a
    ///     missing file still fails at the caller, with its usual error.
    /// </summary>
    public static string Resolve(string root, params ReadOnlySpan<string> segments)
    {
        var current = root;

        foreach (var segment in segments)
        {
            var asGiven = Path.Combine(current, segment);

            current = Path.Exists(asGiven) ? asGiven : Path.Combine(current, FindIgnoringCase(current, segment) ?? segment);
        }

        return current;
    }

    private static string? FindIgnoringCase(string directory, string name)
    {
        if (!Directory.Exists(directory))
            return null;

        return Directory.EnumerateFileSystemEntries(directory)
                        .Select(Path.GetFileName)
                        .Order(StringComparer.Ordinal)
                        .FirstOrDefault(entry => string.Equals(entry, name, StringComparison.OrdinalIgnoreCase));
    }
}
