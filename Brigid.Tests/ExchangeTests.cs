using Brigid.ViewModel;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     BRIG-140: the server's Accepted event (0x42 action 0x05) names the side whose accept it confirms: 0 is you, 1 is the
///     other player. Brigid closed the exchange on a 0, which is Hybrasyl's "both have accepted" form; retail sends the 0
///     to the player who accepts, so the window closed while the trade was still open. The exchange now closes, as on the
///     retail client, only when both sides are confirmed.
/// </summary>
public sealed class ExchangeTests
{
    private const string COMPLETED = "Exchange completed.";
    private const string FIRST = "first confirmation";

    private readonly Exchange Exchange = new();
    private readonly List<string?> Closes = [];
    private readonly List<bool> Accepts = [];

    public ExchangeTests()
    {
        Exchange.Closed += Closes.Add;
        Exchange.Accepted += Accepts.Add;
        Exchange.Start(7, "Sabrael");
    }

    //retail, you accept first: your side to you at once, then their side when they accept
    [Fact]
    public void RetailYouFirst_ClosesOnlyWhenTheyAccept()
    {
        Exchange.SetAccepted(false, FIRST);

        Assert.True(Exchange.IsActive);
        Assert.True(Exchange.IsMyAccepted);
        Assert.Empty(Closes);

        Exchange.SetAccepted(true, COMPLETED);

        Assert.False(Exchange.IsActive);
        Assert.Equal([COMPLETED], Closes);
        Assert.Equal([false, true], Accepts);
    }

    //retail, they accept first: their side, then your side when you accept
    [Fact]
    public void RetailTheyFirst_ClosesOnlyWhenYouAccept()
    {
        Exchange.SetAccepted(true, FIRST);

        Assert.True(Exchange.IsActive);
        Assert.True(Exchange.IsOtherAccepted);
        Assert.Empty(Closes);

        Exchange.SetAccepted(false, COMPLETED);

        Assert.Equal([COMPLETED], Closes);
    }

    //Hybrasyl: their side on each accept by the other player, then your side to both players when both have accepted.
    //Hybrasyl sends the same message on both events
    [Fact]
    public void Hybrasyl_ClosesOnYourSideAfterBothAccept()
    {
        Exchange.SetAccepted(true, COMPLETED);

        Assert.Empty(Closes);

        Exchange.SetAccepted(false, COMPLETED);

        Assert.Equal([COMPLETED], Closes);
        Assert.False(Exchange.IsMyAccepted);
        Assert.False(Exchange.IsOtherAccepted);
    }

    [Fact]
    public void ARepeatedEventForOneSideDoesNotClose()
    {
        Exchange.SetAccepted(true, COMPLETED);
        Exchange.SetAccepted(true, COMPLETED);

        Assert.True(Exchange.IsActive);
        Assert.Empty(Closes);
    }

    [Fact]
    public void AnEventWithNoExchangeOpenDoesNothing()
    {
        Exchange.Close();
        Closes.Clear();

        Exchange.SetAccepted(false, COMPLETED);
        Exchange.SetAccepted(true, COMPLETED);

        Assert.Empty(Closes);
        Assert.Empty(Accepts);
        Assert.False(Exchange.IsMyAccepted);
    }

    //one side only, so the exchange stays open: a side that a Start does not reset would close the next exchange on the
    //other side's accept alone
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANewExchangeStartsWithNeitherSideAccepted(bool rightSide)
    {
        Exchange.SetAccepted(rightSide, null);

        Exchange.Start(8, "Kedian");

        Assert.False(Exchange.IsMyAccepted);
        Assert.False(Exchange.IsOtherAccepted);
    }
}
