using Brigid.Data;
using Xunit;

namespace Brigid.Tests;

// DataDiagnostics.Try is the guard that keeps a denied/locked/full file write from terminating the client (the
// Program-Files UnauthorizedAccessException crash). It swallows only IO/permission failures, logs them via the wired
// sink, and lets genuine bugs (non-IO exceptions) propagate.
public class DataDiagnosticsTests
{
    [Fact]
    public void Try_Success_ReturnsTrue()
        => Assert.True(DataDiagnostics.Try(() => { }, "noop"));

    [Fact]
    public void Try_UnauthorizedAccess_IsSwallowedAndLogged()
    {
        string? logged = null;
        DataDiagnostics.Log = msg => logged = msg;

        try
        {
            var result = DataDiagnostics.Try(
                () => throw new UnauthorizedAccessException("denied"),
                "SaveFamilyList");

            Assert.False(result);
            Assert.NotNull(logged);
            Assert.Contains("SaveFamilyList", logged);
        } finally
        {
            DataDiagnostics.Log = null;
        }
    }

    [Fact]
    public void Try_IoException_IsSwallowed()
        => Assert.False(DataDiagnostics.Try(() => throw new IOException("locked"), "SaveMacros"));

    [Fact]
    public void Try_NonIoException_Propagates()
        => Assert.Throws<InvalidOperationException>(
            () => DataDiagnostics.Try(() => throw new InvalidOperationException("bug"), "test"));
}
