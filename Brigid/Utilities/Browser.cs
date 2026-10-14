#region
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
#endregion

namespace Brigid.Utilities;

/// <summary>
///     Opens URLs in the OS default browser. Best-effort: failures are swallowed — the game must never crash
///     because a browser could not be launched.
/// </summary>
/// <remarks>
///     The URL can come from a game server (the lobby homepage), and the OS shell runs any string it is given. So
///     only an absolute <c>http</c> or <c>https</c> URL opens. A local path, a UNC path, a <c>file:</c> URI and any
///     other protocol handler are ignored.
/// </remarks>
public static class Browser
{
    public static void Open(string? url)
    {
        if (!TryGetWebUri(url, out var uri))
            return;

        try
        {
            Process.Start(
                new ProcessStartInfo(uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
        } catch
        {
            //could not open browser
        }
    }

    /// <summary>Returns true when the string is an absolute <c>http</c> or <c>https</c> URL.</summary>
    internal static bool TryGetWebUri(string? url, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;

        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var parsed))
            return false;

        if ((parsed.Scheme != Uri.UriSchemeHttp) && (parsed.Scheme != Uri.UriSchemeHttps))
            return false;

        uri = parsed;

        return true;
    }
}
