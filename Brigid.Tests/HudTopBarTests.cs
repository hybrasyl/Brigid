#region
using System.Globalization;
using System.Reflection;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Hud;
using Brigid.Controls.World.Hud.Main;
using Brigid.Definitions;
using Brigid.Rendering;
using Brigid.Systems;
using Brigid.ViewModel;
using DALib.Networking.Packets.Server;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-144 phase 2: the top bar of the new HUD (BRIG-127 D8, D15, D17, D39). Each box shows its value, the mail and
///     parcel icons show only while their flag is set, the server and mail boxes raise their clicks, and the clock changes
///     to the game time only when a source gives one.
/// </summary>
[Collection("FontEngine")]
public class HudTopBarTests : IDisposable
{
    public HudTopBarTests() => FontEngine.Initialize(0);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static readonly DateTimeOffset Evening = new(2026, 11, 17, 21, 14, 0, TimeSpan.Zero);

    private readonly List<MainHudControl> Huds = [];

    public void Dispose()
    {
        foreach (var hud in Huds)
            hud.Dispose();
    }

    private (MainHudControl Hud, HudTopBar Bar, FixedClock Clock) NewHud(PlayerAttributes? attributes = null)
    {
        var clock = new FixedClock(Evening);
        var hud = new MainHudControl(DispatchProxy.Create<IWorldHud, MainHudControlTests.RecordingHud>(), clock, attributes ?? new PlayerAttributes());
        Huds.Add(hud);

        return (hud, hud.FindChild<HudTopBar>("top_bar")!, clock);
    }

    private static string TextOf(UIPanel panel, string box, string label)
        => panel.FindChild<HudBox>(box)!.FindChild<UILabel>(label)!.Text;

    private static RoleIcon IconOf(UIPanel panel, string box, string icon) => panel.FindChild<HudBox>(box)!.FindChild<RoleIcon>(icon)!;

    private static void Click(UIElement element) => element.OnClick(new ClickEvent { Button = MouseButton.Left });

    [Fact]
    public void EveryChildHasAUniqueName()
    {
        var (_, bar, _) = NewHud();

        UiAssert.NamedTree(bar);
    }

    //with no theme, each icon draws Brigid's own art, so the bar reads no dat file and shows no empty icon
    [Fact]
    public void EveryTopBarIconHasBuiltInArt() => Assert.All(ThemeRoles.HudIcons, role => Assert.True(BuiltInArt.Has(role), role));

    [Fact]
    public void TheBoxesHaveTheMockupPlaces()
    {
        Assert.Equal(new Rectangle(4, 4, 100, 17), MainHudLayout.ServerBox);
        Assert.Equal(new Rectangle(107, 4, 62, 17), MainHudLayout.GoldBox);
        Assert.Equal(new Rectangle(172, 4, 294, 17), MainHudLayout.ZoneBox);
        Assert.Equal(new Rectangle(469, 4, 34, 17), MainHudLayout.MailBox);
        Assert.Equal(new Rectangle(506, 4, 62, 17), MainHudLayout.WeightBox);
        Assert.Equal(new Rectangle(571, 4, 65, 17), MainHudLayout.ClockBox);
    }

    [Fact]
    public void TheBoxesAreInTheTopBarInOrder()
    {
        var (_, bar, _) = NewHud();
        var boxes = bar.Children.OfType<HudBox>().ToList();

        Assert.Equal(["server_box", "gold_box", "zone_box", "mail_box", "weight_box", "clock_box"], boxes.Select(box => box.Name));
        Assert.All(boxes, box => Assert.True(MainHudLayout.TopBar.Contains(box.Bounds), box.Name));

        for (var i = 1; i < boxes.Count; i++)
            Assert.True(boxes[i - 1].Bounds.Right < boxes[i].Bounds.Left, boxes[i].Name);
    }

    [Fact]
    public void TheSettersShowTheirValues()
    {
        var (hud, bar, _) = NewHud();

        hud.SetServerName("Hybrasyl");
        hud.SetZoneName("Mileth Village");
        hud.SetCoords(54, 37);
        hud.SetWeight(42, 96);
        bar.SetGold(12480);

        Assert.Equal("Hybrasyl", TextOf(bar, "server_box", "server_name"));
        Assert.Equal("Mileth Village (54, 37)", TextOf(bar, "zone_box", "zone"));
        Assert.Equal("42/96", TextOf(bar, "weight_box", "weight"));
        Assert.Equal("12,480", TextOf(bar, "gold_box", "gold"));
    }

    [Fact]
    public void TheSecureIconShowsOnlyOverTls()
    {
        var (hud, bar, _) = NewHud();

        Assert.False(IconOf(bar, "server_box", "secure").Visible);

        hud.SetTransportSecure(true);
        Assert.True(IconOf(bar, "server_box", "secure").Visible);
        Assert.Equal(ThemeRoles.IconHudSecure, IconOf(bar, "server_box", "secure").Role);

        hud.SetTransportSecure(false);
        Assert.False(IconOf(bar, "server_box", "secure").Visible);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TheMailAndParcelIconsShowWhileTheirFlagIsSet(bool mail, bool parcel)
    {
        var (_, bar, _) = NewHud();

        bar.SetMail(mail, parcel);

        Assert.Equal(mail, IconOf(bar, "mail_box", "mail").Visible);
        Assert.Equal(parcel, IconOf(bar, "mail_box", "parcel").Visible);
    }

    [Theory]
    [InlineData(null, ThemeRoles.IconHudLatencyNone)]
    [InlineData(40L, ThemeRoles.IconHudLatencyGood)]
    [InlineData(400L, ThemeRoles.IconHudLatencyFair)]
    [InlineData(4000L, ThemeRoles.IconHudLatencyPoor)]
    public void TheLatencyIconShowsTheBucket(long? ms, string role)
    {
        var (_, bar, _) = NewHud();

        bar.ShowLatency(ms);

        Assert.Equal(role, IconOf(bar, "server_box", "latency").Role);
    }

    //the decode of the packet, through the attributes, to the icons and the boxes
    [Fact]
    public void AnAttributesPacketReachesTheTopBar()
    {
        var (hud, bar, _) = NewHud();
        var attrs = new PlayerAttributes();

        attrs.Update(
            new AttributesPacket
            {
                UnreadMail = true,
                Primary = new PrimaryAttributes { CurrentWeight = 42, MaxWeight = 96 },
                Experience = new ExperienceAttributes { Gold = 1_250_000 },
                Secondary = new SecondaryAttributes { MailStatus = 0x01 }
            });
        hud.ShowAttributes(attrs);

        Assert.False(IconOf(bar, "mail_box", "mail").Visible);
        Assert.True(IconOf(bar, "mail_box", "parcel").Visible);
        Assert.Equal("42/96", TextOf(bar, "weight_box", "weight"));
        Assert.Equal("1.2M", TextOf(bar, "gold_box", "gold"));
    }

    //the monitor can change on another thread; the bar reads it at its update
    [Fact]
    public void TheBarReadsTheLatencyAtItsUpdate()
    {
        var (_, bar, _) = NewHud();

        try
        {
            LatencyMonitor.Update(40);
            bar.Update(new GameTime());

            Assert.Equal(ThemeRoles.IconHudLatencyGood, IconOf(bar, "server_box", "latency").Role);
        } finally
        {
            LatencyMonitor.Update(null);
        }
    }

    [Fact]
    public void ARightClickOnABoxDoesNothing()
    {
        var (hud, bar, _) = NewHud();
        var server = 0;
        hud.ServerBoxClicked += () => server++;

        bar.FindChild<HudBox>("server_box")!.OnClick(new ClickEvent { Button = MouseButton.Right });

        Assert.Equal(0, server);
    }

    [Fact]
    public void TheServerAndMailBoxesRaiseTheirClicks()
    {
        var (hud, bar, _) = NewHud();
        var server = 0;
        var mail = 0;
        hud.ServerBoxClicked += () => server++;
        hud.MailClicked += () => mail++;

        Click(bar.FindChild<HudBox>("server_box")!);
        Click(bar.FindChild<HudBox>("mail_box")!);
        Click(bar.FindChild<HudBox>("gold_box")!);

        Assert.Equal(1, server);
        Assert.Equal(1, mail);
    }

    [Fact]
    public void TheClockShowsTheLocalTimeAndChangesEachMinute()
    {
        var (_, bar, clock) = NewHud();
        string Local() => HudFormat.Clock(clock.GetLocalNow().DateTime, CultureInfo.CurrentCulture);

        Assert.Equal(Local(), TextOf(bar, "clock_box", "clock"));

        var before = Local();
        clock.Now = Evening.AddMinutes(1);
        bar.Update(new GameTime());

        Assert.NotEqual(before, Local());
        Assert.Equal(Local(), TextOf(bar, "clock_box", "clock"));
    }

    [Fact]
    public void AClickOnTheClockDoesNothingWithNoGameTime()
    {
        var (_, bar, _) = NewHud();
        var before = TextOf(bar, "clock_box", "clock");

        Click(bar.FindChild<HudBox>("clock_box")!);

        Assert.Equal(before, TextOf(bar, "clock_box", "clock"));
    }

    [Fact]
    public void AClickOnTheClockChangesToTheGameTimeAndBack()
    {
        var (hud, bar, _) = NewHud();
        var local = TextOf(bar, "clock_box", "clock");
        hud.GameTimeSource = () => "Deoch 3";

        Click(bar.FindChild<HudBox>("clock_box")!);
        Assert.Equal("Deoch 3", TextOf(bar, "clock_box", "clock"));

        Click(bar.FindChild<HudBox>("clock_box")!);
        Assert.Equal(local, TextOf(bar, "clock_box", "clock"));
    }

    [Fact]
    public void AHoverShowsTheTooltipUnderTheBox()
    {
        var (hud, bar, _) = NewHud();
        var tooltip = hud.FindChild<HudTooltip>("hud_tooltip")!;
        //the clock box is at the right edge, so its tooltip moves left to stay on the screen
        var clock = bar.FindChild<HudBox>("clock_box")!;

        clock.OnMouseEnter();

        Assert.True(tooltip.Visible);
        Assert.True(tooltip.Y >= MainHudLayout.TopBar.Bottom);
        Assert.True(clock.ScreenX + tooltip.Width > ChaosGame.VIRTUAL_WIDTH);
        Assert.True(tooltip.X + tooltip.Width <= ChaosGame.VIRTUAL_WIDTH);

        clock.OnMouseLeave();

        Assert.False(tooltip.Visible);
    }

    //the lines are built again while the tooltip shows, so a value that changes under the mouse stays current
    [Fact]
    public void TheTooltipFollowsAChangeUnderTheMouse()
    {
        var (hud, bar, _) = NewHud();
        var tooltip = hud.FindChild<HudTooltip>("hud_tooltip")!;

        bar.FindChild<HudBox>("weight_box")!.OnMouseEnter();
        hud.SetWeight(50, 96);
        tooltip.Update(new GameTime());

        Assert.Contains("Weight 50 of 96", tooltip.WrappedText);
    }

    [Fact]
    public void AHiddenHudHidesItsTooltip()
    {
        var (hud, bar, _) = NewHud();
        var tooltip = hud.FindChild<HudTooltip>("hud_tooltip")!;

        bar.FindChild<HudBox>("weight_box")!.OnMouseEnter();
        hud.Visible = false;
        hud.Visible = true;

        Assert.False(tooltip.Visible);
    }

    //the world screen makes its HUDs after the first attributes can arrive
    [Fact]
    public void TheHudShowsTheAttributesThatItFindsAndThenTheirChanges()
    {
        var attrs = new PlayerAttributes();
        attrs.Update(new AttributesPacket { Primary = new PrimaryAttributes { CurrentWeight = 42, MaxWeight = 96 } });

        var (hud, bar, _) = NewHud(attrs);

        Assert.Equal("42/96", TextOf(bar, "weight_box", "weight"));

        var gold = bar.FindChild<HudBox>("gold_box")!.FindChild<UILabel>("gold")!;
        attrs.Update(new AttributesPacket { Experience = new ExperienceAttributes { Gold = 7 } });
        Assert.Equal("7", gold.Text);

        hud.Dispose();
        attrs.Update(new AttributesPacket { Experience = new ExperienceAttributes { Gold = 9 } });
        Assert.Equal("7", gold.Text);
    }

    //the flag alone after a known state keeps it (Hybrasyl sends the flag alone while something is unread)
    [Fact]
    public void TheFlagAloneKeepsTheKnownMailState()
    {
        var attrs = new PlayerAttributes();

        attrs.Update(new AttributesPacket { UnreadMail = true, Secondary = new SecondaryAttributes { MailStatus = 0x01 } });
        attrs.Update(new AttributesPacket { UnreadMail = true });

        Assert.Equal((false, true), (attrs.HasMail, attrs.HasParcel));

        attrs.Update(new AttributesPacket());

        Assert.Equal((false, false), (attrs.HasMail, attrs.HasParcel));
    }
}
