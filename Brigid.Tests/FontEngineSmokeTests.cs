using Brigid.Rendering;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     Headless smoke checks for the FontStashSharp-backed <see cref="FontEngine" />: the bundled TTF parses and loads,
///     and metric queries (used by all UI layout) work without a GraphicsDevice — measurement happens off the render path.
/// </summary>
public class FontEngineSmokeTests
{
    [Fact]
    public void Initialize_LoadsPrimaryFont()
    {
        FontEngine.Initialize();

        Assert.NotNull(FontEngine.Instance);
    }

    [Fact]
    public void MeasureWidth_ReturnsPositiveForText_AndZeroForEmpty()
    {
        FontEngine.Initialize();

        Assert.Equal(0, FontEngine.Instance.MeasureWidth(string.Empty));

        var width = FontEngine.Instance.MeasureWidth("Hybrasyl");
        Assert.True(width > 0, $"expected positive width, got {width}");

        //wider string must measure wider — sanity that per-string layout is real, not a constant
        Assert.True(FontEngine.Instance.MeasureWidth("Hybrasyl Brigid") > width);
    }

    [Fact]
    public void LineHeight_IsSane()
    {
        FontEngine.Initialize();

        var lineHeight = FontEngine.Instance.LineHeight;
        Assert.InRange(lineHeight, 1, 64);
    }
}
