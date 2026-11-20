#region
using System.Collections.Concurrent;
using Brigid.Data;
#endregion

namespace Brigid.Systems;

/// <summary>
///     The saves that did not go through (<see cref="GuardedFile.NotSaved" />) and wait to show to the player. The event
///     can be raised on any thread, so it waits here; <c>ChaosGame</c> gives each one to the active screen on the game
///     loop (<see cref="ShowOn" />): the orange bar in the world, a main-menu alert in the lobby, and a line below the
///     panel in the launcher. <see cref="GuardedFile" /> raises one event for each file in a session. The texts are in
///     <c>MenuAlerts</c>.
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

    /// <summary>
    ///     Gives each waiting save to <paramref name="screen" /> when it shows them (<see cref="ISaveNoticeHost" />). On
    ///     another screen they wait. Call it on the game loop.
    /// </summary>
    public static void ShowOn(object? screen)
    {
        if (screen is not ISaveNoticeHost host)
            return;

        while (TryTake(out var file, out var result))
            host.ShowSaveNotice(file, result);
    }

    /// <summary>Takes the next save that did not go through and waits to show.</summary>
    public static bool TryTake(out GuardedFile file, out SaveResult result)
    {
        var taken = Waiting.TryDequeue(out var notice);
        (file, result) = notice;

        return taken;
    }
}

/// <summary>A screen that shows the saves that did not go through (<see cref="SaveNotices.ShowOn" />).</summary>
public interface ISaveNoticeHost
{
    /// <summary>Shows one save that did not go through, with why.</summary>
    void ShowSaveNotice(GuardedFile file, SaveResult result);
}
