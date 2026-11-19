namespace Brigid.Tests;

// A clock in milliseconds that a test moves, for code that takes a TimeProvider.
internal sealed class ManualClock : TimeProvider
{
    public long Ms;

    public override long TimestampFrequency => 1000;

    public override long GetTimestamp() => Ms;
}
