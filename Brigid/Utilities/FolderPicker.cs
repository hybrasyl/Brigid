#region
using System.Diagnostics;
#endregion

namespace Brigid.Utilities;

/// <summary>
///     Cross-platform native folder chooser. Shells out to the operating system's own dialog tool — <c>osascript</c>
///     (macOS), <c>zenity</c>/<c>kdialog</c> (Linux), or PowerShell's <c>FolderBrowserDialog</c> (Windows) — so there is
///     no bundled/signed native binary to maintain. Returns the chosen absolute path, or <c>null</c> if the user
///     cancelled, the tool is unavailable, or anything failed (callers then fall back to a typed path field).
///     <para>
///         The dialog runs synchronously and blocks the calling thread until dismissed; that is acceptable for a modal
///         picker invoked from a button press.
///     </para>
/// </summary>
public static class FolderPicker
{
    public static string? Pick(string prompt, string? initialDirectory = null)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
                return PickMacOs(prompt, initialDirectory);

            if (OperatingSystem.IsWindows())
                return PickWindows(prompt);

            if (OperatingSystem.IsLinux())
                return PickLinux(prompt, initialDirectory);
        } catch
        {
            //any failure (tool missing, spawn error) → caller falls back to the typed field
        }

        return null;
    }

    private static string? PickMacOs(string prompt, string? initialDirectory)
    {
        //AppleScript 'choose folder' returns an alias; convert to a POSIX path. ArgumentList avoids shell quoting.
        var location = !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)
            ? $" default location (POSIX file \"{initialDirectory}\")"
            : string.Empty;

        return RunForPath(
            "osascript",
            ["-e", $"POSIX path of (choose folder with prompt \"{Sanitize(prompt)}\"{location})"]);
    }

    private static string? PickWindows(string prompt)
    {
        //STA is required for Windows Forms dialogs; emit the selected path on stdout, nothing on cancel.
        var script =
            "Add-Type -AssemblyName System.Windows.Forms; "
            + "$d = New-Object System.Windows.Forms.FolderBrowserDialog; "
            + $"$d.Description = '{Sanitize(prompt)}'; "
            + "if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($d.SelectedPath) }";

        return RunForPath("powershell", ["-NoProfile", "-STA", "-Command", script]);
    }

    private static string? PickLinux(string prompt, string? initialDirectory)
    {
        var zenityArgs = new List<string> { "--file-selection", "--directory", $"--title={prompt}" };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            zenityArgs.Add($"--filename={initialDirectory.TrimEnd('/')}/");

        return RunForPath("zenity", zenityArgs)
               ?? RunForPath("kdialog", ["--getexistingdirectory", initialDirectory ?? "."]);
    }

    private static string? RunForPath(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo);

        if (process is null)
            return null;

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            return null;

        var path = output.Trim();

        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    //strip the few characters that would break the embedded osascript/powershell string literal
    private static string Sanitize(string text) => text.Replace("\"", string.Empty).Replace("'", string.Empty);
}
