#region
using System.Reflection;
#endregion

namespace Brigid.Utilities;

/// <summary>
///     Single source of the displayable app version, shared by the window title and the login screen's version label so
///     they can never show different strings for the same build. Resolved once from the generated
///     <see cref="AssemblyInformationalVersionAttribute" />.
/// </summary>
internal static class VersionInfo
{
    public static string Display { get; } = Resolve(
        typeof(VersionInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        typeof(VersionInfo).Assembly.GetName().Version);

    /// <summary>
    ///     Pure, tool-agnostic parse split out for unit testing. Both Nerdbank.GitVersioning and MinVer emit a SemVer2
    ///     <c>AssemblyInformationalVersion</c> of the form <c>MAJOR.MINOR.PATCH[-prerelease][+buildmetadata]</c>. We keep
    ///     the prerelease tag (dev/feature builds want it) and drop only the <c>+buildmetadata</c> (git SHA / height), so
    ///     a future versioning-tool swap (Nerdbank↔MinVer) needs no change here. Falls back to the 3-part assembly
    ///     version, then <c>0.0.0</c>, when the informational attribute is absent or degenerate.
    /// </summary>
    internal static string Resolve(string? informationalVersion, Version? assemblyVersion)
    {
        if (!string.IsNullOrEmpty(informationalVersion))
        {
            var plus = informationalVersion.IndexOf('+');
            var core = plus < 0 ? informationalVersion : informationalVersion[..plus];

            //guard the degenerate "+metadata-only" case (no version before the '+') — fall through to the assembly version
            if (core.Length > 0)
                return core;
        }

        return assemblyVersion?.ToString(3) ?? "0.0.0";
    }
}
