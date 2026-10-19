#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The primitive message popup (BRIG-121): every child has a lowercase name, OK and Cancel sit on the bottom bar, the
///     keys answer the popup as before the rebuild, the popup is modal, and the height fits the message.
/// </summary>
[Collection("FontEngine")]
public class OkPopupMessageControlTests
{
    private const string LONG_MESSAGE =
        "This message is long enough to wrap onto a number of lines, so that the popup must grow taller than it is for a "
        + "short message. It goes on for some time, and then it goes on again, and then it stops.";

    public OkPopupMessageControlTests() => FontEngine.Initialize(0);

    [Fact]
    public void EveryChildHasAUniqueName() => UiAssert.NamedTree(new OkPopupMessageControl("Notice", showCancel: true));

    [Fact]
    public void OkAndCancelSitOnTheBottomBar()
    {
        var popup = new OkPopupMessageControl("Notice", showCancel: true);
        var cancel = popup.CancelButton!;

        Assert.False(popup.FindChild<TextButton>("close")!.Visible);
        Assert.True(popup.OkButton.Y >= popup.BottomBarTop);
        Assert.Equal(popup.OkButton.Y, cancel.Y);
        Assert.True(popup.OkButton.X + popup.OkButton.Width <= cancel.X);
        Assert.Null(new OkPopupMessageControl("Notice").CancelButton);
    }

    [Theory]
    [InlineData(Keys.Enter, true, 1, 0)]
    [InlineData(Keys.Space, true, 1, 0)]
    [InlineData(Keys.Escape, true, 0, 1)]
    [InlineData(Keys.Escape, false, 1, 0)]
    [InlineData(Keys.A, true, 0, 0)]
    public void TheKeysAnswerThePopup(Keys key, bool showCancel, int oks, int cancels)
    {
        var popup = new OkPopupMessageControl("Notice", showCancel);
        var ok = 0;
        var cancel = 0;
        popup.OnOk += () => ok++;
        popup.OnCancel += () => cancel++;
        popup.Show("Message");

        var e = new KeyDownEvent { Key = key };
        popup.OnKeyDown(e);

        Assert.True(e.Handled);
        Assert.Equal(oks, ok);
        Assert.Equal(cancels, cancel);
    }

    [Fact]
    public void AltEnterDoesNotAnswerThePopup()
    {
        var popup = new OkPopupMessageControl("Notice");
        var ok = 0;
        popup.OnOk += () => ok++;
        popup.Show("Message");

        popup.OnKeyDown(new KeyDownEvent { Key = Keys.Enter, Modifiers = KeyModifiers.Alt });

        Assert.Equal(0, ok);
    }

    [Fact]
    public void TheHeightFitsTheMessage()
    {
        var popup = new OkPopupMessageControl("Notice");
        var message = popup.FindChild<UILabel>("message")!;

        popup.Show("Short.");
        var shortHeight = popup.Height;

        //a second Show while the popup is open changes the message and fits it again
        popup.Show(LONG_MESSAGE);

        Assert.True(popup.Visible);
        Assert.True(popup.Height > shortHeight);
        Assert.True(message.ContentHeight > 2 * TextRenderer.CHAR_HEIGHT);
        Assert.True(message.Y >= popup.ContentBounds.Y);
        Assert.True(message.Y + message.ContentHeight <= popup.ContentBounds.Bottom);
        Assert.True(popup.OkButton.Y >= popup.BottomBarTop);
        Assert.True(popup.OkButton.Y + popup.OkButton.Height <= popup.Height);
    }

    [Fact]
    public void TheMessageAreaIsTwoToTwentyLinesTall()
    {
        var popup = new OkPopupMessageControl("Notice");

        popup.Show("One line.");
        var oneLine = popup.Height;
        popup.Show("Two lines.\nTwo lines.");

        Assert.Equal(oneLine, popup.Height);

        popup.Show(string.Join("\n", Enumerable.Repeat("A line.", 30)));
        var message = popup.FindChild<UILabel>("message")!;

        Assert.Equal(20 * TextRenderer.CHAR_HEIGHT, message.Height);
        Assert.True(popup.Height < 480);
    }

    [Fact]
    public void ThePopupIsModal() => Assert.True(new OkPopupMessageControl("Notice").IsModal);

    [Fact]
    public void EscapeWithCancelLeavesTheHidingToTheHost()
    {
        var popup = new OkPopupMessageControl("Notice", showCancel: true);
        var closes = 0;
        popup.OnClose += () => closes++;
        popup.Show("Message");

        popup.OnKeyDown(new KeyDownEvent { Key = Keys.Escape });

        Assert.True(popup.Visible);
        Assert.Equal(0, closes);
    }

    [Fact]
    public void HideClearsTheMessage()
    {
        var popup = new OkPopupMessageControl("Notice");
        popup.Show("Message");

        popup.Hide();

        Assert.False(popup.Visible);
        Assert.Equal(string.Empty, popup.FindChild<UILabel>("message")!.Text);
    }
}
