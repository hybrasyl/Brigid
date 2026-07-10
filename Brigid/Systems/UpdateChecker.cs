#region
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Chaos.Extensions.Common;
#endregion

namespace Brigid.Systems;

/// <summary>
///     Best-effort startup check for a newer release on GitHub. <see cref="BeginCheck" /> fires one background
///     request; on success with a newer remote tag, <see cref="Available" /> is set (atomically, via reference
///     publication of an immutable record) for the UI to poll. Failures of any kind — offline, rate-limited,
///     malformed response — leave <see cref="Available" /> null and are never surfaced.
/// </summary>
public static class UpdateChecker
{
    //volatile: written once from a thread-pool task, polled per-frame from the game loop
    private static volatile UpdateInfo? AvailableInfo;

    /// <summary>
    ///     The newer release found by the startup check, or null (not yet checked / up to date / check failed).
    /// </summary>
    public static UpdateInfo? Available => AvailableInfo;

    public static void BeginCheck()
        => _ = Task.Run(async () =>
        {
            try
            {
                await CheckAsync();
            } catch
            {
                //best-effort: no update notice is ever worth an error
            }
        });

    private static async Task CheckAsync()
    {
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(5);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Brigid");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        using var doc = JsonDocument.Parse(await http.GetStringAsync(GlobalSettings.LatestReleaseApiUrl));

        if (!doc.RootElement.TryGetProperty("tag_name", out var tagName))
            return;

        var tag = tagName.GetString();

        if (string.IsNullOrEmpty(tag) || !IsNewer(tag, GetCurrentVersion()))
            return;

        var url = doc.RootElement.TryGetProperty("html_url", out var htmlUrl)
            ? htmlUrl.GetString() ?? GlobalSettings.ReleasesUrl
            : GlobalSettings.ReleasesUrl;

        AvailableInfo = new UpdateInfo(tag, url);
    }

    /// <summary>
    ///     Informational version without +commit metadata (e.g. "0.1.0" or "1.0.0-alpha1"). Also consumed by the
    ///     start-screen version label so the displayed version and the compared version can never drift.
    /// </summary>
    public static string GetCurrentVersion()
    {
        var info = typeof(UpdateChecker).Assembly
                                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                        ?.InformationalVersion;

        if (string.IsNullOrEmpty(info))
            return typeof(UpdateChecker).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        var metadata = info.IndexOf('+');

        return metadata < 0 ? info : info[..metadata];
    }

    /// <summary>
    ///     Whether a remote release tag (e.g. "v0.2.0") is newer than a local version string (e.g. "0.1.0" or
    ///     "1.0.0-alpha1"). Numeric cores are compared numerically; a stable release supersedes a local prerelease
    ///     of the same core. Two prereleases of the same core are treated as equal (Brigid releases are tagged
    ///     stable, so prerelease ordering is deliberately not modeled). Unparseable versions fall back to plain
    ///     inequality.
    /// </summary>
    public static bool IsNewer(string remoteTag, string localVersion)
    {
        var remote = remoteTag.TrimStart('v', 'V');
        var remoteCore = ParseCore(remote);
        var localCore = ParseCore(localVersion);

        //unparseable on either side: fall back to plain inequality
        if ((remoteCore is null) || (localCore is null))
            return !remote.EqualsI(localVersion);

        if (remoteCore != localCore)
            return remoteCore > localCore;

        //same numeric core: a stable release supersedes a local prerelease
        return !HasPrerelease(remote) && HasPrerelease(localVersion);
    }

    private static bool HasPrerelease(string version) => version.Contains('-');

    private static Version? ParseCore(string version)
    {
        var end = version.IndexOfAny(['-', '+']);
        var core = end < 0 ? version : version[..end];

        if (!core.Contains('.'))
            core += ".0";

        return Version.TryParse(core, out var parsed) ? parsed : null;
    }
}

/// <summary>
///     A newer release found on GitHub: the tag as published (e.g. "v0.2.0") and the release page URL.
/// </summary>
public sealed record UpdateInfo(string Tag, string Url);
