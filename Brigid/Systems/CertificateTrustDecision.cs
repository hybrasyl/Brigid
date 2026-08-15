#region
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
#endregion

namespace Brigid.Systems;

/// <summary>
///     How an endpoint's trust was established. Recorded when the pin is written, because the answer is
///     only knowable during validation while the revocation policy must be chosen before the handshake.
/// </summary>
public enum CertificateTrustPath
{
    /// <summary>
    ///     Accepted by platform/public-CA validation — including a shipped flagship pre-pin, which is
    ///     pinned <em>and</em> publicly issued.
    /// </summary>
    PublicCa,

    /// <summary>
    ///     Accepted by the user for a certificate the platform rejected: self-signed, unknown CA, or
    ///     hostname mismatch.
    /// </summary>
    TrustOnFirstUse
}

/// <summary>
///     What to do with a server certificate presented during the TLS upgrade.
/// </summary>
public enum CertificateTrustVerdict
{
    /// <summary>Trusted: a matching pin, or a certificate the system roots already accept.</summary>
    Accept,

    /// <summary>
    ///     No pin for this endpoint and the system roots reject it — the trust-on-first-use case. The
    ///     connection is refused; the user is asked, and a pin makes the retry succeed.
    /// </summary>
    RejectUntrusted,

    /// <summary>
    ///     This endpoint is pinned and presented a <em>different</em> certificate. Loud by design: it is
    ///     either a rotation the user must confirm or an attack, and the two are indistinguishable from
    ///     here.
    /// </summary>
    RejectPinMismatch
}

/// <summary>
///     The trust rule for a presented certificate, as a pure function of the pin, the fingerprint, and
///     the platform's own verdict. Separated from the store and its file I/O because this is the
///     security-critical half and is the part worth testing exhaustively.
/// </summary>
public static class CertificateTrustDecision
{
    /// <summary>
    ///     Decides whether to accept a certificate.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>A pin is authoritative in both directions.</b> When an endpoint is pinned, the pin
    ///         decides alone and the platform verdict is not consulted: a matching pin is accepted even
    ///         though the system roots reject it (that is what makes self-hosted and dev servers usable
    ///         at all), and a non-matching certificate is refused even though the system roots accept it.
    ///         The second half is the one worth stating — it is what protects the flagship endpoint from
    ///         a <em>mis-issued but publicly valid</em> certificate for its hostname.
    ///     </para>
    ///     <para>
    ///         An unpinned endpoint falls back to the platform, and a platform-accepted certificate is
    ///         deliberately <em>not</em> auto-pinned: pinning every public certificate on sight would
    ///         turn each routine renewal into a mismatch prompt, training users to click through the one
    ///         dialog that is supposed to mean something.
    ///     </para>
    /// </remarks>
    /// <param name="pinnedFingerprint">The stored SHA-256 pin for this endpoint, or null if none.</param>
    /// <param name="presentedFingerprint">SHA-256 of the certificate the server presented.</param>
    /// <param name="platformErrors">The platform's own validation result.</param>
    public static CertificateTrustVerdict Decide(
        string? pinnedFingerprint,
        string presentedFingerprint,
        SslPolicyErrors platformErrors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentedFingerprint);

        if (!string.IsNullOrWhiteSpace(pinnedFingerprint))
            return string.Equals(pinnedFingerprint, presentedFingerprint, StringComparison.OrdinalIgnoreCase)
                ? CertificateTrustVerdict.Accept
                : CertificateTrustVerdict.RejectPinMismatch;

        return platformErrors == SslPolicyErrors.None
            ? CertificateTrustVerdict.Accept
            : CertificateTrustVerdict.RejectUntrusted;
    }

    /// <summary>
    ///     The revocation policy for an endpoint, given how its trust was established.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The predicate is the trust path, not the presence of a pin.</b> What licenses skipping
    ///         revocation is having no responder to ask, and those two properties coincide for a
    ///         trust-on-first-use certificate but come apart for the flagship pre-pin, which is pinned
    ///         <em>and</em> publicly issued with live AIA/OCSP. Keying on "is pinned" would silently drop
    ///         revocation from the path that carries almost all the traffic.
    ///     </para>
    ///     <para>
    ///         They are not redundant there either. A pin is an allowlist of one: it proves this is the
    ///         certificate we expect, so a <em>stolen production key</em> presents that exact certificate
    ///         and the pin matches happily. Revocation is the only check that catches it before expiry.
    ///     </para>
    ///     <para>
    ///         Following the trust path also stays correct when a self-hosted server later obtains a real
    ///         certificate: the pin is replaced, the path changes with it, and the policy follows the
    ///         thing that actually determines whether a responder exists.
    ///     </para>
    /// </remarks>
    /// <param name="path">How trust was established, or null for an endpoint with no record.</param>
    public static X509RevocationMode RevocationFor(CertificateTrustPath? path)
        => path == CertificateTrustPath.TrustOnFirstUse
            ? X509RevocationMode.NoCheck
            : X509RevocationMode.Online;
}
