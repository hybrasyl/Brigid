#region
using System.Reflection;
using Brigid.Controls.World.Hud;
using Brigid.Controls.World.Hud.Main;
using Brigid.Rendering;
using Brigid.Rendering.Utility;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using SkiaSharp;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-147: the effect bar of the new HUD. The effects show in the order that they arrive, a colour update keeps the
///     place of its effect, a removal closes the gap, the effects past the last slot wait, and the clock swipe covers the
///     time that has gone in the six steps of the colour bands.
/// </summary>
[Collection("FontEngine")]
public class HudEffectBarTests : IDisposable
{
    public HudEffectBarTests() => FontEngine.Initialize(0);

    private readonly List<ushort> IconReads = [];
    private readonly List<IDisposable> Owned = [];

    public void Dispose()
    {
        foreach (var owned in Owned)
            owned.Dispose();
    }

    private HudEffectBar NewBar()
    {
        var bar = new HudEffectBar(icon =>
        {
            IconReads.Add(icon);

            return null;
        });
        Owned.Add(bar);

        return bar;
    }

    private static ushort[] Icons(HudEffectBar bar) => bar.SlotList.Where(slot => slot.Visible).Select(slot => slot.Icon!.Value).ToArray();

    [Fact]
    public void TheEffectsShowInTheOrderThatTheyArrive()
    {
        var bar = NewBar();
        bar.SetEffect(30, EffectColor.White);
        bar.SetEffect(10, EffectColor.Red);
        bar.SetEffect(20, EffectColor.Blue);

        Assert.Equal([30, 10, 20], Icons(bar));
    }

    [Fact]
    public void AColourUpdateKeepsThePlaceOfItsEffect()
    {
        var bar = NewBar();
        bar.SetEffect(30, EffectColor.White);
        bar.SetEffect(10, EffectColor.White);
        bar.SetEffect(30, EffectColor.Green);

        Assert.Equal([30, 10], Icons(bar));
        Assert.Equal(4f / 6, bar.SlotList[0].Gone, 4);
    }

    [Fact]
    public void ARemovalClosesTheGap()
    {
        var bar = NewBar();
        bar.SetEffect(30, EffectColor.White);
        bar.SetEffect(10, EffectColor.White);
        bar.SetEffect(20, EffectColor.White);
        bar.SetEffect(10, EffectColor.None);

        Assert.Equal([30, 20], Icons(bar));
        Assert.False(bar.SlotList[2].Visible);
        Assert.Null(bar.SlotList[2].Icon);
    }

    [Fact]
    public void ARemovalOfAnEffectThatIsNotThereChangesNothing()
    {
        var bar = NewBar();
        bar.SetEffect(30, EffectColor.White);
        bar.SetEffect(99, EffectColor.None);

        Assert.Equal([30], Icons(bar));
    }

    [Fact]
    public void TheEffectsPastTheLastSlotWaitForAFreeSlot()
    {
        var bar = NewBar();
        var slots = MainHudLayout.MaxEffectSlots;

        for (var i = 0; i <= slots; i++)
            bar.SetEffect((ushort)(100 + i), EffectColor.White);

        Assert.Equal(slots + 1, bar.Effects.Count);
        Assert.DoesNotContain((ushort)(100 + slots), Icons(bar));

        bar.SetEffect(100, EffectColor.None);

        Assert.Equal((ushort)(100 + slots), Icons(bar)[^1]);
    }

    //the packet's icon is 16 bits; an icon over 255 keeps its value
    [Fact]
    public void AnIconOver255KeepsItsValue()
    {
        var bar = NewBar();
        bar.SetEffect(300, EffectColor.White);

        Assert.Equal([300], Icons(bar));
        Assert.Equal([(ushort)300], IconReads);
    }

    //the placeholder of an unknown icon is the 32 x 32 checker; drawn whole, it covered the border of the 18 x 18 slot
    [Theory]
    [InlineData(32, 32, 15, 15)]
    [InlineData(15, 15, 15, 15)]
    [InlineData(10, 12, 10, 12)]
    public void TheSlotDrawsAtMostTheIconSquare(int width, int height, int drawnWidth, int drawnHeight)
        => Assert.Equal(new Rectangle(0, 0, drawnWidth, drawnHeight), HudEffectSlot.IconSourceRect(width, height));

    [Fact]
    public void TheIconIsReadOnceForAnEffect()
    {
        var bar = NewBar();
        bar.SetEffect(30, EffectColor.White);
        bar.SetEffect(30, EffectColor.Red);
        bar.SetEffect(30, EffectColor.Orange);

        Assert.Equal([(ushort)30], IconReads);
    }

    //white is a full time and blue its last sixth; each band falls on a step of the wedge masks exactly
    [Theory]
    [InlineData(EffectColor.White, 0)]
    [InlineData(EffectColor.Red, 1)]
    [InlineData(EffectColor.Orange, 2)]
    [InlineData(EffectColor.Yellow, 3)]
    [InlineData(EffectColor.Green, 4)]
    [InlineData(EffectColor.Blue, 5)]
    [InlineData(EffectColor.None, 6)]
    public void EachBandIsASixthOfTheTimeGone(EffectColor color, int sixthsGone)
    {
        Assert.Equal(sixthsGone / 6f, HudEffectBar.GoneOf(color), 4);
        Assert.Equal(sixthsGone * ClockWedgeCache.STEPS / 6, ClockWedgeCache.StepOf(HudEffectBar.GoneOf(color)));
    }

    //a smooth part of the time rounds to the nearest of the steps, and out of 0 to 1 it is clamped
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.004f, 0)]
    [InlineData(0.01f, 1)]
    [InlineData(0.5f, 30)]
    [InlineData(1f, 60)]
    [InlineData(1.5f, 60)]
    [InlineData(-1f, 0)]
    public void APartOfTheTimeRoundsToAStep(float gone, int step) => Assert.Equal(step, ClockWedgeCache.StepOf(gone));

    [Fact]
    public void ThePartGoneOfASlotIsClamped()
    {
        var bar = NewBar();
        var slot = bar.SlotList[0];

        slot.Show(30, 2f);
        Assert.Equal(1f, slot.Gone);

        slot.Show(30, -1f);
        Assert.Equal(0f, slot.Gone);
    }

    //the bottom area under the bar takes the mouse
    [Fact]
    public void TheBarAndItsSlotsDoNotTakeTheMouse()
    {
        var bar = NewBar();

        Assert.False(bar.IsHitTestVisible);
        Assert.All(bar.SlotList, slot => Assert.False(slot.IsHitTestVisible));
    }

    //the half-size filter reads 30 px on each side: a smaller pack icon is scaled up to it first
    [Theory]
    [InlineData(20, 20, 30, 30)]
    [InlineData(40, 12, 30, 30)]
    [InlineData(32, 32, 32, 32)]
    public void ASmallIconIsScaledUpBeforeTheFilter(int width, int height, int expectedWidth, int expectedHeight)
    {
        using var bitmap = new SKBitmap(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var source = UiRenderer.ToDownsampleSource(image, 30);

        Assert.Equal((expectedWidth, expectedHeight), (source.Width, source.Height));
    }

    //the half-size icon read a 20 px pack icon at its own size, and a scale-up that drew nothing gave a clear icon
    [Fact]
    public void ASmallIconGivesAHalfSizeIconOfItsColour()
    {
        var colour = new SKColor(200, 40, 40);
        using var bitmap = new SKBitmap(new SKImageInfo(20, 20, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(colour);
        using var image = SKImage.FromBitmap(bitmap);

        var pixels = UiRenderer.HalfSizePixels(image, UiRenderer.HALF_SPELL_ICON);

        Assert.Equal(UiRenderer.HALF_SPELL_ICON * UiRenderer.HALF_SPELL_ICON, pixels.Length);
        Assert.All(
            pixels,
            pixel =>
            {
                Assert.InRange(pixel.Alpha, (byte)240, (byte)255);
                Assert.InRange(pixel.Red, (byte)192, (byte)208);
                Assert.InRange(pixel.Green, (byte)32, (byte)48);
                Assert.InRange(pixel.Blue, (byte)32, (byte)48);
            });
    }

    //the pixels at 12, 3, 6 and 9 o'clock of a 15 px icon: the wedge goes clockwise from 12 o'clock, and a pixel on its
    //edge (3 o'clock at a quarter) is out of it
    [Theory]
    [InlineData(0f, false, false, false, false)]
    [InlineData(1f / 6, true, false, false, false)]
    [InlineData(0.25f, true, false, false, false)]
    [InlineData(2f / 6, true, true, false, false)]
    [InlineData(4f / 6, true, true, true, false)]
    [InlineData(5f / 6, true, true, true, true)]
    [InlineData(1f, true, true, true, true)]
    public void TheWedgeGoesClockwiseFromTwelve(float gone, bool twelve, bool three, bool six, bool nine)
    {
        Assert.Equal(twelve, ImageUtil.InClockWedge(7, 0, 15, gone));
        Assert.Equal(three, ImageUtil.InClockWedge(14, 7, 15, gone));
        Assert.Equal(six, ImageUtil.InClockWedge(7, 14, 15, gone));
        Assert.Equal(nine, ImageUtil.InClockWedge(0, 7, 15, gone));
    }

    //the cooldown swipe covers the rest of the turn after the time gone, so its edge also moves clockwise: with a quarter
    //left, it covers from 9 o'clock to 12, not from 12 to 3. A start of exactly 0.5 puts the 6 o'clock pixel on the edge,
    //and MathF.Atan2 on osx-arm64 gives that pixel a turn just under 0.5, so the half-turn case starts at 0.45
    [Theory]
    [InlineData(0.75f, 1f, false, false, false, true)]
    [InlineData(0.45f, 1f, false, false, true, true)]
    [InlineData(0f, 1f, true, true, true, true)]
    [InlineData(1f, 1f, false, false, false, false)]
    public void TheRestOfTheTurnIsAnArcToTwelve(float start, float end, bool twelve, bool three, bool six, bool nine)
    {
        Assert.Equal(twelve, ImageUtil.InClockArc(7, 0, 15, start, end));
        Assert.Equal(three, ImageUtil.InClockArc(14, 7, 15, start, end));
        Assert.Equal(six, ImageUtil.InClockArc(7, 14, 15, start, end));
        Assert.Equal(nine, ImageUtil.InClockArc(0, 7, 15, start, end));
    }

    [Fact]
    public void TheSlotsFitTheBar()
    {
        var last = MainHudLayout.EffectSlotAt(MainHudLayout.MaxEffectSlots - 1);

        Assert.True(last.Right <= MainHudLayout.EffectBar.Width);
        Assert.True(MainHudLayout.EffectSlotAt(MainHudLayout.MaxEffectSlots).Right > MainHudLayout.EffectBar.Width);
        Assert.True(MainHudLayout.EffectDivider.Top > MainHudLayout.EffectBar.Bottom);
        Assert.True(MainHudLayout.EffectDivider.Bottom <= MainHudLayout.HealthBar.Top);
    }

    [Fact]
    public void TheNewHudShowsTheEffectsAndADividerUnderThem()
    {
        var classic = DispatchProxy.Create<IWorldHud, MainHudControlTests.RecordingHud>();
        var hud = new MainHudControl(classic, attributes: new PlayerAttributes(), chat: new Chat());
        Owned.Add(hud);

        hud.SetEffect(300, EffectColor.Red);

        Assert.Equal([new HudEffect(300, EffectColor.Red)], hud.EffectBar.Effects);

        var divider = hud.Children.Single(child => child.Name == "effect_divider");
        Assert.Equal(MainHudLayout.EffectDivider, new Rectangle(divider.X, divider.Y, divider.Width, divider.Height));
        Assert.Empty(((MainHudControlTests.RecordingHud)(object)classic).Calls);
    }
}
