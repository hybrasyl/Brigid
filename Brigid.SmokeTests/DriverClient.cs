#region
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
#endregion

namespace Brigid.SmokeTests;

/// <summary>A command that the test driver refused or that failed in the client.</summary>
public sealed class SmokeCommandException(string message) : Exception(message);

/// <summary>
///     The test side of the driver's pipe (BRIG-184): one JSON line for each request, with an id, and one line for each
///     reply, with the same id. One command runs at a time.
/// </summary>
internal sealed class DriverClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream Pipe;
    private readonly StreamReader Reader;
    private readonly StreamWriter Writer;
    private readonly SemaphoreSlim OneAtATime = new(1, 1);
    private int NextId;

    private DriverClient(NamedPipeClientStream pipe)
    {
        Pipe = pipe;
        Reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        Writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
    }

    internal static async Task<DriverClient> ConnectAsync(string pipeName, CancellationToken cancel)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancel);

        return new DriverClient(pipe);
    }

    /// <summary>Sends <paramref name="request" /> and returns the result of its reply.</summary>
    internal async Task<JsonObject> SendAsync(JsonObject request, TimeSpan timeout)
    {
        await OneAtATime.WaitAsync();

        try
        {
            var id = ++NextId;
            request["id"] = id;
            using var cancel = new CancellationTokenSource(timeout);
            await Writer.WriteLineAsync(request.ToJsonString().AsMemory(), cancel.Token);

            while (true)
            {
                var line = await Reader.ReadLineAsync(cancel.Token) ?? throw new IOException("the client closed the pipe");

                if (JsonNode.Parse(line) is not JsonObject reply || reply["id"]?.GetValue<int>() != id)
                    continue;

                if (reply["ok"]?.GetValue<bool>() == true)
                    return reply["result"] as JsonObject ?? new JsonObject();

                throw new SmokeCommandException($"{request["cmd"]}: {reply["error"]}");
            }
        } finally
        {
            OneAtATime.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Writer.DisposeAsync();
        Reader.Dispose();
        await Pipe.DisposeAsync();
        OneAtATime.Dispose();
    }
}
