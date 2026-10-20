#region
using Brigid.Controls.Components;
using Brigid.Controls.LobbyLogin;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-107 server greeting needs an answer: Enter or OK accepts it, Cancel declines it, and Escape does nothing.
/// </summary>
[Collection("FontEngine")]
public class ServerGreetingTests
{
    public ServerGreetingTests() => FontEngine.Initialize(0);

    [Fact]
    public void TheGreetingIsModal() => Assert.True(new ServerGreetingControl().IsModal);

    [Fact]
    public void EveryChildHasAUniqueName() => UiAssert.NamedTree(new ServerGreetingControl());

    [Fact]
    public void EnterAcceptsTheGreeting()
    {
        var greeting = new ServerGreetingControl();
        var accepted = 0;
        greeting.Accepted += () => accepted++;
        greeting.Show("Welcome");

        greeting.OnKeyDown(new KeyDownEvent { Key = Keys.Enter });

        Assert.Equal(1, accepted);
        Assert.False(greeting.Visible);
    }

    [Fact]
    public void EscapeDoesNotCloseTheGreeting()
    {
        var greeting = new ServerGreetingControl();
        var declined = 0;
        greeting.Declined += () => declined++;
        greeting.Show("Welcome");

        var e = new KeyDownEvent { Key = Keys.Escape };
        greeting.OnKeyDown(e);

        Assert.True(e.Handled);
        Assert.True(greeting.Visible);
        Assert.Equal(0, declined);
    }

    [Fact]
    public void CancelDeclinesTheGreeting()
    {
        var greeting = new ServerGreetingControl();
        var declined = 0;
        greeting.Declined += () => declined++;
        greeting.Show("Welcome");

        greeting.CancelButton.OnClick(new ClickEvent { Button = MouseButton.Left });

        Assert.Equal(1, declined);
        Assert.False(greeting.Visible);
    }

    [Fact]
    public void TheGreetingShowsTheNoticeText()
    {
        var greeting = new ServerGreetingControl();

        greeting.Show("Welcome to the server");

        Assert.Equal("Welcome to the server", greeting.FindChild<UILabel>("text")!.Text);
    }

    [Fact]
    public void AltEnterDoesNotAcceptTheGreeting()
    {
        var greeting = new ServerGreetingControl();
        var accepted = 0;
        greeting.Accepted += () => accepted++;
        greeting.Show("Welcome");

        greeting.OnKeyDown(new KeyDownEvent { Key = Keys.Enter, Modifiers = KeyModifiers.Alt });

        Assert.Equal(0, accepted);
        Assert.True(greeting.Visible);
    }

    [Fact]
    public void TheGreetingIsThemedAndItsPartsAreNamed()
    {
        var greeting = new ServerGreetingControl();

        Assert.True(greeting.IsThemed);
        Assert.Equal("ok", greeting.OkButton.Name);
        Assert.Equal("cancel", greeting.CancelButton.Name);
        Assert.NotNull(greeting.FindChild<UILabel>("title"));
    }

    [Fact]
    public void TheScrollRangeFollowsAResize()
    {
        var engine = FontEngine.Instance;
        var greeting = new ServerGreetingControl();
        var scroll = greeting.FindChild<Controls.Scrolling.ScrollView>("body")!;
        greeting.Show(string.Concat(Enumerable.Repeat("a long line of notice text ", 200)));

        var before = scroll.Model.Max;

        try
        {
            engine.SetLayoutScale(0.5f, 0.5f);
            greeting.Update(new Microsoft.Xna.Framework.GameTime());

            Assert.NotEqual(before, scroll.Model.Max);
        } finally
        {
            engine.SetLayoutScale(1f, 1f);
        }
    }
}
