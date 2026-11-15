#region
using System.Collections.Concurrent;
using Brigid.Data;
#endregion

namespace Brigid.Systems;

/// <summary>
///     The saves that did not go through (<see cref="GuardedFile.NotSaved" />) and wait to show to the player. The event
///     can be raised on any thread, so it waits here; <c>ChaosGame</c> shows each one on the game loop, on the orange bar
///     in the world and as a main-menu alert in the lobby. One on another screen (the launcher) waits for one of the two.
///     <see cref="GuardedFile" /> raises one event for each file in a session. The texts are in <c>MenuAlerts</c>.
/// </summary>
public static class SaveNotices
{
    private static readonly ConcurrentQueue<(GuardedFile File, SaveResult Result)> Waiting = new();
    private static int Started;

    /// <summary>Starts to collect the saves that did not go through. Call it one time, before the first save.</summary>
    public static void Start()
    {
        if (Interlocked.Exchange(ref Started, 1) == 0)
            GuardedFile.NotSaved += (file, result) => Waiting.Enqueue((file, result));
    }

    /// <summary>Takes the next save that did not go through and waits to show.</summary>
    public static bool TryTake(out GuardedFile file, out SaveResult result)
    {
        var taken = Waiting.TryDequeue(out var notice);
        (file, result) = notice;

        return taken;
    }
}
