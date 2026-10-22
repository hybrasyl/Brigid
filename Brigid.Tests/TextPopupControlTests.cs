#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-122 text popup: every child has a lowercase name, the framed look has the title band, the scroll bar and
///     Close, the wooden sign has none of them and the retail size, and Escape, Space and Enter close the popup.
/// </summary>
[Collection("FontEngine")]
public class TextPopupControlTests
{
    public TextPopupControlTests() => FontEngine.Initialize(0);

    [Fact]
    public void EveryChildHasAUniqueName() => UiAssert.NamedTree(new TextPopupControl());

    [Fact]
    public void TheWoodenSignHasNoFrameChrome()
    {
        var popup = new TextPopupControl();
        var title = popup.FindChild<UILabel>("title")!;
        var close = popup.FindChild<TextButton>("close")!;
        var scroll = popup.FindChild<ScrollView>("text")!;

        popup.Show("Framed text.");
        var framedWidth = popup.Width;

        Assert.True(title.Visible);
        Assert.True(close.Visible);
        Assert.True(scroll.Bar.Visible);

        //a second Show while open changes the style and keeps the popup open
        popup.Show("A sign.", PopupStyle.Wooden);

        Assert.True(popup.Visible);
        Assert.False(title.Visible);
        Assert.False(close.Visible);
        Assert.False(scroll.Bar.Visible);
        Assert.Equal(360, popup.Width);
        Assert.Equal(180, popup.Height);

        popup.Show("Framed again.", PopupStyle.NonScroll);

        Assert.True(title.Visible);
        Assert.True(close.Visible);
        Assert.Equal(framedWidth, popup.Width);
    }

    //BRIG-138: with no frame.sign part, the sign is the flat frame with the sign's text margins, and its text takes the
    //text role as every other style does
    [Fact]
    public void WithNoThemeTheSignKeepsItsTextMarginsAndTheTextRole()
    {
        var popup = new TextPopupControl();
        var scroll = popup.FindChild<ScrollView>("text")!;
        var message = popup.FindChild<UILabel>("message")!;

        popup.Show("A sign.", PopupStyle.Wooden);
        message.ApplyForegroundRole();

        Assert.Equal(16, scroll.X);
        Assert.Equal(12, scroll.Y);
        Assert.Equal(360 - 2 * 16, scroll.Width);
        Assert.Equal(180 - 12 - 32, scroll.Height);
        Assert.Equal(DialogPalette.ForRole(ThemeRoles.Text), message.DrawnForegroundColor);

        popup.Hide();
    }

    [Theory]
    [InlineData(Keys.Escape, false)]
    [InlineData(Keys.Space, false)]
    [InlineData(Keys.Enter, false)]
    [InlineData(Keys.A, true)]
    public void TheKeysCloseThePopup(Keys key, bool staysOpen)
    {
        var popup = new TextPopupControl();
        var closes = 0;
        popup.OnClose += () => closes++;
        popup.Show("Text.");

        var e = new KeyDownEvent { Key = key };
        popup.OnKeyDown(e);

        Assert.True(e.Handled);
        Assert.Equal(staysOpen, popup.Visible);
        Assert.Equal(staysOpen ? 0 : 1, closes);
    }

    [Fact]
    public void AltEnterDoesNotCloseThePopup()
    {
        var popup = new TextPopupControl();
        popup.Show("Text.");

        popup.OnKeyDown(new KeyDownEvent { Key = Keys.Enter, Modifiers = KeyModifiers.Alt });

        Assert.True(popup.Visible);
    }
}
