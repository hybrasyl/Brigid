using System.Reflection;
using Brigid.Networking;
using Brigid.Networking.Definitions;
using DALib.Networking.Packets.Server;
using DALib.Networking.Wire;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     BRIG-184: the server can send the first world packets before the lobby screen switches to the world screen.
///     <see cref="ConnectionManager.WorldEntering" /> is raised on the game thread the first time
///     <see cref="ConnectionManager.ProcessPackets" /> runs in the world state, before any packet is dispatched, so the
///     lobby switches there. It is raised one time for each hop, and never outside the world state.
/// </summary>
public sealed class WorldEnteringTests
{
    [Fact]
    public void ItIsRaisedOneTimeForEachHopToTheWorld()
    {
        using var manager = new ConnectionManager();
        var raised = 0;
        manager.WorldEntering += () => raised++;
        List<IServerPacket> buffer = [];

        manager.SetStateForTest(ConnectionState.World);
        manager.ProcessPackets(buffer);
        manager.ProcessPackets(buffer);

        Assert.Equal(1, raised);

        //a world transfer: the next hop raises it again
        manager.ResetEntryForHop();
        manager.ProcessPackets(buffer);

        Assert.Equal(2, raised);
    }

    [Theory]
    [InlineData(ConnectionState.Disconnected)]
    [InlineData(ConnectionState.Connecting)]
    [InlineData(ConnectionState.Lobby)]
    [InlineData(ConnectionState.Login)]
    public void ItIsNotRaisedOutsideTheWorld(ConnectionState state)
    {
        using var manager = new ConnectionManager();
        var raised = 0;
        manager.WorldEntering += () => raised++;

        manager.SetStateForTest(state);
        manager.ProcessPackets([]);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void AHopClearsTheMapOfTheLastConnection()
    {
        using var manager = new ConnectionManager();
        typeof(ConnectionManager).GetMethod("HandleMapInfo", BindingFlags.Instance | BindingFlags.NonPublic)!
                                 .Invoke(manager, [new MapInfoPacket { MapId = 137, Name = "a room", Width = 30, Height = 20, Flags = default, Checksum = default }]);
        Assert.NotNull(manager.MapInfo);

        manager.ResetEntryForHop();

        Assert.Null(manager.MapInfo);
    }
}
