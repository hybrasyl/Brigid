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
            //any unexpected failure → caller falls back to the typed field
        }

        return null;
    }

    private static string? PickMacOs(string prompt, string? initialDirectory)
    {
        //AppleScript 'choose folder' returns an alias; convert to a POSIX path. ArgumentList avoids shell quoting; both
        //the prompt and the directory are sanitized so neither can break out of the AppleScript string literal.
        var location = !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)
            ? $" default location (POSIX file \"{Sanitize(initialDirectory)}\")"
            : string.Empty;

        TryRun("osascript", ["-e", $"POSIX path of (choose folder with prompt \"{Sanitize(prompt)}\"{location})"], out var path);

        return path;
    }

    private static string? PickWindows(string prompt)
    {
        //STA is required for Windows Forms dialogs; emit the selected path on stdout, nothing on cancel.
        var script =
            "Add-Type -AssemblyName System.Windows.Forms; "
            + "$d = New-Object System.Windows.Forms.FolderBrowserDialog; "
            + $"$d.Description = '{Sanitize(prompt)}'; "
            + "if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($d.SelectedPath) }";

        TryRun("powershell", ["-NoProfile", "-STA", "-Command", script], out var path);

        return path;
    }

    private static string? PickLinux(string prompt, string? initialDirectory)
    {
        var zenityArgs = new List<string> { "--file-selection", "--directory", $"--title={prompt}" };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            zenityArgs.Add($"--filename={initialDirectory.TrimEnd('/')}/");

        //only fall through to kdialog when zenity is *absent* (TryRun returns false); a zenity cancel must not pop kdialog
        if (TryRun("zenity", zenityArgs, out var path))
            return path;

        TryRun("kdialog", ["--getexistingdirectory", initialDirectory ?? "."], out var kdialogPath);

        return kdialogPath;
    }

    /// <summary>
    ///     Runs <paramref name="fileName" /> and captures stdout as a path. Returns <c>false</c> only when the process
    ///     could not be started (tool missing) — so callers can try a fallback tool; returns <c>true</c> when it ran, with
    ///     <paramref name="path" /> set to the trimmed output or <c>null</c> for a non-zero exit (cancel) or empty output.
    /// </summary>
    private static bool TryRun(string fileName, IReadOnlyList<string> arguments, out string? path)
    {
        path = null;

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

        Process? process;

        try
        {
            process = Process.Start(startInfo);
        } catch
        {
            //tool not installed / cannot launch → let the caller try the next option
            return false;
        }

        if (process is null)
            return false;

        using (process)
        {
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                return true; //ran, but the user cancelled — do not fall through to another tool

            var trimmed = output.Trim();
            path = string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;

            return true;
        }
    }

    //strip the few characters that would break the embedded osascript/powershell string literal
    private static string Sanitize(string text) => text.Replace("\"", string.Empty).Replace("'", string.Empty);
}
