using Brigid.Controls.Components;
using Brigid.Rendering;
using Xunit;

namespace Brigid.Tests;

// A click in a text box or a selectable label puts the caret at the index under the pointer. The hit test added up
// single-glyph widths, which leave out the tracking between glyphs, so it measured each character about 1 px wider than
// drawn, and the caret landed 3 or 4 characters left of a click near the end of a board line. The hit test now measures
// each prefix whole, as the caret is drawn, so a click at the drawn caret position of an index gives that index back.
[Collection("FontEngine")]
public class TextClickIndexTests
{
    private const string Line = "The board post body runs on for a while, so that the drift has room to grow.";

    public static TheoryData<float> Scales => [1f, 1.5f, 2f, 3f];

    [Theory]
    [MemberData(nameof(Scales))]
    public void IndexAtX_GivesBackTheIndexAtItsDrawnCaretPosition(float scale)
    {
        FontEngine.Initialize(0);
        var engine = FontEngine.Instance;
        var originalFace = engine.ActiveFontIndex;
        var element = new TextElement();

        try
        {
            engine.SetLayoutScale(scale, scale);

            for (var face = 0; face < engine.FontCount; face++)
            {
                engine.SetActiveFont(face);

                for (var i = 0; i <= Line.Length; i++)
                    Assert.True(
                        element.IndexAtX(Line, element.Measure(Line[..i]), colorCodesEnabled: false) == i,
                        $"face {face} at {scale}x: a click at the caret of index {i} gave another index");
            }
        } finally
        {
            engine.SetLayoutScale(1f, 1f);
            engine.SetActiveFont(originalFace);
        }
    }

    [Fact]
    public void IndexAtX_StepsOverAColorCode()
    {
        FontEngine.Initialize(0);
        var element = new TextElement();
        const string text = "ab{=bcd";

        //before "c" the caret sits after the code, never inside it
        Assert.Equal(5, element.IndexAtX(text, element.Measure("ab"), colorCodesEnabled: true));
        Assert.Equal(6, element.IndexAtX(text, element.Measure("ab{=bc"), colorCodesEnabled: true));
    }

    //BRIG-111: with codes off, "{=b" is text. The prefixes were measured with the code removed, so their widths did not
    //only grow, and the binary search could put the caret on the wrong glyph
    [Fact]
    public void IndexAtX_WithCodesOffTreatsACodeAsText()
    {
        FontEngine.Initialize(0);
        var element = new TextElement();
        const string text = "ab{=bcd";

        for (var i = 0; i <= text.Length; i++)
        {
            var caretX = element.Measure(text[..i], colorCodesEnabled: false);

            Assert.Equal(i, element.IndexAtX(text, caretX, colorCodesEnabled: false));
        }
    }

    [Theory]
    [InlineData("", 10, 0)]
    [InlineData("abc", 0, 0)]
    [InlineData("abc", -5, 0)]
    [InlineData("abc", 10_000, 3)]
    public void IndexAtX_ClampsToTheText(string text, int x, int expected)
    {
        FontEngine.Initialize(0);

        Assert.Equal(expected, new TextElement().IndexAtX(text, x, colorCodesEnabled: false));
    }
}
