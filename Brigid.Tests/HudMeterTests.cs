#region
using System.Reflection;
using Brigid.Controls.Components;
using Brigid.Controls.World.Hud;
using Brigid.Controls.World.Hud.Main;
using Brigid.Rendering;
using Brigid.ViewModel;
using DALib.Networking.Packets.Server;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-148 phase 1: the meters of the new HUD. A meter fills to the fraction of its value, clamped, and clips its fill
///     at that fraction, with 1 px at least for a low value and 1 px short for a high value; the flat fill is inside the
///     border; the gaps of its segments are at even fractions of its width; the health and mana meters take their values
///     and their theme parts.
/// </summary>
[Collection("FontEngine")]
public class HudMeterTests : IDisposable
{
    public HudMeterTests() => FontEngine.Initialize(0);

    private readonly List<UIElement> Owned = [];

    public void Dispose()
    {
        foreach (var element in Owned)
            element.Dispose();
    }

    private MainHudControl NewHud(PlayerAttributes attrs)
    {
        var classic = DispatchProxy.Create<IWorldHud, MainHudControlTests.RecordingHud>();
        var hud = new MainHudControl(classic, attributes: attrs, chat: new Chat());
        Owned.Add(hud);

        return hud;
    }

    [Theory]
    [InlineData(50u, 100u, 0.5f)]
    [InlineData(0u, 100u, 0f)]
    [InlineData(100u, 100u, 1f)]
    [InlineData(150u, 100u, 1f)]
    [InlineData(0u, 0u, 0f)]
    [InlineData(7u, 0u, 0f)]
    public void TheFractionIsTheValueOfTheMaximumClamped(uint current, uint max, float expected)
        => Assert.Equal(expected, HudMeter.FractionOf(current, max), 4);

    [Fact]
    public void AFractionOutsideZeroToOneIsClamped()
    {
        var meter = new HudMeter("m", new Rectangle(0, 0, 100, 16), "t", "f", Color.Red);
        Owned.Add(meter);

        meter.SetFill(1.5f);
        Assert.Equal(1f, meter.Fraction);

        meter.SetFill(-0.5f);
        Assert.Equal(0f, meter.Fraction);

        meter.SetFill(float.NaN);
        Assert.Equal(0f, meter.Fraction);
    }

    //the fill art keeps the size of the meter, and the value clips it
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.5f, 102)]
    [InlineData(1f, 203)]
    [InlineData(2f, 203)]
    [InlineData(1f / 1500, 1)]
    [InlineData(1499f / 1500, 202)]
    public void TheFillIsClippedAtTheFractionFromTheLeft(float fraction, int width)
    {
        var bounds = new Rectangle(4, 401, 203, 16);

        Assert.Equal(bounds with { Width = width }, HudMeter.FillIn(bounds, fraction));
    }

    //the flat fill does not cover the border
    [Fact]
    public void TheFlatFillIsInsideTheBorder()
        => Assert.Equal(new Rectangle(5, 402, 201, 14), HudMeter.FlatFillArea(new Rectangle(4, 401, 203, 16)));

    [Fact]
    public void TheGapsOfTheSegmentsAreAtEvenFractionsOfTheWidth()
    {
        var bounds = new Rectangle(4, 472, 632, 8);
        var gaps = HudMeter.GapsIn(bounds, 20).ToList();

        Assert.Equal(19, gaps.Count);
        Assert.Equal(4 + 32, gaps[0]);
        Assert.Equal(4 + 316, gaps[9]);
        Assert.Equal(4 + 600, gaps[^1]);
        Assert.Empty(HudMeter.GapsIn(bounds, 1));
    }

    [Fact]
    public void TheHealthAndManaBarsTakeTheirThemeParts()
    {
        var hud = NewHud(new PlayerAttributes());

        Assert.Equal(ThemeRoles.HudHealthTrack, hud.HealthBar.TrackRole);
        Assert.Equal(ThemeRoles.HudHealthFill, hud.HealthBar.FillRole);
        Assert.Equal(ThemeRoles.HudManaTrack, hud.ManaBar.TrackRole);
        Assert.Equal(ThemeRoles.HudManaFill, hud.ManaBar.FillRole);
        Assert.Equal(DialogPalette.HealthFill, hud.HealthBar.FlatFill);
        Assert.Equal(DialogPalette.ManaFill, hud.ManaBar.FlatFill);
        Assert.Equal("HP", hud.HealthBar.Label);
        Assert.Equal("MP", hud.ManaBar.Label);
        Assert.Equal(MainHudLayout.HealthBar, new Rectangle(hud.HealthBar.X, hud.HealthBar.Y, hud.HealthBar.Width, hud.HealthBar.Height));
        Assert.Equal(MainHudLayout.ManaBar, new Rectangle(hud.ManaBar.X, hud.ManaBar.Y, hud.ManaBar.Width, hud.ManaBar.Height));
    }

    [Fact]
    public void TheBarsFollowTheVitalsOfEachPacket()
    {
        var attrs = new PlayerAttributes();
        var hud = NewHud(attrs);

        attrs.Update(new AttributesPacket { Primary = new PrimaryAttributes { MaxHp = 1500, MaxMp = 900 } });
        attrs.Update(new AttributesPacket { Current = new CurrentAttributes { Hp = 1240, Mp = 610 } });

        Assert.Equal("1,240 / 1,500", hud.HealthBar.Value);
        Assert.Equal("610 / 900", hud.ManaBar.Value);
        Assert.Equal(1240f / 1500, hud.HealthBar.Fraction, 4);
        Assert.Equal(610f / 900, hud.ManaBar.Fraction, 4);

        attrs.Update(new AttributesPacket { Current = new CurrentAttributes { Hp = 0, Mp = 900 } });

        Assert.Equal("0 / 1,500", hud.HealthBar.Value);
        Assert.Equal(0f, hud.HealthBar.Fraction);
        Assert.Equal(1f, hud.ManaBar.Fraction);
    }

    //the attributes can arrive before the world screen makes its HUDs
    [Fact]
    public void AHudMadeAfterThePacketShowsItsVitals()
    {
        var attrs = new PlayerAttributes();
        attrs.Update(new AttributesPacket
        {
            Primary = new PrimaryAttributes { MaxHp = 200, MaxMp = 100 },
            Current = new CurrentAttributes { Hp = 50, Mp = 100 }
        });

        var hud = NewHud(attrs);

        Assert.Equal("50 / 200", hud.HealthBar.Value);
        Assert.Equal(0.25f, hud.HealthBar.Fraction, 4);
    }

    [Fact]
    public void TheBarsDoNotTakeTheMouse()
    {
        var hud = NewHud(new PlayerAttributes());

        Assert.False(hud.HealthBar.IsHitTestVisible);
        Assert.False(hud.ManaBar.IsHitTestVisible);
    }
}
