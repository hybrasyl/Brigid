#region
using System.Net;
using Brigid.Networking;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     Which name a redirect hop authenticates as. The retail <c>0x03</c> redirect carries an address
///     and no name, so without an inherited identity every hop after the lobby is validated against an
///     IP literal — which matches no ordinary certificate, and forces a trust prompt per hop for a
///     certificate the user already approved at the lobby.
/// </summary>
/// <remarks>
///     The lookup that feeds this decision is exercised only against a live resolver; these cover the
///     decision itself. The end-to-end half — that the chosen identity is what reaches the TLS options
///     factory rather than the dialled address — is in <see cref="TlsUpgradeTests" />, which owns the
///     loopback server.
/// </remarks>
public class ServerIdentityTests
{
    /// <summary>
    ///     A redirect into the lobby host's own addresses inherits its name, so the pin taken at the
    ///     lobby covers the hop and the user is not asked twice about one certificate.
    /// </summary>
    [Fact]
    public void RedirectWithinTheLobbyHost_InheritsItsName()
    {
        var identity = ConnectionManager.ChooseRedirectIdentity(
            "wintermute.example.net",
            [IPAddress.Parse("10.0.0.7"), IPAddress.Parse("10.0.0.8")],
            IPAddress.Parse("10.0.0.8"));

        Assert.Equal("wintermute.example.net", identity);
    }

    /// <summary>
    ///     A redirect that leaves the lobby host authenticates as itself. Inheriting unconditionally
    ///     would put one server's certificate to another server's name — which either fails
    ///     confusingly, or, where a pin is already held under that name, describes the wrong machine to
    ///     the user.
    /// </summary>
    [Fact]
    public void RedirectOutsideTheLobbyHost_AuthenticatesAsItself()
    {
        var identity = ConnectionManager.ChooseRedirectIdentity(
            "wintermute.example.net",
            [IPAddress.Parse("10.0.0.7")],
            IPAddress.Parse("203.0.113.9"));

        Assert.Equal("203.0.113.9", identity);
    }

    /// <summary>A lobby reached by bare address compares against itself and needs no resolver.</summary>
    [Fact]
    public void LobbyReachedByAddress_InheritsOnlyItsOwnAddress()
    {
        Assert.Equal(
            "10.0.0.7",
            ConnectionManager.ChooseRedirectIdentity(
                "10.0.0.7",
                [IPAddress.Parse("10.0.0.7")],
                IPAddress.Parse("10.0.0.7")));

        Assert.Equal(
            "10.0.0.9",
            ConnectionManager.ChooseRedirectIdentity(
                "10.0.0.7",
                [IPAddress.Parse("10.0.0.7")],
                IPAddress.Parse("10.0.0.9")));
    }

    /// <summary>
    ///     With no lobby host recorded there is no name to inherit and the address stands as the
    ///     identity. An empty identity would send an empty SNI and validate against nothing.
    /// </summary>
    [Fact]
    public void NoLobbyHost_LeavesTheAddressAsTheIdentity()
        => Assert.Equal(
            "10.0.0.7",
            ConnectionManager.ChooseRedirectIdentity(string.Empty, [], IPAddress.Parse("10.0.0.7")));
}
