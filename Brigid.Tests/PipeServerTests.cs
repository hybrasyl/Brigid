using System.IO.Pipes;
using Brigid.TestDriver;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     BRIG-184: the test driver closes the client when the test is gone. A test that stopped before it connected never
///     closes the pipe, so the driver also counts a test that does not connect in time as gone.
/// </summary>
public sealed class PipeServerTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    //a client of another user of the machine cannot drive the test client
    [Fact]
    public void ThePipeTakesOnlyAClientOfTheSameUser() => Assert.True(PipeServer.Options.HasFlag(PipeOptions.CurrentUserOnly));

    [Fact]
    public async Task ATestThatDoesNotConnectInTimeIsGone()
    {
        using var server = new PipeServer(NewPipeName(), _ => { }, TimeSpan.FromMilliseconds(200));

        Assert.True(await BecomesTrueAsync(() => server.ClientLeft));
    }

    [Fact]
    public async Task ATestThatConnectsInTimeStaysUntilItClosesThePipe()
    {
        var name = NewPipeName();
        using var server = new PipeServer(name, _ => { }, TimeSpan.FromSeconds(2));
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync((int)Wait.TotalMilliseconds);

        //past the time limit, the connected test is still there
        Assert.False(await BecomesTrueAsync(() => server.ClientLeft, TimeSpan.FromSeconds(3)));

        await client.DisposeAsync();
        Assert.True(await BecomesTrueAsync(() => server.ClientLeft));
    }

    //short, as in BrigidApp: on macOS the socket path $TMPDIR/CoreFxPipe_<name> must fit in 104 bytes
    private static string NewPipeName() => "brigid-pipe-test-" + Guid.NewGuid().ToString("N")[..12];

    private static async Task<bool> BecomesTrueAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? Wait);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            await Task.Delay(50);
        }

        return condition();
    }
}
