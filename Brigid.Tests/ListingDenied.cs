using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;

namespace Brigid.Tests;

/// <summary>Refuses the listing of a folder to this process until it is disposed.</summary>
internal sealed class ListingDenied : IDisposable
{
    private readonly string Directory;
    private readonly FileSystemAccessRule? Rule;

    /// <summary>False when the OS still lists the folder, for example for a root user.</summary>
    public bool Works { get; }

    public ListingDenied(string directory)
    {
        Directory = directory;

        if (OperatingSystem.IsWindows())
        {
            var user = WindowsIdentity.GetCurrent().User!;
            Rule = new FileSystemAccessRule(user, FileSystemRights.ListDirectory, AccessControlType.Deny);
            var info = new DirectoryInfo(directory);
            var security = info.GetAccessControl();
            security.AddAccessRule(Rule);
            info.SetAccessControl(security);
        } else
            File.SetUnixFileMode(directory, UnixFileMode.UserExecute);

        Works = !CanList(directory);
    }

    private static bool CanList(string directory)
    {
        try
        {
            System.IO.Directory.GetFiles(directory);

            return true;
        } catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            var info = new DirectoryInfo(Directory);
            var security = info.GetAccessControl();
            security.RemoveAccessRule(Rule!);
            info.SetAccessControl(security);
        } else
            File.SetUnixFileMode(Directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}

/// <summary>
///     A test that needs a folder that this process cannot list. A root user lists every folder, so the test is skipped
///     there, and does not pass with nothing tested.
/// </summary>
public sealed class ListingDenialFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> CanDeny = new(Probe);

    public ListingDenialFactAttribute()
    {
        if (!CanDeny.Value)
            Skip = "This user can list a folder that refuses the listing (a root user), so the test cannot deny it.";
    }

    private static bool Probe()
    {
        var directory = Path.Combine(Path.GetTempPath(), "brigid-deny-probe-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);

        try
        {
            using var denied = new ListingDenied(directory);

            return denied.Works;
        } finally
        {
            System.IO.Directory.Delete(directory);
        }
    }
}
