#region
using Chaos.DarkAges.Definitions;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     The stat points of the Details window (BRIG-151, D34): pending points that the player adds and removes before
///     Confirm. On retail each point goes to the server at once and cannot be undone, so the client holds the points until
///     Confirm. The down arrow removes only a pending point of its own stat. The server's unspent points cap the pending
///     points: when they fall (a refused point, a point from another window), the newest pending points go first. The
///     points of a Confirm count as spent until the server's unspent points show them, for <see cref="ANSWER_WAIT_MS" />
///     at most (the server can refuse a point and send nothing), so a second Confirm does not send more points than the
///     player has.
/// </summary>
internal sealed class StatRaise
{
    /// <summary>The longest wait for the server's answer to a Confirm.</summary>
    public const long ANSWER_WAIT_MS = 2000;

    /// <summary>The stats, in the order of the rows.</summary>
    public static readonly Stat[] Stats = [Stat.STR, Stat.INT, Stat.WIS, Stat.CON, Stat.DEX];

    //the pending points, oldest first
    private readonly List<Stat> Points = [];
    private readonly Func<long> Clock;
    private int Sent;
    private long SentAt;

    /// <param name="clock">The time in milliseconds; null for <see cref="Environment.TickCount64" />.</param>
    public StatRaise(Func<long>? clock = null) => Clock = clock ?? (() => Environment.TickCount64);

    /// <summary>The unspent points that the server sent last.</summary>
    public int Unspent { get; private set; }

    /// <summary>The points of a Confirm that the server's unspent points do not show yet.</summary>
    public int Waiting => (Sent > 0) && (Clock() - SentAt < ANSWER_WAIT_MS) ? Sent : 0;

    /// <summary>The unspent points less the pending points and the waiting points.</summary>
    public int Remaining => Unspent - Points.Count - Waiting;

    /// <summary>The pending points of <paramref name="stat" />.</summary>
    public int PendingOf(Stat stat) => Points.Count(point => point == stat);

    /// <summary>True while a point is pending.</summary>
    public bool HasPending => Points.Count > 0;

    /// <summary>Adds a pending point to <paramref name="stat" />; false when no point remains.</summary>
    public bool Add(Stat stat)
    {
        if (Remaining <= 0)
            return false;

        Points.Add(stat);

        return true;
    }

    /// <summary>Removes the newest pending point of <paramref name="stat" />; false when it has none.</summary>
    public bool Remove(Stat stat)
    {
        var index = Points.LastIndexOf(stat);

        if (index < 0)
            return false;

        Points.RemoveAt(index);

        return true;
    }

    /// <summary>
    ///     The unspent points from the server. A fall shows waiting points first; then the newest pending points go while
    ///     they are more than the points that remain.
    /// </summary>
    public void SetUnspent(int unspent)
    {
        unspent = Math.Max(0, unspent);

        if (unspent < Unspent)
            Sent = Math.Max(0, Sent - (Unspent - unspent));

        Unspent = unspent;

        while ((Points.Count > 0) && (Remaining < 0))
            Points.RemoveAt(Points.Count - 1);
    }

    /// <summary>The points to send, one for each pending point, in the order of the rows; the pending points clear.</summary>
    public IReadOnlyList<Stat> Confirm()
    {
        var points = Stats.SelectMany(stat => Enumerable.Repeat(stat, PendingOf(stat))).ToArray();
        Points.Clear();
        Sent = Waiting + points.Length;
        SentAt = Clock();

        return points;
    }

    /// <summary>Clears the pending points, with nothing sent: the window closed.</summary>
    public void Reset() => Points.Clear();
}
