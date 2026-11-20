#region
using Brigid.Networking;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The inventory slots of the swaps that the server has not answered (<see cref="InventorySwaps" />). After a compact
///     sent 2 to 1 and 5 to 2, a use of a slot that the client showed acted on the server's new item of that slot.
/// </summary>
public class InventorySwapsTests
{
    private long Now;

    [Fact]
    public void ASwapHoldsBothSlotsAndNoOther()
    {
        var swaps = new InventorySwaps(() => Now);

        swaps.Sent(2, 5);

        Assert.True(swaps.Holds(2));
        Assert.True(swaps.Holds(5));
        Assert.False(swaps.Holds(1));
    }

    [Fact]
    public void AnUpdateOfASlotEndsItsHold()
    {
        var swaps = new InventorySwaps(() => Now);
        swaps.Sent(2, 5);

        swaps.Answered(5);

        Assert.True(swaps.Holds(2));
        Assert.False(swaps.Holds(5));

        swaps.Answered(2);
        Assert.False(swaps.Holds(2));
    }

    //the server can refuse a swap and send nothing for it
    [Fact]
    public void AHoldEndsAfterTheLongestWait()
    {
        var swaps = new InventorySwaps(() => Now);
        swaps.Sent(2, 5);

        Now += InventorySwaps.WAIT_MS - 1;
        Assert.True(swaps.Holds(2));

        Now += 1;
        Assert.False(swaps.Holds(2));
    }

    [Fact]
    public void ANewSwapOfASlotHoldsItAgainFromItsOwnTime()
    {
        var swaps = new InventorySwaps(() => Now);
        swaps.Sent(2, 5);

        Now += InventorySwaps.WAIT_MS - 1;
        swaps.Sent(5, 7);
        Now += 1;

        Assert.False(swaps.Holds(2));
        Assert.True(swaps.Holds(5));
        Assert.True(swaps.Holds(7));
    }
}
