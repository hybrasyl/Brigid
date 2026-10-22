#region
using Brigid.Controls.LobbyLogin;
using Chaos.DarkAges.Definitions;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-116 creator appearance keeps the rules of the legacy creator: the hair style wraps at the limit of each
///     gender and moves down when the limit falls, the colours are the legacy swatch's 14, and the facing turns in a cycle
///     of four.
/// </summary>
public class CreatorAppearanceTests
{
    [Fact]
    public void ANewAppearanceIsTheLegacyStart()
    {
        var appearance = new CreatorAppearance();

        Assert.Equal(Gender.Male, appearance.Gender);
        Assert.Equal(1, appearance.HairStyle);
        Assert.Equal(DisplayColor.Default, appearance.HairColor);
        Assert.Equal(1, appearance.Direction);
    }

    [Theory]
    [InlineData(Gender.Male, 18)]
    [InlineData(Gender.Female, 17)]
    public void TheHairStyleWrapsAtTheLimitOfTheGender(Gender gender, int limit)
    {
        var appearance = new CreatorAppearance();
        appearance.SetGender(gender);

        Assert.Equal(limit, appearance.MaxHairStyle);

        appearance.PreviousHairStyle();
        Assert.Equal(limit, appearance.HairStyle);

        appearance.NextHairStyle();
        Assert.Equal(1, appearance.HairStyle);
    }

    [Fact]
    public void AStyleAboveTheFemaleLimitMovesDownToIt()
    {
        var appearance = new CreatorAppearance();
        appearance.PreviousHairStyle();
        Assert.Equal(CreatorAppearance.MAX_MALE_HAIR_STYLE, appearance.HairStyle);

        appearance.SetGender(Gender.Female);

        Assert.Equal(CreatorAppearance.MAX_FEMALE_HAIR_STYLE, appearance.HairStyle);
    }

    [Fact]
    public void TheColoursAreTheLegacySwatchInItsOrder()
        => Assert.Equal(
            [
                DisplayColor.Teal, DisplayColor.Green, DisplayColor.Olive, DisplayColor.Yellow, DisplayColor.Pumpkin,
                DisplayColor.Apple, DisplayColor.Violet, DisplayColor.Default, DisplayColor.Navy, DisplayColor.Blue,
                DisplayColor.Gray, DisplayColor.Carrot, DisplayColor.Brown, DisplayColor.Black
            ],
            CreatorAppearance.HairColors);

    [Fact]
    public void AColourThatTheCreatorDoesNotOfferIsIgnored()
    {
        var appearance = new CreatorAppearance();
        var changes = 0;
        appearance.Changed += () => changes++;

        var offered = CreatorAppearance.HairColors;
        var missing = Enum.GetValues<DisplayColor>().First(color => !offered.Contains(color));
        appearance.SetHairColor(missing);

        Assert.Equal(DisplayColor.Default, appearance.HairColor);
        Assert.Equal(0, changes);

        appearance.SetHairColor(DisplayColor.Navy);

        Assert.Equal(DisplayColor.Navy, appearance.HairColor);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void TheFacingTurnsInACycleOfFour()
    {
        var appearance = new CreatorAppearance();

        //the legacy left arrow went 1, 2, 3, 0 and the right arrow back
        appearance.TurnClockwise();
        Assert.Equal(2, appearance.Direction);
        appearance.TurnClockwise();
        appearance.TurnClockwise();
        Assert.Equal(0, appearance.Direction);

        appearance.TurnCounterClockwise();
        Assert.Equal(3, appearance.Direction);
    }

    [Fact]
    public void ResetReturnsToTheStart()
    {
        var appearance = new CreatorAppearance();
        appearance.SetGender(Gender.Female);
        appearance.NextHairStyle();
        appearance.SetHairColor(DisplayColor.Black);
        appearance.TurnClockwise();

        appearance.Reset();

        Assert.Equal(Gender.Male, appearance.Gender);
        Assert.Equal(1, appearance.HairStyle);
        Assert.Equal(DisplayColor.Default, appearance.HairColor);
        Assert.Equal(1, appearance.Direction);
    }
}
