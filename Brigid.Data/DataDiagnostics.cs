namespace Brigid.Data;

/// <summary>
///     Minimal diagnostic seam for the data layer. <c>Brigid.Data</c> sits below <c>Brigid.Networking</c> (where
///     <c>NoticeDebugLog</c> lives), so it can't log directly; the client wires <see cref="Log" /> to its logger at
///     startup. Used to report best-effort IO failures that are caught (never rethrown) so a read-only/locked path can
///     never crash the client.
/// </summary>
public static class DataDiagnostics
{
    /// <summary>Sink for data-layer diagnostics. Null until the client wires it (e.g. to <c>NoticeDebugLog</c>).</summary>
    public static Action<string>? Log { get; set; }

    /// <summary>
    ///     Runs a file operation, catching (and logging) only IO/permission failures so the caller never crashes on a
    ///     denied/locked/full write. Non-IO exceptions propagate. Returns true on success.
    /// </summary>
    public static bool Try(Action io, string what)
    {
        try
        {
            io();

            return true;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log?.Invoke($"[data] IO failed ({what}): {ex.GetType().Name}: {ex.Message}");

            return false;
        }
    }
}
