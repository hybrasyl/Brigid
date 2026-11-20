namespace Brigid.Networking;

/// <summary>
///     The inventory slots of the swaps that the server has not answered yet. The server holds the new layout when it
///     gets a swap, and the client shows it only when the slot updates arrive, so until then a slot number of a swap names
///     an item that the player does not see there. <see cref="ConnectionManager" /> sends no request that names a held
///     slot. A slot is held until an update of the slot arrives, or for <see cref="WAIT_MS" /> at most, because the server
///     can refuse a swap and send nothing for it. The game loop sends and handles the packets, so one thread uses it.
/// </summary>
internal sealed class InventorySwaps(Func<long>? clock = null)
{
    /// <summary>The longest time that a slot is held.</summary>
    public const long WAIT_MS = 2000;

    private readonly Func<long> Clock = clock ?? (() => Environment.TickCount64);
    private readonly Dictionary<byte, long> SentAt = [];

    /// <summary>A swap of <paramref name="first" /> and <paramref name="second" /> went to the server.</summary>
    public void Sent(byte first, byte second)
    {
        var now = Clock();
        SentAt[first] = now;
        SentAt[second] = now;
    }

    /// <summary>An update of <paramref name="slot" /> arrived.</summary>
    public void Answered(byte slot) => SentAt.Remove(slot);

    /// <summary>True while a swap of <paramref name="slot" /> waits for its update.</summary>
    public bool Holds(byte slot) => SentAt.TryGetValue(slot, out var sent) && (Clock() - sent < WAIT_MS);
}
