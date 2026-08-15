#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Brigid.Rendering;
using Brigid.Systems;
using Hybrasyl.Protocol;
using Hybrasyl.Protocol.Negotiation;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     What this connection actually is: which server, carried how, under which certificate, and what a
///     round trip costs right now. Opened by clicking the HUD's server box.
/// </summary>
/// <remarks>
///     The certificate half answers a question the padlock deliberately cannot. A padlock says the
///     transport is encrypted; it says nothing about <em>who</em> the other end proved itself to be,
///     and on a trust-on-first-use connection that distinction is the whole security model. The
///     fingerprint is here so it can be compared against one the operator published.
/// </remarks>
public sealed class ServerInfoControl : CenteredModalPanel
{
    private const int PANEL_W = 420;
    //content ends at y=191; the bar sits at 204, leaving a row of slack for a face that inks larger.
    private const int PANEL_H = 228;
    private const int ROW_H = 14;
    private const int PING_W = 60;

    private readonly UILabel NameRow;
    private readonly UILabel HostRow;
    private readonly UILabel TransportRow;
    private readonly UILabel DialectRow;
    private readonly UILabel TrustRow;
    private readonly UILabel SubjectRow;
    private readonly UILabel IssuerRow;
    private readonly UILabel ExpiryRow;
    private readonly UILabel FingerprintLine1;
    private readonly UILabel FingerprintLine2;
    private readonly UILabel LatencyRow;
    private readonly TextButton PingButton;

    /// <summary>Raised when the user asks for a fresh measurement. The host sends the probe.</summary>
    public event Action? PingRequested;

    public ServerInfoControl()
        : base("Connection", PANEL_W, PANEL_H)
    {
        var y = ContentTop + 4;

        NameRow = AddRow(ref y);
        HostRow = AddRow(ref y);
        TransportRow = AddRow(ref y);
        DialectRow = AddRow(ref y);

        y += 6;
        TrustRow = AddRow(ref y);
        //a real subject runs to "CN=..., O=..., L=..., S=..., C=US", which overruns the panel. These two
        //shrink rather than clip: a distinguished name loses its meaning from either end.
        SubjectRow = AddRow(ref y);
        SubjectRow.ShrinkToFit = true;
        IssuerRow = AddRow(ref y);
        IssuerRow.ShrinkToFit = true;
        ExpiryRow = AddRow(ref y);
        FingerprintLine1 = AddRow(ref y);
        FingerprintLine2 = AddRow(ref y);

        y += 6;
        LatencyRow = AddRow(ref y);

        PingButton = AddBottomBarButton("Ping", PING_W, () => PingRequested?.Invoke());
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

    /// <summary>
    ///     Fills the panel from the live connection. Called on open and again whenever the transport
    ///     changes, so a panel left open across a world transfer does not describe the previous hop.
    /// </summary>
    public void Describe(string serverName, string host, DialectResolution? negotiated, AcceptedCertificate? certificate)
    {
        NameRow.Text = $"Server:     {(string.IsNullOrWhiteSpace(serverName) ? "(unnamed)" : serverName)}";
        HostRow.Text = $"Host:       {host}";

        var secure = negotiated is not null;

        TransportRow.Text = secure ? $"Transport:  {Glyphs.PADLOCK} TLS 1.3" : "Transport:  plaintext";
        TransportRow.ForegroundColor = secure ? LegendColors.Lime : LegendColors.White;

        DialectRow.Text = negotiated is { } resolution
            ? $"Dialect:    {resolution.Dialect} ({resolution.Mode})"
            : "Dialect:    none (retail framing)";

        DescribeCertificate(certificate);
    }

    private void DescribeCertificate(AcceptedCertificate? certificate)
    {
        if (certificate is null)
        {
            TrustRow.Text = "Certificate: none — this connection is not encrypted.";
            TrustRow.ForegroundColor = DialogPalette.Title;

            SubjectRow.Text = string.Empty;
            IssuerRow.Text = string.Empty;
            ExpiryRow.Text = string.Empty;
            FingerprintLine1.Text = string.Empty;
            FingerprintLine2.Text = string.Empty;

            return;
        }

        //the distinction the padlock cannot carry: a publicly valid certificate was vouched for by a CA,
        //a pinned one only by this user on some earlier connection.
        TrustRow.Text = certificate.PlatformValidated
            ? "Trust:      validated against the system roots"
            : "Trust:      pinned by you (not publicly trusted)";

        TrustRow.ForegroundColor = certificate.PlatformValidated ? LegendColors.Lime : LegendColors.CanaryYellow;

        SubjectRow.Text = $"Subject:    {certificate.Subject}";
        IssuerRow.Text = $"Issuer:     {certificate.Issuer}";

        ExpiryRow.Text = certificate.NotAfter == DateTime.MinValue
            ? "Expires:    unknown"
            : $"Expires:    {certificate.NotAfter:yyyy-MM-dd}";

        var (first, second) = CertificateTrustPromptControl.SplitFingerprint(certificate.Fingerprint);
        FingerprintLine1.Text = $"SHA-256:    {first}";
        FingerprintLine2.Text = $"            {second}";
    }

    /// <summary>Shows the measured application round-trip time.</summary>
    public void SetLatency(TimeSpan roundTrip)
    {
        LatencyRow.Text = $"Round trip: {roundTrip.TotalMilliseconds:0.0} ms";
        LatencyRow.ForegroundColor = DialogPalette.Title;
        PingButton.SetEnabled(true);
    }

    /// <summary>
    ///     States why no measurement is possible, or that one is in flight. A probe in flight leaves the
    ///     button live: nothing times an echo out, so disabling it would strand the panel on
    ///     "measuring…" for good if a reply never came.
    /// </summary>
    public void SetLatencyUnavailable(string reason, bool canRetry)
    {
        LatencyRow.Text = $"Round trip: {reason}";
        LatencyRow.ForegroundColor = DialogPalette.Title;
        PingButton.SetEnabled(canRetry);
    }
}
