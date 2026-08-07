using Brigid.Networking;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     The Rogue/Monk pre-swap applied to stage-4 recruit boxes on retail.
/// </summary>
/// <remarks>
///     Pinned with the measured values rather than abstract ones, because the direction is
///     trivial to invert and inverting it is silent: the box still round-trips through USDA
///     consistently, it just gates the wrong class. The two cases below are the exact exchanges
///     captured against USDA on 2026-08-07.
/// </remarks>
public class RetailGroupBoxQuirkTests
{
    /// <summary>
    ///     Player wants 5 monks and 1 rogue. USDA reads byte 2 as Monk, so the 5 must be placed in
    ///     the packet's maxRogue field to land in USDA's monk cap.
    /// </summary>
    [Fact]
    public void OnRetail_TheMonkCapIsSentInTheRogueField()
    {
        var (rogueField, monkField) = RetailGroupBoxQuirk.CapsForWire(rogue: 1, monk: 5, isRetail: true);

        Assert.Equal(5, rogueField);
        Assert.Equal(1, monkField);
    }

    /// <summary>
    ///     The mirror: 5 rogues wanted, so the 5 goes in maxMonk. Measured — sending it in maxRogue
    ///     instead admitted monks.
    /// </summary>
    [Fact]
    public void OnRetail_TheRogueCapIsSentInTheMonkField()
    {
        var (rogueField, monkField) = RetailGroupBoxQuirk.CapsForWire(rogue: 5, monk: 1, isRetail: true);

        Assert.Equal(1, rogueField);
        Assert.Equal(5, monkField);
    }

    /// <summary>
    ///     Hybrasyl reads the caps correctly. Applying the swap there would introduce the very bug
    ///     it works around, so the pass-through case is pinned too.
    /// </summary>
    [Theory]
    [InlineData(1, 5)]
    [InlineData(5, 1)]
    [InlineData(0, 0)]
    [InlineData(13, 13)]
    public void OffRetail_CapsPassThroughUntouched(byte rogue, byte monk)
    {
        var (rogueField, monkField) = RetailGroupBoxQuirk.CapsForWire(rogue, monk, isRetail: false);

        Assert.Equal(rogue, rogueField);
        Assert.Equal(monk, monkField);
    }

    /// <summary>Equal caps are the one input the swap cannot be detected on. Stated, not relied on.</summary>
    [Fact]
    public void EqualCapsAreIndistinguishable()
    {
        Assert.Equal(
            RetailGroupBoxQuirk.CapsForWire(3, 3, isRetail: true),
            RetailGroupBoxQuirk.CapsForWire(3, 3, isRetail: false));
    }
}
