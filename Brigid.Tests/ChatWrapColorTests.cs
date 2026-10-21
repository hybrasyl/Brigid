using Brigid.Rendering;
using Xunit;

namespace Brigid.Tests;

// The chat panel wraps each message with WrapLines. WrapLines dropped an inline {=x} color code at a wrap, so the rows
// after the first used the message's base color. It now starts each later row with the last code before it, as WrapText
// and the chat bubble do.
[Collection("FontEngine")]
public class ChatWrapColorTests
{
    private const int WIDTH = 60;
    private const string Words = "one two three four five six seven eight nine ten";

    private static List<string> Wrap(string text)
    {
        FontEngine.Initialize(0);

        var lines = TextRenderer.WrapLines(text, WIDTH);
        Assert.True(lines.Count > 2, "the text must wrap onto three or more lines");

        return lines;
    }

    private static string WithoutCodes(IEnumerable<string> lines)
        => string.Join(' ', lines.Select(line => line.Replace("{=b", "").Replace("{=c", "")));

    [Fact]
    public void WrapLines_StartsEachRowWithTheColorCodeOfTheLineBefore()
    {
        var lines = Wrap("{=b" + Words);

        Assert.All(lines, line => Assert.StartsWith("{=b", line));
        Assert.Equal(Words, WithoutCodes(lines));
    }

    [Fact]
    public void WrapLines_ALaterCodeReplacesTheCarriedCode()
    {
        var lines = Wrap("{=bone two three {=c" + Words);

        Assert.StartsWith("{=c", lines[^1]);
    }

    [Fact]
    public void WrapLines_CarriesTheCodeOntoTheNextParagraph()
    {
        FontEngine.Initialize(0);

        var lines = TextRenderer.WrapLines("{=bone\ntwo", WIDTH);

        Assert.Equal(["{=bone", "{=btwo"], lines);
    }

    [Fact]
    public void WrapLines_AddsNoCodeToTextWithoutOne()
    {
        var lines = Wrap(Words);

        Assert.DoesNotContain(lines, line => line.Contains("{="));
        Assert.Equal(Words, WithoutCodes(lines));
    }
}
