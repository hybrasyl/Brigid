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
///     Producers call <see cref="Update" /> with a fresh sample (or null to clear), on any thread. Each HUD reads
///     <see cref="LatencyMs" /> at its update, on the game thread, and shows its bucket (<see cref="QualityOf" />).
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

    //no reading; a long is read and written as one value, where a long? can tear between two threads
    private const long NO_READING = -1;

    private static long Reading = NO_READING;

    /// <summary>
    ///     The most recent round-trip time in milliseconds, or null if no measurement is available
    ///     (not yet connected, producer hasn't reported a sample, or the connection was just dropped).
    /// </summary>
    public static long? LatencyMs => Volatile.Read(ref Reading) is var ms and >= 0 ? ms : null;

    /// <summary>
    ///     Records a new latency sample. Pass null to clear the current reading (e.g. on disconnect or when
    ///     the producer can't measure right now). A negative sample is no reading.
    /// </summary>
    public static void Update(long? rttMs) => Volatile.Write(ref Reading, rttMs is >= 0 ? rttMs.Value : NO_READING);
}
