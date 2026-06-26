namespace Brigid.Systems;

/// <summary>
///     Passive sink for the most recent application-layer round-trip-time measurement.
///     Producers call <see cref="Update" /> with a fresh sample (or null to clear); the HUD subscribes to
///     <see cref="LatencyChanged" /> and reads <see cref="LatencyMs" />.
/// </summary>
public static class LatencyMonitor
{
    /// <summary>
    ///     The most recent round-trip time in milliseconds, or null if no measurement is available
    ///     (not yet connected, producer hasn't reported a sample, or the connection was just dropped).
    /// </summary>
    public static long? LatencyMs { get; private set; }

    /// <summary>
    ///     Fires whenever <see cref="LatencyMs" /> changes. May be raised on any thread, including the network
    ///     receive thread — subscribers must not block, and any non-trivial work should be marshalled to the
    ///     game-loop thread by the consumer.
    /// </summary>
    public static event Action? LatencyChanged;

    /// <summary>
    ///     Records a new latency sample. Pass null to clear the current reading (e.g. on disconnect or when
    ///     the producer can't measure right now). No-ops when the value is unchanged.
    /// </summary>
    public static void Update(long? rttMs)
    {
        if (rttMs == LatencyMs)
            return;

        LatencyMs = rttMs;
        LatencyChanged?.Invoke();
    }
}
