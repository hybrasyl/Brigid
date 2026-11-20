#region
using System.Text.Json.Nodes;
using Brigid.DevModules;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.TestDriver;

/// <summary>
///     The BRIG-184 test driver. When <c>BRIGID_TEST_PIPE</c> is set, it listens on that named pipe, runs each command on
///     the game thread at the start of an update, and replies with the result. Its input goes through SDL and
///     <see cref="InputBuffer" />, so hit testing, the control stack and focus run as for a player; it sends no packet.
///     Without the variable it does nothing.
/// </summary>
public sealed class TestDriverModule : IDevModule
{
    /// <summary>The environment variable with the name of the pipe.</summary>
    public const string PIPE_VARIABLE = "BRIGID_TEST_PIPE";

    private IDevHost? Host;
    private PipeServer? Server;
    private DriverContext? Context;
    private (JsonNode? Id, IEnumerator<JsonObject?> Steps)? Current;

    public string Name => "Test driver";

    public string Description => "Runs the smoke test commands (BRIG-184).";

    public IReadOnlyList<DevAction> Actions => [];

    public void Initialize(IDevHost host)
    {
        var pipe = Environment.GetEnvironmentVariable(PIPE_VARIABLE);

        if (string.IsNullOrWhiteSpace(pipe))
        {
            host.Log($"test driver: {PIPE_VARIABLE} is not set, so the driver does nothing");

            return;
        }

        Host = host;
        Server = new PipeServer(pipe, host.Log);
        host.UpdateStarted += OnUpdateStarted;
        host.Log($"test driver: listening on {pipe}");
    }

    public void Shutdown()
    {
        if (Host is not null)
            Host.UpdateStarted -= OnUpdateStarted;

        Server?.Dispose();
        InputBuffer.ClearPointerOverride();
        Host = null;
        Server = null;
    }

    private void OnUpdateStarted(GameTime gameTime)
    {
        if (Host is null || Server is null)
            return;

        //a throw here would remove the listener, and the client would stay open with no test: it closes the client
        //instead, so the test fails at once with the reason in the log
        if (Context is null)
            try
            {
                Context = new DriverContext(Host, SdlInput.WindowId(Host.Game.Window.Handle));
                Host.Log($"test driver: the window is on display {SdlInput.MoveToSecondDisplay(Host.Game.Window.Handle)}");
            } catch (Exception ex)
            {
                Host.Log($"test driver: could not start, so the client closes: {ex}");
                Host.Game.Exit();

                return;
            }

        //the test asked to quit, or it is gone (it ended, was cancelled, or its process stopped): a client must not stay
        //open with no test
        if (Context.ExitRequested || Server.ClientLeft)
        {
            Host.Game.Exit();

            return;
        }

        if (Current is null && Server.Requests.TryDequeue(out var request))
        {
            var id = request["id"]?.DeepClone();

            //an error in a command goes back to the test as its reply; it must not reach the host, which would remove the
            //driver's listener
            try
            {
                Current = (id, DriverCommands.Start(request, Context));
            } catch (Exception ex)
            {
                Fail(id, ex);

                return;
            }
        }

        if (Current is not { } current)
            return;

        try
        {
            if (!current.Steps.MoveNext())
                Finish(current.Id, new JsonObject());
            else if (current.Steps.Current is { } result)
                Finish(current.Id, result);
        } catch (Exception ex)
        {
            Fail(current.Id, ex);
        }
    }

    private void Finish(JsonNode? id, JsonObject result)
    {
        EndCurrent();
        Server!.Send(new JsonObject { ["id"] = id, ["ok"] = true, ["result"] = result });
    }

    //a DriverException is a reason for the test; any other exception is a bug, and its stack goes back too
    private void Fail(JsonNode? id, Exception ex)
    {
        EndCurrent();
        Server!.Send(new JsonObject { ["id"] = id, ["ok"] = false, ["error"] = ex is DriverException ? ex.Message : ex.ToString() });
    }

    //disposing the steps runs their finally blocks, which put the modifier keys back
    private void EndCurrent()
    {
        Current?.Steps.Dispose();
        Current = null;
    }
}
