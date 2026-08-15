#region
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Brigid.Systems;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The trust-on-first-use rule. Exhaustive over (pinned?, matches?, platform verdict) because this
///     is the whole security decision for the extension channel's transport.
/// </summary>
public class CertificateTrustDecisionTests
{
    private const string PIN = "AA:BB:CC";
    private const string OTHER = "11:22:33";

    /// <summary>The dev and self-hosted case: a pin makes an otherwise untrusted certificate usable.</summary>
    [Fact]
    public void PinnedAndMatching_IsAccepted_EvenWhenThePlatformRejectsIt()
        => Assert.Equal(
            CertificateTrustVerdict.Accept,
            CertificateTrustDecision.Decide(PIN, PIN, SslPolicyErrors.RemoteCertificateChainErrors));

    /// <summary>
    ///     The case most likely to be got wrong, and the reason the flagship pin is worth shipping: a
    ///     pinned endpoint presenting a different certificate is refused <em>even though the platform
    ///     accepts it</em>. Without this, a mis-issued but publicly valid certificate for the hostname
    ///     would sail through.
    /// </summary>
    [Fact]
    public void PinnedButDifferent_IsRefused_EvenWhenThePlatformAcceptsIt()
        => Assert.Equal(
            CertificateTrustVerdict.RejectPinMismatch,
            CertificateTrustDecision.Decide(PIN, OTHER, SslPolicyErrors.None));

    /// <summary>A changed certificate is a mismatch, not a first-contact prompt.</summary>
    [Fact]
    public void PinnedButDifferent_IsAMismatchRatherThanUntrusted()
        => Assert.Equal(
            CertificateTrustVerdict.RejectPinMismatch,
            CertificateTrustDecision.Decide(PIN, OTHER, SslPolicyErrors.RemoteCertificateNameMismatch));

    /// <summary>The ordinary public-CA path: no pin needed, no prompt.</summary>
    [Fact]
    public void Unpinned_IsAccepted_WhenThePlatformTrustsIt()
        => Assert.Equal(
            CertificateTrustVerdict.Accept,
            CertificateTrustDecision.Decide(null, PIN, SslPolicyErrors.None));

    /// <summary>First contact with a self-signed server: refused, and offered to the user.</summary>
    [Theory]
    [InlineData(SslPolicyErrors.RemoteCertificateChainErrors)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch)]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable)]
    public void Unpinned_IsRefusedForTrustOnFirstUse_WhenThePlatformRejectsIt(SslPolicyErrors errors)
        => Assert.Equal(
            CertificateTrustVerdict.RejectUntrusted,
            CertificateTrustDecision.Decide(null, PIN, errors));

    /// <summary>An empty pin is no pin; it must not be read as "matches nothing" and refuse everything.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankPin_FallsBackToThePlatform(string pin)
        => Assert.Equal(
            CertificateTrustVerdict.Accept,
            CertificateTrustDecision.Decide(pin, PIN, SslPolicyErrors.None));

    /// <summary>
    ///     The flagship path: pinned, matching, and publicly issued must <em>keep</em> revocation
    ///     checking. Keying revocation on "is pinned" would drop it from the path carrying almost all
    ///     traffic — and the two are not redundant there, because a pin is an allowlist of one: a stolen
    ///     production key presents the pinned certificate and matches, and only revocation catches that
    ///     before expiry.
    /// </summary>
    [Fact]
    public void PrePinnedPublicCaEndpoint_KeepsRevocationChecking()
        => Assert.Equal(
            X509RevocationMode.Online,
            CertificateTrustDecision.RevocationFor(CertificateTrustPath.PublicCa));

    /// <summary>A user-trusted self-signed certificate has no responder, so checking would only stall.</summary>
    [Fact]
    public void TrustOnFirstUseEndpoint_SkipsRevocationChecking()
        => Assert.Equal(
            X509RevocationMode.NoCheck,
            CertificateTrustDecision.RevocationFor(CertificateTrustPath.TrustOnFirstUse));

    /// <summary>An endpoint with no record rests on the public PKI and keeps the check.</summary>
    [Fact]
    public void UnknownEndpoint_KeepsRevocationChecking()
        => Assert.Equal(X509RevocationMode.Online, CertificateTrustDecision.RevocationFor(null));

    /// <summary>Fingerprint hex casing is incidental to the comparison.</summary>
    [Fact]
    public void PinComparison_IgnoresHexCasing()
        => Assert.Equal(
            CertificateTrustVerdict.Accept,
            CertificateTrustDecision.Decide("aa:bb:cc", "AA:BB:CC", SslPolicyErrors.RemoteCertificateChainErrors));
}
