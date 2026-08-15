#region
using DALib.Networking.Packets.Server;
using Hybrasyl.Protocol.Negotiation;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     Guards the assembly boundary between Hybrasyl.Protocol and DALib. Brigid pins a newer DALib
///     than the published Hybrasyl.Protocol package was compiled against, and NuGet unifies the
///     graph on the newer one — so a DALib surface change Hybrasyl.Protocol depends on fails at
///     runtime with <see cref="System.MissingMethodException" /> and not at build time. These
///     exercise the two calls that actually cross that boundary.
/// </summary>
public class ProtocolPackageBindingTests
{
    /// <summary>
    ///     <see cref="CapabilityMarker.BuildGreetingBody()" /> constructs DALib's
    ///     <see cref="AcceptConnectionPacket" /> and calls its <c>ToBody</c> across the assembly
    ///     boundary — the load-bearing cross-assembly call on the client's detection path.
    /// </summary>
    [Fact]
    public void BuildGreetingBody_AppendsMarkerToDALibGreeting_AcrossTheAssemblyBoundary()
    {
        var body = CapabilityMarker.Current.BuildGreetingBody();

        var greetingLength = new AcceptConnectionPacket().ToBody()
                                                         .Length;

        Assert.Equal(greetingLength + CapabilityMarker.Length, body.Length);
        Assert.Equal(CapabilityMarker.Magic.ToArray(), body[greetingLength..(greetingLength + 4)]);
    }

    /// <summary>
    ///     The marker survives a round trip through the greeting body, which is exactly what
    ///     Brigid's lobby detection performs on the received <c>0x7E</c>.
    /// </summary>
    [Fact]
    public void TryRead_RecoversTheMarker_FromAGreetingBuiltByTheLibrary()
    {
        var body = CapabilityMarker.Current.BuildGreetingBody();

        Assert.True(CapabilityMarker.TryRead(body, out var marker));
        Assert.Equal(CapabilityMarker.CurrentVersion, marker.Version);
        Assert.Equal(CapabilityFlags.None, marker.Flags);
    }

    /// <summary>
    ///     A retail greeting carries no marker, so detection must decline it — the "silence means
    ///     retail forever" fallback. Without this the passing test above would also pass against a
    ///     detector that always returned true.
    /// </summary>
    [Fact]
    public void TryRead_DeclinesARetailGreeting_CarryingNoMarker()
    {
        var retailBody = new AcceptConnectionPacket().ToBody();

        Assert.False(CapabilityMarker.TryRead(retailBody, out _));
    }
}
