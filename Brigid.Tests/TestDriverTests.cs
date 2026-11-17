#region
using System.Text;
using Brigid.Controls.Components;
using Brigid.TestDriver;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The pure parts of the BRIG-184 test driver: the locator finds a control by its path of names, the key map gives
///     SDL scancodes, text splits into SDL text events at character boundaries, and a virtual point maps to a window pixel
///     that reads back as the same point.
/// </summary>
public sealed class TestDriverTests
{
    private static UIPanel Tree()
    {
        var root = new UIPanel { Name = "root" };
        var menu = new UIPanel { Name = "mainmenu", X = 10, Y = 20 };
        var buttons = new UIPanel { Name = "buttons", X = 5, Y = 5 };
        buttons.AddChild(new UILabel { Name = "continue", X = 1, Y = 2, Width = 30, Height = 12 });
        menu.AddChild(buttons);
        root.AddChild(menu);

        return root;
    }

    [Fact]
    public void FindWalksAPathOfNamesWithNoLetterCase()
    {
        var found = UiLocator.Find(Tree(), "MainMenu/buttons/CONTINUE");

        Assert.NotNull(found);
        Assert.Equal("mainmenu/buttons/continue", UiLocator.PathOf(found));
        Assert.Equal(16, found.ScreenX);
        Assert.Equal(27, found.ScreenY);
    }

    [Fact]
    public void FindWithOneNameSearchesEveryDepth() => Assert.Equal("continue", UiLocator.Find(Tree(), "continue")?.Name);

    [Theory]
    [InlineData("mainmenu/continue")]
    [InlineData("mainmenu/buttons/continue/more")]
    [InlineData("nothing")]
    [InlineData("")]
    public void FindReturnsNullForAPathThatDoesNotExist(string path) => Assert.Null(UiLocator.Find(Tree(), path));

    [Fact]
    public void AControlIsShownOnlyWhenEachParentIsVisible()
    {
        var root = Tree();
        var label = UiLocator.Find(root, "continue")!;

        Assert.True(UiLocator.IsShown(label));

        root.FindChild<UIPanel>("buttons")!.Visible = false;

        Assert.False(UiLocator.IsShown(label));
    }

    [Theory]
    [InlineData(Keys.A, 4)]
    [InlineData(Keys.Enter, 40)]
    [InlineData(Keys.Escape, 41)]
    [InlineData(Keys.F11, 68)]
    [InlineData(Keys.LeftShift, 225)]
    public void TheKeyMapGivesTheSdlScancode(Keys key, int scancode)
    {
        Assert.True(SdlInput.TryGetScancode(key, out var found));
        Assert.Equal(scancode, found);
    }

    [Fact]
    public void TextSplitsIntoPiecesOfAtMost31BytesAndNeverInsideACharacter()
    {
        //30 ASCII bytes, then a three-byte character that the 31-byte limit would cut
        var bytes = Encoding.UTF8.GetBytes(new string('a', 30) + "€b");
        var chunks = SdlInput.TextChunks(bytes).ToList();

        Assert.Equal([(0, 30), (30, 4)], chunks);
        Assert.Equal(new string('a', 30) + "€b", string.Concat(chunks.Select(chunk => Encoding.UTF8.GetString(bytes, chunk.Start, chunk.Length))));
    }

    [Fact]
    public void ShortTextIsOnePiece() => Assert.Equal([(0, 5)], SdlInput.TextChunks(Encoding.UTF8.GetBytes("hello")));

    [Theory]
    [InlineData(1f, 1f)]
    [InlineData(2f, 2f)]
    [InlineData(1.5f, 1.5f)]
    [InlineData(2.37f, 1.81f)]
    public void AWindowPointReadsBackAsTheSameVirtualPoint(float scaleX, float scaleY)
    {
        foreach (var (x, y) in new[] { (0, 0), (1, 1), (319, 239), (639, 479) })
        {
            var (windowX, windowY) = InputBuffer.ToWindowPoint(x, y, scaleX, scaleY);

            Assert.Equal(x, (int)(windowX / scaleX));
            Assert.Equal(y, (int)(windowY / scaleY));
        }
    }
}
