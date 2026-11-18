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

        var lines = SystemMessagePanel.WrapHistory([new Chat.OrangeBarMessage(LONG, Color.Orange)], WIDTH);

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

        var lines = SystemMessagePanel.WrapHistory(history, WIDTH);

        Assert.Equal(history, lines);
    }
}
