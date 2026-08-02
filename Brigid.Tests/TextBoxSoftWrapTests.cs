#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     Covers the soft-wrap half of the trailing-space fix: <c>GetLineText</c> drops a soft wrap's boundary spaces
///     (the wrapper consumed them into the previous line's range, so they belong to the break) but keeps a hard line's
///     authored trailing spaces, which are text the caret has to be able to sit after.
///     <para>
///         Driving real layout headless needs two things that are easy to omit: <see cref="FontEngine.Initialize" />,
///         without which every measurement is 0 and any pixel-to-character mapping collapses to index 0; and at least
///         one <c>Update</c>, which is what runs <c>ComputeLineLayout</c>. Omit either and the box keeps a degenerate
///         single-line layout, and tests in this category <em>pass</em> while exercising nothing — so assert
///         <see cref="UITextBox.LineCount" /> before trusting any multi-line assertion.
///     </para>
///     Shares the FontEngine collection because the engine is a static singleton whose active face other tests mutate.
/// </summary>
[Collection("FontEngine")]
public class TextBoxSoftWrapTests
{
    private static UITextBox LaidOutBox(string text, int width = 90)
    {
        FontEngine.Initialize(0);

        var box = new UITextBox
        {
            Width = width,
            Height = 100,
            MaxLength = 1000,
            IsMultiLine = true,
            IsFocused = true,
            Text = text
        };

        box.Update(new GameTime());

        return box;
    }

    private static void PressEnd(UITextBox box)
        => box.OnKeyDown(
            new KeyDownEvent
            {
                Key = Keys.End,
                Modifiers = KeyModifiers.None
            });

    [Fact]
    public void RealLayoutRuns_Headless()
    {
        var box = LaidOutBox("aaaa bbbb cccc dddd eeee ffff gggg");

        //the precondition every other test here depends on: if this is 1, nothing below is exercising wrapping
        Assert.True(box.LineCount > 1, $"expected soft wrapping at width 90, got LineCount {box.LineCount}");
    }

    [Fact]
    public void End_OnASoftWrappedLine_StopsBeforeTheBoundarySpace()
    {
        var box = LaidOutBox("aaaa bbbb cccc dddd eeee ffff gggg");

        Assert.True(box.LineCount > 1, "text did not wrap; test is not exercising the soft-wrap path");

        box.CursorPosition = 0;
        PressEnd(box);

        //the wrap consumed the space that ended line 0, so End must stop short of it rather than treat it as content
        Assert.True(box.CursorPosition < box.Text.Length, "End ran past the wrapped line");
        Assert.Equal(' ', box.Text[box.CursorPosition]);
        Assert.NotEqual(' ', box.Text[box.CursorPosition - 1]);
    }

    [Fact]
    public void End_OnAHardLine_KeepsAuthoredTrailingSpaces()
    {
        //no wrapping here — a short hard line whose trailing spaces the user typed
        var box = LaidOutBox("abc  \ndef", 400);

        box.CursorPosition = 0;
        PressEnd(box);

        //those spaces are text: the caret belongs after them, at the newline
        Assert.Equal('\n', box.Text[box.CursorPosition]);
        Assert.Equal(' ', box.Text[box.CursorPosition - 1]);
    }
}
