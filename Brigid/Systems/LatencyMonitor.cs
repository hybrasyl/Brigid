namespace Brigid.Systems;

/// <summary>The bucket of a latency reading, which each HUD shows as one icon.</summary>
public enum LatencyQuality
{
    /// <summary>No reading.</summary>
    None,

    /// <summary>Under 100 ms.</summary>
    Good,

    /// <summary>100 ms to under 1000 ms.</summary>
    Fair,

    /// <summary>1000 ms or more.</summary>
    Poor
}

/// <summary>
///     Passive sink for the most recent application-layer round-trip-time measurement.
///     Producers call <see cref="Update" /> with a fresh sample (or null to clear); the HUD subscribes to
///     <see cref="LatencyChanged" /> and reads <see cref="LatencyMs" />.
/// </summary>
public static class LatencyMonitor
{
    /// <summary>The bucket of <paramref name="ms" />: the one rule that the latency icon of each HUD uses.</summary>
    public static LatencyQuality QualityOf(long? ms)
        => ms switch
        {
            null   => LatencyQuality.None,
            < 100  => LatencyQuality.Good,
            < 1000 => LatencyQuality.Fair,
            _      => LatencyQuality.Poor
        };

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
