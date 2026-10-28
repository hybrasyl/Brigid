#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Popups.Exchange;
using Brigid.Definitions;
using Brigid.Rendering;
using Chaos.DarkAges.Definitions;
using DALib.Networking.Packets.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-126 step 3b: the exchange window is a themed modal panel that shows the <c>WorldState.Exchange</c> state. Each
///     side shows its name, its items in four rows, its gold and its "Accepted" mark. Accept, Cancel, Escape and the gold
///     button raise events for the screen, and a key that the panel does not use goes on to the screen.
/// </summary>
[Collection("FontEngine")]
public sealed class ExchangeControlTests : IDisposable
{
    private static readonly Rectangle Viewport = new(0, 0, 640, 480);

    private readonly ExchangeControl Exchange;
    private readonly string PreviousPlayerName = WorldState.PlayerName;

    public ExchangeControlTests()
    {
        FontEngine.Initialize(0);
        WorldState.Exchange.Close();
        WorldState.PlayerName = "Angelique";
        Exchange = new ExchangeControl(Viewport);
    }

    public void Dispose()
    {
        WorldState.Exchange.Close();
        WorldState.PlayerName = PreviousPlayerName;
        Exchange.Dispose();
    }

    private static ClickEvent LeftClick() => new() { Button = MouseButton.Left };

    [Fact]
    public void EveryChildHasAUniqueName()
    {
        Assert.Equal("exchange", Exchange.Name);
        UiAssert.NamedTree(Exchange);

        //each side is a group, so that a theme can move a column as one
        Assert.NotNull(Exchange.FindChild<UIPanel>("my"));
        Assert.NotNull(Exchange.FindChild<UIPanel>("their"));
        Assert.NotNull(Exchange.FindChild<TextButton>("gold_button"));
    }

    [Fact]
    public void AStartShowsBothNamesAndNoGold()
    {
        WorldState.Exchange.Start(7, "Sabrael");

        Assert.True(Exchange.Visible);
        Assert.Equal(("Angelique", "Gold: 0", false), Exchange.SideText(false));
        Assert.Equal(("Sabrael", "Gold: 0", false), Exchange.SideText(true));
        Assert.True(Exchange.OkButton.Enabled);
        Assert.True(Exchange.GoldButton.Enabled);
    }

    [Fact]
    public void ItemsShowInArrivalOrder_FourAtATime()
    {
        WorldState.Exchange.Start(7, "Sabrael");

        for (byte i = 0; i < 5; i++)
            WorldState.Exchange.AddItem(true, i, 1, DisplayColor.Default, $"Apple {i}");

        var rows = Exchange.Rows(true);

        Assert.Equal(["Apple 0", "Apple 1", "Apple 2", "Apple 3"], rows.Select(row => row.ItemName));
        Assert.All(rows, row => Assert.True(row.Visible));
        Assert.All(Exchange.Rows(false), row => Assert.False(row.Visible));
    }

    [Fact]
    public void TheWheelScrollsTheList_OneRowANotch()
    {
        WorldState.Exchange.Start(7, "Sabrael");

        for (byte i = 0; i < 6; i++)
            WorldState.Exchange.AddItem(false, i, 1, DisplayColor.Default, $"Apple {i}");

        Exchange.WheelList(false, -1);

        Assert.Equal(["Apple 1", "Apple 2", "Apple 3", "Apple 4"], Exchange.Rows(false).Select(row => row.ItemName));
    }

    [Fact]
    public void ARowGrowsToItsName_SoTheEndOfALongNameCanScrollIntoView()
    {
        WorldState.Exchange.Start(7, "Sabrael");
        var row = Exchange.Rows(true)[0];
        var emptyWidth = row.Width;

        WorldState.Exchange.AddItem(true, 0, 1, DisplayColor.Default, new string('W', 60));

        Assert.True(row.EntryWidth > emptyWidth);
        Assert.Equal(row.EntryWidth, row.Width);

        WorldState.Exchange.Close();

        Assert.Equal(emptyWidth, row.Width);
    }

    [Fact]
    public void GoldShowsWithThousandsSeparators()
    {
        WorldState.Exchange.Start(7, "Sabrael");

        WorldState.Exchange.SetGold(false, 1234);
        WorldState.Exchange.SetGold(true, 5);

        Assert.Equal($"Gold: {1234:N0}", Exchange.SideText(false).Gold);
        Assert.Equal("Gold: 5", Exchange.SideText(true).Gold);
    }

    [Fact]
    public void AcceptDisablesAcceptAndGold_MarksYourSide_AndRaisesOnOk()
    {
        WorldState.Exchange.Start(7, "Sabrael");
        var oks = 0;
        Exchange.OnOk += () => oks++;

        Exchange.OkButton.OnClick(LeftClick());

        Assert.Equal(1, oks);
        Assert.False(Exchange.OkButton.Enabled);
        Assert.False(Exchange.GoldButton.Enabled);
        Assert.True(Exchange.SideText(false).Accepted);
        Assert.False(Exchange.SideText(true).Accepted);
    }

    [Fact]
    public void TheirAcceptMarksTheirSide()
    {
        WorldState.Exchange.Start(7, "Sabrael");

        WorldState.Exchange.SetAccepted(true, null);

        Assert.True(Exchange.SideText(true).Accepted);
    }

    //BRIG-140: retail confirms your own accept to you at once, and the trade stays open until the other player accepts
    [Fact]
    public void TheServerConfirmingYourSideAloneKeepsTheWindowOpen()
    {
        WorldState.Exchange.Start(7, "Sabrael");
        Exchange.OkButton.OnClick(LeftClick());

        WorldState.Exchange.SetAccepted(false, "Exchange completed.");

        Assert.True(Exchange.Visible);
        Assert.True(Exchange.SideText(false).Accepted);
        Assert.False(Exchange.SideText(true).Accepted);

        WorldState.Exchange.SetAccepted(true, "Exchange completed.");

        Assert.False(Exchange.Visible);
    }

    //the packet path: WorldState sends every Accepted event to the exchange, so a side-0 event does not close it alone
    [Fact]
    public void ASideZeroAcceptedPacketLeavesTheWindowOpen()
    {
        WorldState.Exchange.Start(7, "Sabrael");

        WorldState.ApplyExchange(new AcceptExchangeResponsePacket { RightSide = false, Message = "Exchange completed." });

        Assert.True(Exchange.Visible);
        Assert.True(Exchange.SideText(false).Accepted);

        WorldState.ApplyExchange(new AcceptExchangeResponsePacket { RightSide = true, Message = "Exchange completed." });

        Assert.False(Exchange.Visible);
    }

    [Fact]
    public void ANewExchangeStartsClean()
    {
        WorldState.Exchange.Start(7, "Sabrael");
        WorldState.Exchange.AddItem(false, 0, 1, DisplayColor.Default, "Apple");
        WorldState.Exchange.SetAccepted(true, null);
        Exchange.OkButton.OnClick(LeftClick());
        WorldState.Exchange.Close();

        Assert.False(Exchange.Visible);

        WorldState.Exchange.Start(8, "Kedian");

        Assert.Equal(("Angelique", "Gold: 0", false), Exchange.SideText(false));
        Assert.Equal(("Kedian", "Gold: 0", false), Exchange.SideText(true));
        Assert.All(Exchange.Rows(false), row => Assert.False(row.Visible));
        Assert.True(Exchange.OkButton.Enabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscapeOrCancelRaisesOnCancelOnce_AndTheWindowWaitsForTheServer(bool escapeFirst)
    {
        WorldState.Exchange.Start(7, "Sabrael");
        var cancels = 0;
        Exchange.OnCancel += () => cancels++;
        var escape = new KeyDownEvent { Key = Keys.Escape };

        if (escapeFirst)
            Exchange.OnKeyDown(escape);
        else
            Exchange.CancelButton.OnClick(LeftClick());

        //BRIG-168: a second cancel sends nothing, and the buttons wait for the server
        Exchange.CancelButton.OnClick(LeftClick());
        Exchange.OnKeyDown(new KeyDownEvent { Key = Keys.Escape });

        Assert.Equal(1, cancels);
        Assert.False(Exchange.OkButton.Enabled);
        Assert.False(Exchange.CancelButton.Enabled);
        Assert.False(Exchange.GoldButton.Enabled);

        if (escapeFirst)
            Assert.True(escape.Handled);

        //the window stays until the server's Cancelled event closes the exchange
        Assert.True(Exchange.Visible);

        WorldState.ApplyExchange(new CancelExchangeResponsePacket { RightSide = false, Message = "Exchange cancelled." });

        Assert.False(Exchange.Visible);
    }

    //the partner's last accept was in transit when the player cancelled: the server completed the trade, and its
    //Accepted event closes the window
    [Fact]
    public void ATradeThatCompletesWhileACancelWaitsClosesTheWindow()
    {
        WorldState.Exchange.Start(7, "Sabrael");
        WorldState.Exchange.SetAccepted(false, null);
        Exchange.CancelButton.OnClick(LeftClick());

        Assert.True(Exchange.Visible);

        WorldState.Exchange.SetAccepted(true, "You exchanged.");

        Assert.False(Exchange.Visible);
    }

    [Fact]
    public void ANewExchangeAfterACancelEnablesTheButtons()
    {
        WorldState.Exchange.Start(7, "Sabrael");
        Exchange.CancelButton.OnClick(LeftClick());
        WorldState.Exchange.Close("Exchange cancelled.");

        WorldState.Exchange.Start(8, "Kedian");

        Assert.True(Exchange.OkButton.Enabled);
        Assert.True(Exchange.CancelButton.Enabled);
        Assert.True(Exchange.GoldButton.Enabled);
    }

    [Fact]
    public void AKeyThePanelDoesNotUseGoesOnToTheScreen()
    {
        WorldState.Exchange.Start(7, "Sabrael");

        var enter = new KeyDownEvent { Key = Keys.Enter };
        Exchange.OnKeyDown(enter);

        Assert.False(enter.Handled);
    }

    [Fact]
    public void TheGoldButtonRaisesOnGoldClicked()
    {
        WorldState.Exchange.Start(7, "Sabrael");
        var clicks = 0;
        Exchange.OnGoldClicked += () => clicks++;

        Exchange.GoldButton.OnClick(LeftClick());

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void TheCloseButtonIsHidden_AcceptAndCancelShow()
    {
        Assert.False(Exchange.FindChild<TextButton>("close")!.Visible);
        Assert.True(Exchange.OkButton.Visible);
        Assert.True(Exchange.CancelButton.Visible);
        Assert.True(Exchange.OkButton.X < Exchange.CancelButton.X);
    }
}
