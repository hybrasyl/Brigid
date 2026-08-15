#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     The downgrade warning: raised when a server that has completed a TLS session before connects
///     without upgrading. Asks whether to continue in plaintext or disconnect.
/// </summary>
/// <remarks>
///     <para>
///         The capability marker that triggers an upgrade rides in plaintext on first contact, so an
///         attacker in the network path can strip it and leave a client that never attempts TLS,
///         observes nothing unusual, and sends a password in the clear. Neither end can detect this
///         from inside TLS, because TLS never happened. The only mitigation is memory the client
///         holds — this prompt is that memory being spent.
///     </para>
///     <para>
///         It blocks rather than informing, because the whole value is in arriving <em>before</em> the
///         credential flow: a banner the user reads while typing a password has already lost. Escape
///         and the Disconnect button are the same action, so the safe outcome is also the reflexive
///         one — continuing takes a deliberate click on a button that is not the default.
///     </para>
/// </remarks>
public sealed class InsecureConnectionPromptControl : CenteredModalPanel
{
    private const int PANEL_W = 396;
    private const int PANEL_H = 184;
    private const int ROW_H = 14;
    private const int CONTINUE_W = 72;
    private const int DISCONNECT_W = 80;

    private readonly UILabel Headline;
    private readonly UILabel ServerRow;
    private readonly UILabel Explanation;
    private readonly UILabel Guidance;

    private string? Server;

    /// <summary>Raised when the user accepts the plaintext connection and continues.</summary>
    public event Action<string>? OnContinued;

    /// <summary>Raised when the user refuses — the Disconnect button, the close box, or Escape.</summary>
    public event Action<string>? OnRefused;

    public InsecureConnectionPromptControl()
        : base("Connection Is Not Encrypted", PANEL_W, PANEL_H)
    {
        var y = ContentTop + 4;

        Headline = AddRow(ref y, DialogPalette.RequirementUnmet);
        Headline.WordWrap = true;
        Headline.Height = ROW_H * 2;

        Headline.Text = "This server has used an encrypted connection before, but is not offering one now.";
        y += ROW_H;

        y += 4;
        ServerRow = AddRow(ref y);

        y += 4;
        Explanation = AddRow(ref y);
        Explanation.WordWrap = true;
        Explanation.Height = ROW_H * 3;

        Explanation.Text =
            "Your password and everything you say would travel in the clear. This can mean the server turned "
            + "encryption off, or that something on the network is stripping it out.";

        y += ROW_H * 2;

        y += 4;
        Guidance = AddRow(ref y, DialogPalette.RequirementUnmet);
        Guidance.WordWrap = true;
        Guidance.Height = ROW_H * 2;
        Guidance.Text = "Continue only if you know encryption was turned off deliberately.";

        //the close box is the refusal, so it says what it does. Escape reaches the same handler.
        CloseButton.SetText("Disconnect");
        CloseButton.Width = DISCONNECT_W;
        RelayoutBottomBar();

        AddBottomBarButton("Continue", CONTINUE_W, AcceptRisk);

        OnClose += Refuse;
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

    /// <summary>Shows the warning for <paramref name="server" />.</summary>
    public void Show(string server)
    {
        ArgumentException.ThrowIfNullOrEmpty(server);

        Server = server;
        ServerRow.Text = $"Server:  {server}";

        base.Show();
    }

    private void AcceptRisk()
    {
        if (Server is not { } server)
            return;

        Server = null;
        Hide();
        OnContinued?.Invoke(server);
    }

    private void Refuse()
    {
        if (Server is not { } server)
            return;

        Server = null;
        OnRefused?.Invoke(server);
    }
}
