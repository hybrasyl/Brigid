namespace Brigid.Data;

/// <summary>
///     Minimal diagnostic seam for the data layer. <c>Brigid.Data</c> sits below <c>Brigid.Networking</c> (where
///     <c>NoticeDebugLog</c> lives), so it can't log directly; the client wires <see cref="Log" /> to its logger at
///     startup. Used to report best-effort IO failures that are caught (never rethrown) so a read-only/locked path can
///     never crash the client, and by the layers above (Rendering) to report a corrupt asset that is skipped.
/// </summary>
public static class DataDiagnostics
{
    /// <summary>Sink for data-layer diagnostics. Null until the client wires it (e.g. to <c>NoticeDebugLog</c>).</summary>
    public static Action<string>? Log { get; set; }

    /// <summary>
    ///     Runs a file operation, catching (and logging) only IO/permission failures so the caller never crashes on a
    ///     denied/locked/full write. Non-IO exceptions propagate. Returns true on success.
    /// </summary>
    public static bool Try(Action io, string what) => Run(io, what, catchesFileTooLarge: false);

    /// <summary>
    ///     Runs a write of a file, as <see cref="Try" /> does. On Linux a write over the file-size limit (EFBIG) arrives as
    ///     <see cref="ArgumentOutOfRangeException" />, so it is an IO failure here too. Give it only the write: other work
    ///     that throws that exception is a bug, and <see cref="Try" /> lets it through.
    /// </summary>
    public static bool TryWrite(Action write, string what) => Run(write, what, catchesFileTooLarge: true);

    private static bool Run(Action io, string what, bool catchesFileTooLarge)
    {
        try
        {
            io();

            return true;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
                                     || (catchesFileTooLarge && ex is ArgumentOutOfRangeException))
        {
            Log?.Invoke($"[data] IO failed ({what}): {ex.GetType().Name}: {ex.Message}");

            return false;
        }
    }
}
