#region
using System.Globalization;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Hud.Main;
using Brigid.Systems;
using Brigid.ViewModel;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-144 phase 2: the text of the top bar, the latency buckets that both HUDs use, and the mail and parcel decode
///     (BRIG-127 D8, D15, D17, D39).
/// </summary>
public class HudFormatTests
{
    [Theory]
    [InlineData(0u, "0")]
    [InlineData(12480u, "12,480")]
    [InlineData(999999u, "999,999")]
    [InlineData(1000000u, "1.0M")]
    [InlineData(1999999u, "1.9M")]
    [InlineData(9999999u, "9.9M")]
    [InlineData(10000000u, "10M")]
    [InlineData(999999999u, "999M")]
    [InlineData(1000000000u, "1.0B")]
    [InlineData(uint.MaxValue, "4.2B")]
    public void GoldShowsInShortFormWhenItIsLarge(uint gold, string expected) => Assert.Equal(expected, HudFormat.Gold(gold));

    [Fact]
    public void ShortFormIsNeverMoreThanTheGold() => Assert.Equal("1.9M", HudFormat.Gold(1_999_999));

    [Fact]
    public void GoldTooltipShowsTheFullAmountOnlyWhenTheBoxIsShort()
    {
        Assert.Null(HudFormat.GoldTooltip(999_999));
        Assert.Equal("1,250,000 gold", HudFormat.GoldTooltip(1_250_000));
    }

    [Fact]
    public void WeightAndZoneText()
    {
        Assert.Equal("42/96", HudFormat.Weight(42, 96));
        Assert.Equal("Mileth Village (54, 37)", HudFormat.Zone("Mileth Village", 54, 37));
        Assert.Equal("(54, 37)", HudFormat.Zone("", 54, 37));
    }

    [Fact]
    public void TheClockUsesTheShortTimeOfTheCulture()
    {
        var time = new DateTime(2026, 11, 17, 21, 14, 59);

        Assert.Equal("21:14", HudFormat.Clock(time, CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal("9:14 PM", HudFormat.Clock(time, CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void TheClockUsesAPlainSpace()
        => Assert.DoesNotContain('\u202F', HudFormat.Clock(new DateTime(2026, 11, 17, 21, 14, 0), CultureInfo.GetCultureInfo("en-US")));

    [Fact]
    public void ATooltipBelowOrAboveStaysOnTheScreen()
    {
        var box = new Microsoft.Xna.Framework.Rectangle(571, 4, 65, 17);

        Assert.Equal((540, 23), TooltipPlacement.Below(box, 100, 30, 2));
        Assert.Equal((10, 408), TooltipPlacement.Above(new Microsoft.Xna.Framework.Rectangle(10, 440, 18, 18), 100, 30, 2));
        Assert.Equal((0, 0), TooltipPlacement.Above(new Microsoft.Xna.Framework.Rectangle(-5, 10, 18, 18), 100, 30, 2));
    }

    [Fact]
    public void TheMonitorKeepsOneReading()
    {
        try
        {
            LatencyMonitor.Update(48);
            Assert.Equal(48, LatencyMonitor.LatencyMs);

            LatencyMonitor.Update(null);
            Assert.Null(LatencyMonitor.LatencyMs);

            LatencyMonitor.Update(-5);
            Assert.Null(LatencyMonitor.LatencyMs);
        } finally
        {
            LatencyMonitor.Update(null);
        }
    }

    [Theory]
    [InlineData(null, LatencyQuality.None)]
    [InlineData(0L, LatencyQuality.Good)]
    [InlineData(99L, LatencyQuality.Good)]
    [InlineData(100L, LatencyQuality.Fair)]
    [InlineData(999L, LatencyQuality.Fair)]
    [InlineData(1000L, LatencyQuality.Poor)]
    public void LatencyBuckets(long? ms, LatencyQuality expected) => Assert.Equal(expected, LatencyMonitor.QualityOf(ms));

    //retail decodes the two nibbles only when the flag and the secondary section are both present
    [Theory]
    [InlineData(0x10, true, false)]
    [InlineData(0x01, false, true)]
    [InlineData(0x11, true, true)]
    [InlineData(0x00, false, false)]
    [InlineData(0x20, true, false)]
    [InlineData(0x02, false, true)]
    public void TheMailStatusNibblesDecodeWithTheFlag(byte status, bool mail, bool parcel)
        => Assert.Equal((mail, parcel), PlayerAttributes.DecodeMail(true, status, false, false));

    [Fact]
    public void NoFlagMeansNothingIsUnread()
        => Assert.Equal((false, false), PlayerAttributes.DecodeMail(false, 0x11, true, true));

    //Hybrasyl sends the flag alone when a letter arrives, and with the secondary section later
    [Fact]
    public void TheFlagAloneKeepsWhatWasKnown_ElseShowsMail()
    {
        Assert.Equal((false, true), PlayerAttributes.DecodeMail(true, null, false, true));
        Assert.Equal((true, false), PlayerAttributes.DecodeMail(true, null, false, false));
    }
}
