#region
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
#endregion

namespace Brigid.Data;

/// <summary>
///     The one rule for a file name that Brigid makes from data it does not control: a server-sent metafile name, a
///     character name. A safe name is a bare file name that names the same file on every platform (BRIG-111).
/// </summary>
public static class FileNames
{
    //the characters Windows refuses in a file name, including both separators. They are refused on every platform, so
    //a name that one platform accepts, every platform accepts.
    private static readonly char[] InvalidChars = ['"', '<', '>', '|', ':', '*', '?', '\\', '/', .. Enumerable.Range(0, 32).Select(i => (char)i)];

    //Windows opens a device, not a file, for these names, with or without an extension. The superscript digits are
    //reserved too.
    private static readonly FrozenSet<string> ReservedNames = new[]
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "LPT¹", "LPT²", "LPT³"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Returns true only for a bare file name that names the same file on every platform: not empty, no separator, no
    ///     character that Windows refuses, not rooted, no trailing dot or space, and not a Windows device name.
    /// </summary>
    public static bool IsSafeName([NotNullWhen(true)] string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || (name.IndexOfAny(InvalidChars) >= 0))
            return false;

        //Windows removes a trailing dot or space, so the file on disk gets another name. This also refuses "." and "..".
        if (name.EndsWith('.') || name.EndsWith(' '))
            return false;

        if (Path.IsPathRooted(name) || (Path.GetFileName(name) != name))
            return false;

        //Windows also removes the spaces before the extension, so "CON .txt" opens the device
        return !ReservedNames.Contains(name.Split('.')[0].TrimEnd(' '));
    }

    /// <summary>
    ///     Makes a safe name (<see cref="IsSafeName" />) from <paramref name="name" />: removes the characters that Windows
    ///     refuses, the outer spaces and the trailing dots, and puts <c>_</c> before a Windows device name. An empty result
    ///     is <c>_</c>. A name that is safe does not change.
    /// </summary>
    public static string ToSafeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "_";

        var cleaned = new string(name.Where(c => Array.IndexOf(InvalidChars, c) < 0).ToArray()).Trim().TrimEnd('.', ' ');

        if (cleaned.Length == 0)
            return "_";

        //only a device name is still refused here, and the prefix makes it another name
        return IsSafeName(cleaned) ? cleaned : "_" + cleaned;
    }
}
