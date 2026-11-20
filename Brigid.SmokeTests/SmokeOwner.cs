using System.Diagnostics;

namespace Brigid.SmokeTests;

/// <summary>
///     The owner of a smoke run's temporary folders, files and compose projects. Each name is
///     <c>brigid-smoke-&lt;process id&gt;-&lt;id&gt;</c>, with the process id of the test host, so a run removes only what
///     a stopped run left: a second run on the same machine does not remove the stack or the files of a run that still
///     goes.
/// </summary>
internal static class SmokeOwner
{
    /// <summary>The start of every smoke name.</summary>
    internal const string PREFIX = "brigid-smoke-";

    /// <summary>A new name of this test host: <c>brigid-smoke-&lt;process id&gt;-</c> and <paramref name="hexDigits" /> of a GUID.</summary>
    internal static string NewName(int hexDigits) => $"{PREFIX}{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..hexDigits]}";

    /// <summary>The process id in a smoke name, or null when the name has none (a name from before the process id).</summary>
    internal static int? OwnerOf(string name)
    {
        if (!name.StartsWith(PREFIX, StringComparison.Ordinal))
            return null;

        var rest = name.AsSpan(PREFIX.Length);
        var dash = rest.IndexOf('-');

        return (dash > 0) && int.TryParse(rest[..dash], out var id) ? id : null;
    }

    /// <summary>
    ///     True when a run that a stopped test host left owns <paramref name="name" />: its process does not run. A name with
    ///     no process id is stale when <paramref name="lastWrite" /> is older than <paramref name="staleAge" />.
    /// </summary>
    internal static bool IsStale(string name, DateTime lastWrite, TimeSpan staleAge)
        => OwnerOf(name) is { } owner ? !Runs(owner) : lastWrite < DateTime.UtcNow - staleAge;

    /// <summary>True when the process with <paramref name="id" /> runs.</summary>
    internal static bool Runs(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);

            return !process.HasExited;
        } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        } catch (System.ComponentModel.Win32Exception)
        {
            //a process that this user cannot query runs
            return true;
        }
    }
}
