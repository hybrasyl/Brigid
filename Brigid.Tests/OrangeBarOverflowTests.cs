using Brigid.Collections;
using Brigid.Controls.World.Hud;
using Brigid.Controls.World.Hud.Panel;
using Brigid.Rendering;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     A message that is wider than the orange bar (the BRIG-177 save notice, for example) squeezed its letters together
///     to fit. Now the bar shows the part that fits at the normal spacing, and the message history (Shift+F) wraps the
///     whole message onto more lines.
/// </summary>
[Collection("FontEngine")]
public sealed class OrangeBarOverflowTests
{
    private const int WIDTH = 120;
    private const string LONG = "Brigid did not save your macros: Macro.cfg could not be read. Close the program that holds it, then log in again.";

    [Fact]
    public void AnOrangeBarLineDoesNotSqueezeItsText()
        => Assert.False(OrangeBarControl.CreateLine(0, 0, WIDTH).ShrinkToFit);

    [Fact]
    public void TheHistoryWrapsALongMessageWholeInItsColor()
    {
        FontEngine.Initialize(0);
        Assert.True(TextRenderer.MeasureWidth(LONG) > WIDTH, "the message must be wider than a line");

        var lines = Wrap([new Chat.OrangeBarMessage(LONG, Color.Orange)]);

        Assert.True(lines.Count > 1);
        Assert.All(lines, line => Assert.Equal(Color.Orange, line.Color));
        Assert.All(lines, line => Assert.True(TextRenderer.MeasureWidth(line.Text) <= WIDTH, $"\"{line.Text}\" is wider than a line"));
        Assert.Equal(LONG, string.Join(' ', lines.Select(line => line.Text)));
    }

    [Fact]
    public void TheHistoryKeepsTheOrderAndAnEmptyMessage()
    {
        FontEngine.Initialize(0);
        Chat.OrangeBarMessage[] history =
        [
            new("first", Color.Orange),
            new(string.Empty, Color.White),
            new("last", Color.Cyan)
        ];

        var lines = Wrap(history);

        Assert.Equal(history, lines);
    }

    //a new message wraps only itself, and the full history buffer drops its oldest message: the lines must be those of
    //a wrap of the whole history
    [Fact]
    public void AnAppendGivesTheLinesOfAFullWrap()
    {
        FontEngine.Initialize(0);
        var history = new CircularBuffer<Chat.OrangeBarMessage>(4);
        var wrapped = new WrappedHistory();
        wrapped.Rebuild(history, WIDTH);

        string[] texts = ["one", LONG, string.Empty, "two words", LONG + " again", "three", "a last message that wraps over the width"];

        for (var i = 0; i < texts.Length; i++)
        {
            history.Add(new Chat.OrangeBarMessage(texts[i], i % 2 == 0 ? Color.Orange : Color.Cyan));

            //two messages at once on every third message, as when the panel was hidden for a while
            if ((i % 3 == 1) && (i + 1 < texts.Length))
            {
                history.Add(new Chat.OrangeBarMessage(texts[++i], Color.White));
                wrapped.Append(history, 2, WIDTH);
            } else
                wrapped.Append(history, 1, WIDTH);

            Assert.Equal(Wrap(history), wrapped.Lines);
        }
    }

    [Fact]
    public void MoreNewMessagesThanTheHistoryHoldsWrapsItAll()
    {
        FontEngine.Initialize(0);
        var history = new CircularBuffer<Chat.OrangeBarMessage>(2);
        var wrapped = new WrappedHistory();
        wrapped.Rebuild(history, WIDTH);

        foreach (var text in new[] { "a", "b", LONG })
            history.Add(new Chat.OrangeBarMessage(text, Color.Orange));

        wrapped.Append(history, 3, WIDTH);

        Assert.Equal(Wrap(history), wrapped.Lines);
    }

    private static List<Chat.OrangeBarMessage> Wrap(IReadOnlyList<Chat.OrangeBarMessage> history)
    {
        var wrapped = new WrappedHistory();
        wrapped.Rebuild(history, WIDTH);

        return wrapped.Lines.ToList();
    }
}
