#region
using System.Reflection;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Hud;
using Brigid.Controls.World.Hud.Main;
using Brigid.Rendering;
using Brigid.ViewModel;
using DALib.Networking.Packets.Server;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-148: the experience bar (BRIG-127 D27). Below the level cap it shows the experience of the level while
///     the start is known. At the cap, Hybrasyl's bar is blue and shows the experience toward the next Crystallized
///     Reflection; retail's bar is full and yellow with ability level 0, and blue with the ability experience after it.
/// </summary>
[Collection("FontEngine")]
public class HudExperienceTests : IDisposable
{
    public HudExperienceTests() => FontEngine.Initialize(0);

    private readonly List<UIElement> Owned = [];

    public void Dispose()
    {
        foreach (var element in Owned)
            element.Dispose();
    }

    private static PlayerAttributes Reading(byte level, uint total, uint toNext, byte ability = 0, uint abilityTotal = 0, uint abilityToNext = 0)
    {
        var attrs = new PlayerAttributes();
        attrs.Update(Packet(level, total, toNext, ability, abilityTotal, abilityToNext));

        return attrs;
    }

    private static AttributesPacket Packet(byte level, uint total, uint toNext, byte ability = 0, uint abilityTotal = 0, uint abilityToNext = 0)
        => new()
        {
            Primary = new PrimaryAttributes { Level = level, Ability = ability },
            Experience = new ExperienceAttributes { Experience = total, ExpToLevel = toNext, AbilityExp = abilityTotal, NextAB = abilityToNext }
        };

    [Fact]
    public void BelowTheCapAKnownStartFillsTheBarToTheLevel()
    {
        var attrs = Reading(40, 900, 100);
        attrs.Update(Packet(41, 1500, 1500));

        var view = HudExperience.Describe(attrs, false);

        Assert.Equal(0.25f, view.Fraction, 4);
        Assert.False(view.UsesCapFill);
        Assert.Equal("Level 41", view.Title);
        Assert.Equal(["500 / 2,000 (25%)", "Total: 1,500"], view.Lines);
    }

    [Fact]
    public void BelowTheCapAnUnknownStartLeavesTheBarEmpty()
    {
        var view = HudExperience.Describe(Reading(41, 12_345, 1500), true);

        Assert.Equal(0f, view.Fraction);
        Assert.False(view.UsesCapFill);
        Assert.Equal(["1,500 to level 42", "Total: 12,345"], view.Lines);
    }

    //the percent rounds down: 1999 of 2000 is 99%, not 100%
    [Fact]
    public void ThePercentDoesNotRoundUp()
    {
        var attrs = Reading(40, 900, 100);
        attrs.Update(Packet(41, 2999, 1));

        Assert.Equal("1,999 / 2,000 (99%)", HudExperience.Describe(attrs, false).Lines[0]);
    }

    [Fact]
    public void AtTheCapOnHybrasylTheBarShowsTheNextReflection()
    {
        var view = HudExperience.Describe(Reading(75, 1_300_000, 0), true);

        Assert.True(view.UsesCapFill);
        Assert.Equal(50_000f / 250_000, view.Fraction, 4);
        Assert.Equal("Level 75", view.Title);
        Assert.Equal(["Total: 1,300,000", "50,000 / 250,000 to the next Crystallized Reflection"], view.Lines);
    }

    [Fact]
    public void AtTheCapOnRetailWithNoAbilityLevelTheBarIsFullAndYellow()
    {
        var view = HudExperience.Describe(Reading(99, 4_000_000, 0, 0, 1200), false);

        Assert.False(view.UsesCapFill);
        Assert.Equal(1f, view.Fraction);
        Assert.Equal(["Total: 4,000,000", "Ability level 0", "Ability experience: 1,200"], view.Lines);
    }

    [Fact]
    public void AtTheCapOnRetailTheAbilityLevelFillsTheBlueBar()
    {
        var attrs = Reading(99, 4_000_000, 0, 3, 700, 300);
        attrs.Update(Packet(99, 4_000_000, 0, 4, 1500, 1500));

        var view = HudExperience.Describe(attrs, false);

        Assert.True(view.UsesCapFill);
        Assert.Equal(0.25f, view.Fraction, 4);
        Assert.Equal(
            ["Total: 4,000,000", "Ability level 4", "Ability experience: 1,500", "500 / 2,000 (25%)"],
            view.Lines);
    }

    [Fact]
    public void AnUnknownAbilityStartGivesTheExperienceToTheNextAbilityLevel()
    {
        var view = HudExperience.Describe(Reading(99, 4_000_000, 0, 4, 1500, 1500), false);

        Assert.True(view.UsesCapFill);
        Assert.Equal(0f, view.Fraction);
        Assert.Equal("1,500 to ability level 5", view.Lines[^1]);
    }

    [Fact]
    public void AtTheAbilityCapTheBlueBarIsFull()
    {
        var view = HudExperience.Describe(Reading(99, 4_000_000, 0, 99, 9000, 0), false);

        Assert.True(view.UsesCapFill);
        Assert.Equal(1f, view.Fraction);
        Assert.Equal(["Total: 4,000,000", "Ability level 99", "Ability experience: 9,000"], view.Lines);
    }

    private MainHudControl NewHud(PlayerAttributes attrs)
    {
        var classic = DispatchProxy.Create<IWorldHud, MainHudControlTests.RecordingHud>();
        var hud = new MainHudControl(classic, attributes: attrs, chat: new Chat());
        Owned.Add(hud);

        return hud;
    }

    [Fact]
    public void TheExperienceBarHasTwentySegmentsAndTakesTheMouseForItsTooltip()
    {
        var hud = NewHud(new PlayerAttributes());
        var bar = hud.ExperienceBar;

        Assert.Equal(20, bar.Segments);
        Assert.True(bar.IsHitTestVisible);
        Assert.Equal(ThemeRoles.HudExperienceTrack, bar.TrackRole);
        Assert.Equal(MainHudLayout.ExperienceBar, new Microsoft.Xna.Framework.Rectangle(bar.X, bar.Y, bar.Width, bar.Height));
    }

    [Fact]
    public void TheExperienceBarChangesItsFillAtTheCap()
    {
        var attrs = Reading(40, 900, 100);
        var hud = NewHud(attrs);

        Assert.Equal(ThemeRoles.HudExperienceFill, hud.ExperienceBar.FillRole);
        Assert.Equal(DialogPalette.ExperienceFill, hud.ExperienceBar.FlatFill);

        attrs.Update(Packet(75, 1_300_000, 0));
        Assert.Equal(ThemeRoles.HudExperienceFill, hud.ExperienceBar.FillRole);
        Assert.Equal(1f, hud.ExperienceBar.Fraction);

        hud.ConvertsExperiencePastCap = true;

        Assert.Equal(ThemeRoles.HudExperienceCapFill, hud.ExperienceBar.FillRole);
        Assert.Equal(DialogPalette.CapFill, hud.ExperienceBar.FlatFill);
        Assert.Equal(0.2f, hud.ExperienceBar.Fraction, 4);
    }

    [Fact]
    public void ARestoredStartShowsAfterARefresh()
    {
        var attrs = Reading(41, 1500, 1500);
        var hud = NewHud(attrs);
        Assert.Equal(0f, hud.ExperienceBar.Fraction);

        attrs.LevelSpan.Restore(41, 1000);
        hud.RefreshAttributes();

        Assert.Equal(0.25f, hud.ExperienceBar.Fraction, 4);
    }

    [Fact]
    public void TheTooltipShowsTheLevelAsItsTitleWhileTheMouseIsOverTheBar()
    {
        var hud = NewHud(Reading(41, 12_345, 1500));

        Assert.Equal(
            [
                TooltipLine.Themed("Level 41", ThemeRoles.TextTitle), TooltipLine.Themed("1,500 to level 42", ThemeRoles.Text),
                TooltipLine.Themed("Total: 12,345", ThemeRoles.Text)
            ],
            hud.ExperienceLines());

        hud.ExperienceBar.OnMouseEnter();

        var tooltip = hud.Children.OfType<HudTooltip>().Single();
        Assert.True(tooltip.Visible);

        hud.ExperienceBar.OnMouseLeave();
        Assert.False(tooltip.Visible);
    }

    [Fact]
    public void BeforeTheAttributesTheBarShowsNoTooltip()
    {
        var hud = NewHud(new PlayerAttributes());
        hud.ExperienceBar.OnMouseEnter();

        Assert.False(hud.Children.OfType<HudTooltip>().Single().Visible);
    }
}
