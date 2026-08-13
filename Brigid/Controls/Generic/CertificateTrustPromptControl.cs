#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Rendering;
using Brigid.Systems;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     Raised when the user accepts a server certificate.
/// </summary>
/// <param name="endpoint">The <c>host:port</c> being trusted.</param>
/// <param name="fingerprint">The SHA-256 fingerprint to pin.</param>
/// <param name="path">How trust was established, which also selects the revocation policy.</param>
public delegate void CertificateTrustedHandler(string endpoint, string fingerprint, CertificateTrustPath path);

/// <summary>
///     The trust-on-first-use prompt: shows what a server presented and asks whether to pin it. Raised
///     when a TLS upgrade refuses a certificate — either one this endpoint has never presented, or one
///     that <em>differs</em> from what it presented before.
/// </summary>
/// <remarks>
///     <para>
///         The two cases are deliberately not one message. An unknown certificate is the ordinary first
///         contact with a self-hosted or development server; a changed one contradicts something the user
///         already approved and is either a rotation or an attack, which cannot be told apart from here.
///         Collapsing them would train the user to click through the one that matters.
///     </para>
///     <para>
///         Accepting pins and closes; the caller retries the connection, because the refusal that raised
///         this prompt already dropped it. The prompt never accepts on the user's behalf and has no
///         default action — declining is the Close button, which is also what Escape does.
///     </para>
/// </remarks>
public sealed class CertificateTrustPromptControl : CenteredModalPanel
{
    private const int PANEL_W = 396;
    private const int PANEL_H = 208;
    private const int ROW_H = 14;
    private const int TRUST_W = 64;

    //a SHA-256 fingerprint is 32 colon-separated bytes; half fits a panel-width line comfortably.
    private const int FINGERPRINT_BYTES_PER_LINE = 16;

    private readonly UILabel Headline;
    private readonly UILabel EndpointRow;
    private readonly UILabel SubjectRow;
    private readonly UILabel IssuerRow;
    private readonly UILabel ExpiryRow;
    private readonly UILabel FingerprintLabel;
    private readonly UILabel FingerprintLine1;
    private readonly UILabel FingerprintLine2;
    private readonly UILabel Guidance;

    private PendingCertificateTrust? Subject;

    /// <summary>Raised when the user accepts. The host pins and retries.</summary>
    public event CertificateTrustedHandler? OnTrusted;

    /// <summary>Raised when the user declines, so the host can clear the pending decision.</summary>
    public event Action? OnDeclined;

    public CertificateTrustPromptControl()
        : base("Unrecognized Server Certificate", PANEL_W, PANEL_H)
    {
        var y = ContentTop + 4;

        Headline = AddRow(ref y, DialogPalette.Title);
        Headline.WordWrap = true;
        Headline.Height = ROW_H * 2;
        y += ROW_H;

        y += 4;
        EndpointRow = AddRow(ref y);
        SubjectRow = AddRow(ref y);
        IssuerRow = AddRow(ref y);
        ExpiryRow = AddRow(ref y);

        y += 4;
        FingerprintLabel = AddRow(ref y);
        FingerprintLabel.Text = "SHA-256 fingerprint:";
        FingerprintLine1 = AddRow(ref y);
        FingerprintLine2 = AddRow(ref y);

        y += 4;
        Guidance = AddRow(ref y);
        Guidance.WordWrap = true;
        Guidance.Height = ROW_H * 2;

        AddBottomBarButton("Trust", TRUST_W, AcceptTrust);

        OnClose += Decline;
    }

    private UILabel AddRow(ref int y, Color? color = null)
    {
        var label = new UILabel
        {
            X = PADDING,
            Y = y,
            Width = Width - 2 * PADDING,
            Height = ROW_H,
            ForegroundColor = color ?? DialogPalette.Title,
            IsHitTestVisible = false,
            ShrinkToFit = false
        };

        AddChild(label);
        y += ROW_H;

        return label;
    }

    /// <summary>Shows the prompt for <paramref name="pending" />.</summary>
    public void Show(PendingCertificateTrust pending)
    {
        ArgumentNullException.ThrowIfNull(pending);

        Subject = pending;

        SetTitle(pending.IsPinMismatch ? "Server Certificate Changed" : "Unrecognized Server Certificate");

        Headline.Text = pending.IsPinMismatch
            ? "This server is presenting a DIFFERENT certificate from the one you previously trusted."
            : "Brigid does not recognize this server's certificate.";

        Headline.ForegroundColor = pending.IsPinMismatch ? DialogPalette.RequirementUnmet : DialogPalette.Title;

        EndpointRow.Text = $"Server:  {pending.Endpoint}";
        SubjectRow.Text = $"Subject: {pending.Subject}";
        IssuerRow.Text = $"Issuer:  {pending.Issuer}";

        ExpiryRow.Text = pending.NotAfter == DateTime.MinValue
            ? "Expires: unknown"
            : $"Expires: {pending.NotAfter:yyyy-MM-dd}";

        var (first, second) = SplitFingerprint(pending.Fingerprint);
        FingerprintLine1.Text = first;
        FingerprintLine2.Text = second;

        Guidance.Text = pending.IsPinMismatch
            ? "Only continue if you know the server's certificate was replaced. Otherwise the connection may be intercepted."
            : "Trust this only if the fingerprint matches the one published by the server's operator.";

        Guidance.ForegroundColor = pending.IsPinMismatch ? DialogPalette.RequirementUnmet : DialogPalette.Title;

        base.Show();
    }

    /// <summary>
    ///     Splits a colon-separated fingerprint across two lines. Returned as a pair rather than wrapped by
    ///     the label so the break always lands on a byte boundary — a fingerprint broken mid-byte is one a
    ///     user cannot compare against a published value.
    /// </summary>
    internal static (string First, string Second) SplitFingerprint(string fingerprint)
    {
        var bytes = (fingerprint ?? string.Empty).Split(':');

        if (bytes.Length <= FINGERPRINT_BYTES_PER_LINE)
            return (string.Join(':', bytes), string.Empty);

        return (
            string.Join(':', bytes[..FINGERPRINT_BYTES_PER_LINE]),
            string.Join(':', bytes[FINGERPRINT_BYTES_PER_LINE..]));
    }

    private void AcceptTrust()
    {
        if (Subject is not { } pending)
            return;

        //a mismatch on a certificate the platform itself accepts is still a public-CA endpoint; recording
        //it as trust-on-first-use would silently drop revocation checking from it.
        var path = pending.PlatformValidated
            ? CertificateTrustPath.PublicCa
            : CertificateTrustPath.TrustOnFirstUse;

        Subject = null;
        Hide();
        OnTrusted?.Invoke(pending.Endpoint, pending.Fingerprint, path);
    }

    private void Decline()
    {
        if (Subject is null)
            return;

        Subject = null;
        OnDeclined?.Invoke();
    }
}
