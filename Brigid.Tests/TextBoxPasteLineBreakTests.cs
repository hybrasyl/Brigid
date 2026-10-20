using Brigid.Controls.Components;
using Xunit;

namespace Brigid.Tests;

// Enter inserts one \n in a multi-line box. A Windows clipboard gives \r\n, and an old Mac text gives a lone \r. Before
// the fix a paste kept each \r, which counted toward MaxLength, could draw as a glyph and moved the caret.
public class TextBoxPasteLineBreakTests
{
    [Theory]
    [InlineData("one\r\ntwo", "one\ntwo")]
    [InlineData("one\rtwo", "one\ntwo")]
    [InlineData("one\ntwo", "one\ntwo")]
    [InlineData("one\r\n\r\ntwo\r", "one\n\ntwo\n")]
    [InlineData("one\r\r\ntwo", "one\n\ntwo")]
    public void NormalizeLineBreaks_GivesOneNewlinePerBreakInAMultiLineBox(string pasted, string expected)
        => Assert.Equal(expected, UITextBox.NormalizeLineBreaks(pasted, isMultiLine: true));

    [Theory]
    [InlineData("one\r\ntwo", "onetwo")]
    [InlineData("one\rtwo\n", "onetwo")]
    [InlineData("\r\n", "")]
    public void NormalizeLineBreaks_RemovesEveryBreakInASingleLineBox(string pasted, string expected)
        => Assert.Equal(expected, UITextBox.NormalizeLineBreaks(pasted, isMultiLine: false));
}
