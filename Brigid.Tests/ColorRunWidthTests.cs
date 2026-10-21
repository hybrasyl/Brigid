using Brigid.Rendering;
using Xunit;

namespace Brigid.Tests;

// A line with {=x} color codes is drawn one color run at a time, and the caret is placed with MeasureWidth. The draw
// moved the pen by a width at the default tracking plus 1 px for each character, but MeasureWidth measured each run
// with the spacing, and rounded each run to a whole pixel. With the chat spacing (+1), the caret fell behind the text
// at each color code. Both now add the same unrounded advance for each run, and MeasureWidth rounds once at the end.
[Collection("FontEngine")]
public class ColorRunWidthTests
{
    private const float CHAT_SPACING = -FontEngine.DEFAULT_TRACKING;

    public static TheoryData<float> Scales => [1f, 1.5f, 2f, 3f];

    [Theory]
    [MemberData(nameof(Scales))]
    public void MeasureWidth_IsTheUnroundedRunWidthsRoundedOnce(float scale)
    {
        FontEngine.Initialize(0);
        var engine = FontEngine.Instance;
        var px = engine.UiSize;

        try
        {
            engine.SetLayoutScale(scale, scale);

            var runs = new[] { "Hello ", "there, ", "Mileth ", "and Suomi" };
            var expected = (int)MathF.Round(runs.Sum(run => engine.MeasureWidthExact(run, px, FontStyle.Regular, CHAT_SPACING)));

            Assert.Equal(expected, TextRenderer.MeasureWidth("{=bHello {=cthere, {=dMileth {=eand Suomi", px, CHAT_SPACING));
        } finally
        {
            engine.SetLayoutScale(1f, 1f);
        }
    }
}
