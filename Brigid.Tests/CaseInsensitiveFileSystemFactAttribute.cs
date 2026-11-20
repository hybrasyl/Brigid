using Xunit;

namespace Brigid.Tests;

/// <summary>
///     A test of a file that the file system finds under a name in another case (Windows, and macOS by default). On a
///     file system that keeps the two names apart (Linux), the test is skipped.
/// </summary>
public sealed class CaseInsensitiveFileSystemFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> IgnoresCase = new(Probe);

    public CaseInsensitiveFileSystemFactAttribute()
    {
        if (!IgnoresCase.Value)
            Skip = "The temporary folder keeps names apart by case, so a name in another case is another file.";
    }

    private static bool Probe()
    {
        var directory = Directory.CreateTempSubdirectory("brigid-case-probe-").FullName;

        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Probe"), []);

            return File.Exists(Path.Combine(directory, "PROBE"));
        } finally
        {
            Directory.Delete(directory, true);
        }
    }
}
