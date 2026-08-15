#region
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Brigid.Data;
using Brigid.Networking;
using Hybrasyl.Protocol.Transport;
#endregion

namespace Brigid.Systems;

/// <summary>
///     What a server presented that the user has not yet ruled on: enough to identify the certificate
///     in a prompt, plus why it was refused.
/// </summary>
/// <param name="Server">The server identity the certificate was presented for.</param>
/// <param name="Subject">The certificate subject.</param>
/// <param name="Issuer">The certificate issuer.</param>
/// <param name="Fingerprint">SHA-256 fingerprint, colon-separated hex.</param>
/// <param name="NotAfter">Expiry, for display.</param>
/// <param name="IsPinMismatch">
///     True when this server was already pinned to a <em>different</em> certificate. The prompt must
///     read differently here: a first-time certificate is unknown, a changed one contradicts something
///     the user already approved.
/// </param>
/// <param name="PlatformValidated">
///     Whether the platform itself accepted this certificate. Carried so accepting it records the right
///     <see cref="CertificateTrustPath" />: a pin mismatch on a publicly valid certificate is a
///     public-CA endpoint, not a trust-on-first-use one, and their revocation policies differ.
/// </param>
public sealed record PendingCertificateTrust(
    string Server,
    string Subject,
    string Issuer,
    string Fingerprint,
    DateTime NotAfter,
    bool IsPinMismatch,
    bool PlatformValidated);

/// <summary>
///     Trust-on-first-use certificate pins and TLS history, keyed by <em>server identity</em> — the
///     hostname a certificate is validated against — and persisted in the per-user app root. SSH
///     <c>known_hosts</c> semantics: an unknown certificate is refused and offered to the user, an
///     accepted one is pinned and never asked about again, and a changed one is refused loudly.
/// </summary>
/// <remarks>
///     <para>
///         The key is the host alone, not <c>host:port</c>. A certificate is bound to a hostname; the
///         port identifies a service on that host and appears in no part of validation. Keying by port
///         asked the user to vouch for the same certificate three times per session — once each for
///         lobby, login and world — which trains exactly the click-through the prompt exists to
///         prevent. A different certificate on another port of the same host still mismatches and
///         still prompts, so this narrows the prompting, not the checking.
///     </para>
///     <para>
///         This diverges from <c>EXTENSIONS.md</c> §8.4, which specifies pinning keyed by endpoint.
///         The divergence is client-side only and invisible on the wire.
///     </para>
/// </remarks>
/// <remarks>
///     Lives in the client project rather than <c>Brigid.Networking</c>: it needs
///     <see cref="AppPaths" /> and drives UI, and the networking layer takes only a validation callback.
/// </remarks>
public static class CertificateTrustStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        //this file is the known_hosts analogue and will be read and sometimes hand-edited by operators;
        //a trust path written as a bare 0 or 1 is both unreadable and easy to set wrongly.
        Converters = { new JsonStringEnumConverter() }
    };

    //guards Entries and Pending: the validation callback runs on the handshake's thread while the game
    //loop reads Pending to raise the prompt.
    private static readonly Lock Gate = new();

    private static readonly Dictionary<string, TrustEntry> Entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Fingerprints shipped with the client, so the flagship path never sees a trust prompt and is
    ///     protected even against a mis-issued public certificate for its hostname. Populated when the
    ///     production fingerprint is available; empty pre-pins simply mean every server goes through
    ///     the ordinary rules.
    /// </summary>
    private static readonly Dictionary<string, string> PrePinned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     The certificate awaiting a user decision, or null. Set by the validation callback when it
    ///     refuses an unknown or changed certificate; read by the connect flow to raise the prompt.
    /// </summary>
    public static PendingCertificateTrust? Pending
    {
        get
        {
            using var scope = Gate.EnterScope();

            return PendingField;
        }
    }

    private static PendingCertificateTrust? PendingField;

    private static string FilePath => Path.Combine(AppPaths.AppRoot, "known-servers.json");

    /// <summary>
    ///     The store's key format. Version 1 keyed entries by <c>host:port</c>; version 2 keys them by
    ///     host alone. Version 1 entries are discarded rather than migrated: stripping the port would
    ///     merge three keys that were free to hold different fingerprints, and picking a winner among
    ///     them is a guess about which certificate the user vouched for. Discarding costs one re-prompt
    ///     per server and cannot silently widen a pin to a fingerprint the user never saw.
    /// </summary>
    private const int STORE_VERSION = 2;

    /// <summary>Loads pins from disk, replacing anything held in memory.</summary>
    public static void Load()
    {
        using var scope = Gate.EnterScope();

        Entries.Clear();
        PendingField = null;
        PendingDowngradeField = null;

        foreach (var (endpoint, fingerprint) in PrePinned)
            Entries[endpoint] = new TrustEntry
            {
                Fingerprint = fingerprint,
                TlsSeen = true,
                Path = CertificateTrustPath.PublicCa
            };

        if (!File.Exists(FilePath))
            return;

        var model = ReadModel();

        if (model?.Servers is null)
            return;

        if (model.Version != STORE_VERSION)
        {
            //loud rather than silent: the user is about to be asked to vouch for servers they already
            //approved, and without this the re-prompt looks like a pin mismatch with no cause.
            NoticeDebugLog.Write(
                $"known-servers.json is version {model.Version}; this client keys pins by host (version "
                + $"{STORE_VERSION}). {model.Servers.Count} stored pin(s) discarded — each server will be asked about once more.");

            return;
        }

        //a shipped pre-pin outranks a stored one: the flagship pin is a claim the client makes, not a
        //user preference, and letting a file override it would defeat the point of shipping it.
        foreach (var (server, entry) in model.Servers)
            if (!PrePinned.ContainsKey(server))
                Entries[server] = entry;
    }

    private static Model? ReadModel()
    {
        try
        {
            return JsonSerializer.Deserialize<Model>(File.ReadAllText(FilePath), SerializerOptions);
        } catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            //a corrupt or unreadable store must not block connecting: fall back to pre-pins only, which
            //costs the user a re-prompt rather than a dead client.
            return null;
        }
    }

    /// <summary>Writes pins to disk. Best effort — a failure costs a re-prompt, not a connection.</summary>
    public static void Save()
    {
        Model model;

        using (Gate.EnterScope())
            model = new Model
            {
                Version = STORE_VERSION,
                Servers = new Dictionary<string, TrustEntry>(Entries, StringComparer.OrdinalIgnoreCase)
            };

        try
        {
            Directory.CreateDirectory(AppPaths.AppRoot);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(model, SerializerOptions));
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            //ignored by design; see summary.
        }
    }

    /// <summary>
    ///     Builds the validation callback for one connection. The server identity is captured here
    ///     because the platform callback does not carry it.
    /// </summary>
    public static RemoteCertificateValidationCallback ValidatorFor(string server)
        => (_, certificate, _, errors) => Validate(server, certificate, errors);

    /// <summary>
    ///     The complete TLS client options for a server identity — validator and revocation policy
    ///     together, since both follow from the same trust record. This is what
    ///     <c>GameClient.TlsOptions</c> is set to.
    /// </summary>
    public static SslClientAuthenticationOptions OptionsFor(string server)
        => TlsConfig.ClientOptions(server, ValidatorFor(server), RevocationModeFor(server));

    /// <summary>
    ///     The revocation policy for a server, from how its trust was established. See
    ///     <see cref="CertificateTrustDecision.RevocationFor" /> for why the predicate is the trust path
    ///     rather than the presence of a pin.
    /// </summary>
    public static X509RevocationMode RevocationModeFor(string server)
    {
        using var scope = Gate.EnterScope();

        return CertificateTrustDecision.RevocationFor(
            Entries.TryGetValue(server, out var entry) ? entry.Path : null);
    }

    private static bool Validate(string server, X509Certificate? certificate, SslPolicyErrors errors)
    {
        if (certificate is null)
            return false;

        var presented = FingerprintOf(certificate);

        using var scope = Gate.EnterScope();

        Entries.TryGetValue(server, out var entry);

        var verdict = CertificateTrustDecision.Decide(entry?.Fingerprint, presented, errors);

        if (verdict == CertificateTrustVerdict.Accept)
        {
            PendingField = null;

            return true;
        }

        PendingField = new PendingCertificateTrust(
            server,
            certificate.Subject,
            certificate.Issuer,
            presented,
            ParseNotAfter(certificate),
            verdict == CertificateTrustVerdict.RejectPinMismatch,
            errors == SslPolicyErrors.None);

        //the fingerprint is what the user is being asked to vouch for, so it belongs in the log whether
        //or not a prompt is reachable — it is also the value needed to pin an endpoint by hand.
        NoticeDebugLog.Write(
            $"certificate refused for {server} ({(verdict == CertificateTrustVerdict.RejectPinMismatch ? "pinned to a different certificate" : "not trusted")}); "
            + $"subject={certificate.Subject} issuer={certificate.Issuer} sha256={presented}");

        return false;
    }

    /// <summary>
    ///     Pins <paramref name="fingerprint" /> for <paramref name="server" /> after the user accepts
    ///     it, and persists. The connection must then be retried; the refusal that raised the prompt
    ///     already dropped it.
    /// </summary>
    public static void Trust(string server, string fingerprint, CertificateTrustPath path)
    {
        using (Gate.EnterScope())
        {
            Entries[server] = new TrustEntry
            {
                Fingerprint = fingerprint,
                TlsSeen = true,
                Path = path
            };

            PendingField = null;
        }

        Save();
    }

    /// <summary>Discards the pending decision without pinning, for a user who declines.</summary>
    public static void Decline()
    {
        using var scope = Gate.EnterScope();

        PendingField = null;
    }

    /// <summary>
    ///     Records that <paramref name="server" /> completed a TLS upgrade, so a later plaintext
    ///     connection to it can be flagged. Called on every successful upgrade, not only on the ones
    ///     that prompted: a server with a publicly valid certificate is accepted silently and pins
    ///     nothing, and it is precisely that path this history has to cover.
    /// </summary>
    /// <returns>True when this is new information, so the caller can avoid a write per hop.</returns>
    public static bool RecordTlsSeen(string server)
    {
        using (Gate.EnterScope())
        {
            if (Entries.TryGetValue(server, out var entry))
            {
                if (entry.TlsSeen)
                    return false;

                entry.TlsSeen = true;
            } else
                Entries[server] = new TrustEntry { TlsSeen = true };
        }

        Save();

        return true;
    }

    /// <summary>
    ///     Whether <paramref name="server" /> has previously spoken TLS. A connection that does not
    ///     upgrade where this is true is the downgrade case worth warning about: the capability marker
    ///     rides in <em>plaintext</em>, so an active attacker can strip it, and silence is otherwise
    ///     indistinguishable from a server that stopped offering the extension.
    /// </summary>
    public static bool HasSpokenTls(string server)
    {
        using var scope = Gate.EnterScope();

        return Entries.TryGetValue(server, out var entry) && entry.TlsSeen;
    }

    /// <summary>
    ///     The server that connected in plaintext despite a recorded TLS history, or null. Set when a
    ///     connection completes without an upgrade; read by the lobby screen to raise the warning.
    /// </summary>
    /// <remarks>
    ///     Mirrors <see cref="Pending" /> deliberately. Both are decisions discovered on a connect task
    ///     and acted on by the game loop, which is the thread allowed to touch the control tree.
    /// </remarks>
    public static string? PendingDowngrade
    {
        get
        {
            using var scope = Gate.EnterScope();

            return PendingDowngradeField;
        }
    }

    private static string? PendingDowngradeField;

    /// <summary>
    ///     Servers the user has agreed to talk to in plaintext this run. Deliberately not persisted:
    ///     the answer covers the session it was given in, and a stripping attacker should have to strip
    ///     again — and be refused again — the next time the client starts.
    /// </summary>
    private static readonly HashSet<string> PlaintextAccepted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Flags <paramref name="server" /> as having connected in plaintext after previously speaking
    ///     TLS, if it has and the user has not already agreed to it this run. Returns true when the flag
    ///     was raised.
    /// </summary>
    /// <remarks>
    ///     The session exemption is what keeps one answer from being asked for again on the next hop:
    ///     lobby, login and world are three connections to one identity, and a user who continued past
    ///     the lobby's warning has already answered for all of them.
    /// </remarks>
    public static bool FlagDowngradeIfSeen(string server)
    {
        using var scope = Gate.EnterScope();

        if (PlaintextAccepted.Contains(server))
            return false;

        if (!Entries.TryGetValue(server, out var entry) || !entry.TlsSeen)
            return false;

        PendingDowngradeField = server;

        return true;
    }

    /// <summary>
    ///     Clears the pending downgrade warning once the user has ruled on it. When
    ///     <paramref name="accepted" />, the server is exempt for the rest of this run.
    /// </summary>
    public static void ClearDowngrade(string server, bool accepted)
    {
        using var scope = Gate.EnterScope();

        PendingDowngradeField = null;

        if (accepted)
            PlaintextAccepted.Add(server);
    }

    /// <summary>SHA-256 of the certificate's DER encoding, as colon-separated uppercase hex.</summary>
    public static string FingerprintOf(X509Certificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()))
                      .Chunk(2)
                      .Select(pair => new string(pair))
                      .Aggregate((left, right) => $"{left}:{right}");
    }

    private static DateTime ParseNotAfter(X509Certificate certificate)
        => DateTime.TryParse(certificate.GetExpirationDateString(), out var parsed) ? parsed : DateTime.MinValue;

    /// <summary>One endpoint's trust record.</summary>
    public sealed class TrustEntry
    {
        /// <summary>The pinned SHA-256 fingerprint, or null when only TLS history is recorded.</summary>
        public string? Fingerprint { get; set; }

        /// <summary>Whether this endpoint has completed a TLS upgrade before.</summary>
        public bool TlsSeen { get; set; }

        /// <summary>How this endpoint's trust was established; drives the revocation policy.</summary>
        public CertificateTrustPath Path { get; set; }
    }

    private sealed class Model
    {
        /// <summary>Key format of <see cref="Servers" />. Absent (0) means the pre-versioning host:port layout.</summary>
        public int Version { get; set; }

        public Dictionary<string, TrustEntry>? Servers { get; set; }
    }
}
