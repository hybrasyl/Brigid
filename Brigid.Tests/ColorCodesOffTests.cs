using Brigid.Controls.Components;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Xunit;

namespace Brigid.Tests;

// With color codes off, a {=x} code is text and has width. A revert of any one of these paths to measure it as a code
// left the suite green: the fast path of FindLineBreak, the clip measure of UIElement.DrawTextClipped, and the wrap in
// TextElement.Update (BRIG-181).
[Collection("FontEngine")]
public class ColorCodesOffTests
{
    private const string Codes = "{=b{=b{=b{=b";
    private const string Words = "one two three four five six seven eight nine ten";

    [Fact]
    public void FindLineBreak_WithCodesOffBreaksTextThatFitsOnlyWithoutItsCodes()
    {
        FontEngine.Initialize(0);
        const string text = Codes + "abcdef";
        var widthWithoutCodes = TextRenderer.MeasureWidth(text, colorCodesEnabled: true);
        Assert.True(TextRenderer.MeasureWidth(text, colorCodesEnabled: false) > widthWithoutCodes);

        var breakAt = TextRenderer.FindLineBreak(text, widthWithoutCodes, colorCodesEnabled: false);

        Assert.True(breakAt < text.Length);
    }

    [Fact]
    public void FitTextToClip_WithCodesOffMeasuresTheCodesAsText()
    {
        FontEngine.Initialize(0);
        const string text = Codes + "abcdef";
        var clip = new Rectangle(0, 0, TextRenderer.MeasureWidth(text, colorCodesEnabled: true), 40);

        Assert.Equal(UIElement.TextClip.Inside, UIElement.FitTextToClip(clip, Vector2.Zero, text, true, null, 0f));
        Assert.Equal(UIElement.TextClip.Partial, UIElement.FitTextToClip(clip, Vector2.Zero, text, false, null, 0f));
    }

    [Fact]
    public void Update_WithCodesOffWrapsTheCodeAsText()
    {
        FontEngine.Initialize(0);
        const string text = "{=b" + Words;
        var element = new TextElement
        {
            WrapWidth = 60,
            ColorCodesEnabled = false
        };

        element.Update(text, Color.White);

        Assert.NotNull(element.WrappedLines);
        Assert.True(element.WrappedLines.Count > 2, "the text must wrap onto three or more lines");
        Assert.All(element.WrappedLines.Skip(1), line => Assert.DoesNotContain("{=", line));
        Assert.Equal(text, string.Join(' ', element.WrappedLines));
    }
}
