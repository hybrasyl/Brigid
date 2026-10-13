#region
using Brigid.Rendering;
using Chaos.DarkAges.Definitions;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The rain test that starts the rain bed. Only the low nibble of the map flags holds the weather; the high bits
///     hold other flags, such as darkness.
/// </summary>
public class WeatherRendererTests
{
    [Theory]
    [InlineData(0x02, true)]
    [InlineData(0x12, true)]
    [InlineData(0x42, true)]
    [InlineData(0x00, false)]
    [InlineData(0x01, false)]
    [InlineData(0x03, false)]
    [InlineData(0x20, false)]
    public void IsRain_ReadsOnlyTheLowNibble(byte flags, bool expected) => Assert.Equal(expected, WeatherRenderer.IsRain((MapFlags)flags));
}
