#region
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
#endregion

namespace Brigid.TestDriver;

/// <summary>
///     The command channel of the test driver (BRIG-184): a named pipe for one client, the test that started this client,
///     and only for the current user. Each message is one line of JSON. The reader thread puts each request in
///     <see cref="Requests" />, and the game thread takes them; a reply goes through <see cref="Send" /> to the writer
///     thread, so the game thread never waits on the pipe. When the test closes the pipe (it ended, or its process
///     stopped), or does not connect in time (<see cref="DefaultConnectTimeout" />), <see cref="ClientLeft" /> becomes true, and the
///     driver closes the client. A .NET named pipe is a Unix domain socket on Linux and macOS.
/// </summary>
internal sealed class PipeServer : IDisposable
{
    //longer than the harness waits for the start, so only a test that is gone gets here
    internal static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromMinutes(3);

    private readonly TimeSpan ConnectTimeout;
    private readonly NamedPipeServerStream Pipe;
    private readonly Action<string> Log;
    private readonly CancellationTokenSource Stop = new();
    private readonly BlockingCollection<string> Outbound = new();
    private volatile StreamWriter? Output;
    private volatile bool Left;

    internal ConcurrentQueue<JsonObject> Requests { get; } = new();

    /// <summary>True when the test connected and then closed the pipe, or did not connect in time.</summary>
    internal bool ClientLeft => Left;

    /// <summary>
    ///     The options of the pipe. <see cref="PipeOptions.CurrentUserOnly" /> refuses a client of another user of the
    ///     machine, so only the test that started the client can drive it.
    /// </summary>
    internal const PipeOptions Options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

    internal PipeServer(string pipeName, Action<string> log, TimeSpan? connectTimeout = null)
    {
        Log = log;
        ConnectTimeout = connectTimeout ?? DefaultConnectTimeout;
        Pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            Options);
        new Thread(ReadLoop) { IsBackground = true, Name = "brigid-test-driver-read" }.Start();
        new Thread(WriteLoop) { IsBackground = true, Name = "brigid-test-driver-write" }.Start();
    }

    internal void Send(JsonObject reply) => Outbound.Add(reply.ToJsonString());

    public void Dispose()
    {
        Stop.Cancel();
        Outbound.CompleteAdding();

        //a reader blocked in ReadLine returns when the pipe closes
        Pipe.Dispose();
    }

    private void ReadLoop()
    {
        try
        {
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(Stop.Token))
            {
                connect.CancelAfter(ConnectTimeout);

                try
                {
                    Pipe.WaitForConnectionAsync(connect.Token).GetAwaiter().GetResult();
                } catch (OperationCanceledException) when (!Stop.IsCancellationRequested)
                {
                    Log($"test driver: no test connected in {ConnectTimeout.TotalSeconds} s");
                    Left = true;

                    return;
                }
            }

            Log("test driver: the test connected");

            using var reader = new StreamReader(Pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            Output = new StreamWriter(Pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

            while (!Stop.IsCancellationRequested && reader.ReadLine() is { } line)
                Accept(line);

            Log("test driver: the test closed the pipe");
        } catch (OperationCanceledException)
        {
            return;
        } catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Log($"test driver: pipe error: {ex.Message}");
        }

        Output = null;
        Left = true;
    }

    //a line that is not a JSON object is logged and skipped; the connection stays
    private void Accept(string line)
    {
        try
        {
            if (JsonNode.Parse(line) is JsonObject request)
                Requests.Enqueue(request);
            else
                Log($"test driver: skipped a line that is not a JSON object: {line}");
        } catch (JsonException ex)
        {
            Log($"test driver: skipped a line that is not JSON ({ex.Message}): {line}");
        }
    }

    private void WriteLoop()
    {
        foreach (var line in Outbound.GetConsumingEnumerable())
            try
            {
                Output?.WriteLine(line);
            } catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                Log($"test driver: could not send a reply: {ex.Message}");
            }
    }
}
