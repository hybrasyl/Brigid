#region
using System.Diagnostics;
#endregion

namespace Brigid.Utilities;

/// <summary>
///     Opens URLs in the OS default browser. Best-effort: failures are swallowed — the game must never crash
///     because a browser could not be launched.
/// </summary>
public static class Browser
{
    public static void Open(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(
                new ProcessStartInfo(url)
                {
                    UseShellExecute = true
                });
        } catch
        {
            //could not open browser
        }
    }
}
