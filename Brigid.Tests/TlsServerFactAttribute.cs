using Xunit;

namespace Brigid.Tests;

/// <summary>
///     A test that needs a loopback <c>SslStream</c> server to complete a TLS 1.3 handshake. macOS's server-side TLS
///     stack caps at TLS 1.2 (the Network.framework switch only covers client connections), so these tests are skipped
///     there rather than failing on a platform limit. They run on Windows and Linux.
/// </summary>
public sealed class TlsServerFactAttribute : FactAttribute
{
    public TlsServerFactAttribute()
    {
        if (OperatingSystem.IsMacOS())
            Skip = "macOS cannot host a TLS 1.3 server; covered on Windows and Linux.";
    }
}
