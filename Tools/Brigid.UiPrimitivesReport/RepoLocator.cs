namespace Brigid.UiPrimitivesReport;

/// <summary>Finds the repo root by walking up from the running tool's base directory until <c>Brigid.slnx</c> is found.</summary>
internal static class RepoLocator
{
    public static string? Discover()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Brigid.slnx")))
                return dir.FullName;

            dir = dir.Parent;
        }

        return null;
    }
}
