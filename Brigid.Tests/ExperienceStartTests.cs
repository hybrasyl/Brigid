#region
using Brigid.Data.Models;
using Brigid.Data.Repositories;
using Brigid.ViewModel;
using DALib.Networking.Packets.Server;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-148: the start of the current level. The attributes packet has no start of the level, so the client learns it
///     at a level-up of one level that it sees, in any order of the level and experience blocks, saves it for the
///     character, and does not guess it otherwise.
/// </summary>
public class ExperienceStartTests
{
    [Fact]
    public void ALevelUpOfOneLevelTeachesTheStartOfTheNewLevel()
    {
        var span = ExperienceSpan.ForLevel();

        Assert.False(span.Observe(40, 900, 100));
        Assert.Null(span.Start);

        Assert.True(span.Observe(41, 1020, 1980));
        Assert.Equal((byte)41, span.Level);
        Assert.Equal(1000ul, span.Start);
        Assert.Equal((20ul, 2000ul), span.Progress(41, 1020, 1980));
    }

    [Fact]
    public void AGainOfTwoLevelsMakesTheStartUnknown()
    {
        var span = ExperienceSpan.ForLevel();
        span.Observe(40, 900, 100);
        span.Observe(41, 1020, 1980);

        Assert.False(span.Observe(43, 9000, 500));
        Assert.Null(span.Start);
        Assert.Null(span.Progress(43, 9000, 500));
    }

    [Fact]
    public void LevelOneStartsAtZero()
    {
        var span = ExperienceSpan.ForLevel();
        span.Observe(1, 30, 70);

        Assert.Equal(0ul, span.Start);
        Assert.Equal((30ul, 100ul), span.Progress(1, 30, 70));
    }

    [Fact]
    public void ATrackWithNoFirstLevelDoesNotGuessAStart()
    {
        var span = ExperienceSpan.ForAbility();
        span.Observe(1, 30, 70);

        Assert.Null(span.Start);
    }

    [Fact]
    public void ASavedStartAppliesAtItsLevel()
    {
        var span = ExperienceSpan.ForLevel();
        span.Restore(41, 1000);
        span.Observe(41, 1500, 1500);

        Assert.Equal((500ul, 2000ul), span.Progress(41, 1500, 1500));
    }

    //the next character's saved start applies after a reset, before its first reading
    [Fact]
    public void ASavedStartAppliesAfterAReset()
    {
        var span = ExperienceSpan.ForLevel();
        span.Observe(30, 100, 50);
        span.Reset();
        span.Restore(41, 1000);
        span.Observe(41, 1500, 1500);

        Assert.Equal(1000ul, span.Start);
    }

    [Fact]
    public void ASavedStartOfAnotherLevelIsNotUsed()
    {
        var before = ExperienceSpan.ForLevel();
        before.Restore(40, 1000);
        before.Observe(41, 1500, 1500);

        Assert.Null(before.Start);

        var after = ExperienceSpan.ForLevel();
        after.Observe(41, 1500, 1500);
        after.Restore(40, 1000);

        Assert.Null(after.Start);
    }

    [Fact]
    public void ASavedStartDoesNotReplaceALearnedStart()
    {
        var span = ExperienceSpan.ForLevel();
        span.Observe(40, 900, 100);
        span.Observe(41, 1020, 1980);
        span.Restore(41, 7);

        Assert.Equal(1000ul, span.Start);
    }

    [Fact]
    public void ALossUnderTheStartGivesNothingGained()
    {
        var span = ExperienceSpan.ForLevel();
        span.Restore(41, 1000);

        Assert.Equal((0ul, 2100ul), span.Progress(41, 900, 2100));
    }

    [Fact]
    public void AReadingAtAnotherLevelGivesNoProgress()
    {
        var span = ExperienceSpan.ForLevel();
        span.Restore(41, 1000);

        Assert.Null(span.Progress(42, 1500, 100));
    }

    private static AttributesPacket Full(byte level, uint total, uint toNext, byte ability = 0, uint abilityTotal = 0, uint abilityToNext = 0)
        => new()
        {
            Primary = new PrimaryAttributes { Level = level, Ability = ability },
            Experience = new ExperienceAttributes { Experience = total, ExpToLevel = toNext, AbilityExp = abilityTotal, NextAB = abilityToNext }
        };

    //Hybrasyl sends one full packet at a level-up (User.GiveExperience)
    [Fact]
    public void TheAttributesLearnTheStartAndAskForASave()
    {
        var attrs = new PlayerAttributes();
        var saves = 0;
        attrs.SpanLearned += () => saves++;

        attrs.Update(Full(40, 900, 100));
        attrs.Update(Full(40, 950, 50));
        Assert.Equal(0, saves);

        attrs.Update(Full(41, 1020, 1980));

        Assert.Equal(1, saves);
        Assert.Equal(1000ul, attrs.LevelSpan.Start);
    }

    //a packet of the level alone, then a packet of the experience, still gives the threshold of the level before
    [Fact]
    public void ALevelUpInTwoPacketsTeachesTheSameStart()
    {
        var attrs = new PlayerAttributes();
        attrs.Update(Full(40, 900, 100));
        attrs.Update(new AttributesPacket { Primary = new PrimaryAttributes { Level = 41 } });
        attrs.Update(new AttributesPacket { Experience = new ExperienceAttributes { Experience = 1020, ExpToLevel = 1980 } });

        Assert.Equal(1000ul, attrs.LevelSpan.Start);
        Assert.Equal((20ul, 2000ul), attrs.LevelSpan.Progress(41, 1020, 1980));
    }

    //the level comes in the primary block: an experience block before it does not count as a reading at level 0
    [Fact]
    public void TheSpansWaitForTheFirstPrimaryBlock()
    {
        var attrs = new PlayerAttributes();
        attrs.Update(new AttributesPacket { Experience = new ExperienceAttributes { Experience = 0, ExpToLevel = 5 } });
        attrs.Update(Full(1, 10, 90));

        Assert.Equal(0ul, attrs.LevelSpan.Start);

        var later = new PlayerAttributes();
        later.Update(new AttributesPacket { Experience = new ExperienceAttributes { Experience = 0, ExpToLevel = 5 } });
        later.Update(Full(41, 1500, 1500));

        Assert.Null(later.LevelSpan.Start);
    }

    //an experience block of the new level before the level block: the reading at the old level has the threshold of the
    //level after, and the lowest threshold of the level is kept
    [Fact]
    public void AnExperienceBlockBeforeTheLevelBlockTeachesTheSameStart()
    {
        var attrs = new PlayerAttributes();
        attrs.Update(Full(40, 900, 100));
        attrs.Update(new AttributesPacket { Experience = new ExperienceAttributes { Experience = 1020, ExpToLevel = 1980 } });
        attrs.Update(new AttributesPacket { Primary = new PrimaryAttributes { Level = 41 } });

        Assert.Equal(1000ul, attrs.LevelSpan.Start);
    }

    //the reading at the new level with the total of the level before gives no threshold, so the next level-up is right
    [Fact]
    public void ALevelBlockBeforeTheExperienceBlockKeepsTheNextStartRight()
    {
        var attrs = new PlayerAttributes();
        attrs.Update(Full(40, 900, 100));
        attrs.Update(new AttributesPacket { Primary = new PrimaryAttributes { Level = 41 } });
        attrs.Update(new AttributesPacket { Experience = new ExperienceAttributes { Experience = 1020, ExpToLevel = 1980 } });
        attrs.Update(Full(42, 3010, 2990));

        Assert.Equal(3000ul, attrs.LevelSpan.Start);
    }

    //Hybrasyl sends 0 to the next level when the total is past a threshold that a formula change lowered
    [Fact]
    public void AReadingWithNothingToTheNextLevelGivesNoThreshold()
    {
        var span = ExperienceSpan.ForLevel();
        span.Observe(40, 5000, 0);

        Assert.False(span.Observe(41, 5100, 900));
        Assert.Null(span.Start);
    }

    [Fact]
    public void TheAbilityLevelHasItsOwnSpan()
    {
        var attrs = new PlayerAttributes();
        attrs.Update(Full(99, 5000, 0, 3, 700, 300));
        attrs.Update(Full(99, 5000, 0, 4, 1100, 1900));

        Assert.Equal(1000ul, attrs.AbilitySpan.Start);
        Assert.Null(attrs.LevelSpan.Start);
    }

    [Fact]
    public void AClearForgetsTheStarts()
    {
        var attrs = new PlayerAttributes();
        attrs.Update(Full(40, 900, 100));
        attrs.Update(Full(41, 1020, 1980));
        attrs.Clear();

        Assert.Null(attrs.LevelSpan.Start);

        //the first primary block after the clear is not a level-up from the character before
        attrs.Update(Full(42, 5000, 100));
        Assert.Null(attrs.LevelSpan.Start);
    }

    //after a clear, an experience block before the first primary block of the next character is not a reading at the
    //level of the character before
    [Fact]
    public void AfterAClearTheSpansWaitForThePrimaryBlockAgain()
    {
        var attrs = new PlayerAttributes();
        attrs.Update(Full(41, 1500, 1500));
        attrs.Clear();
        attrs.Update(new AttributesPacket { Experience = new ExperienceAttributes { Experience = 100, ExpToLevel = 200 } });
        attrs.Update(Full(42, 5000, 100));

        Assert.Null(attrs.LevelSpan.Start);
    }

    [Fact]
    public void TheAttributesRestoreTheSavedStartsAndShowThem()
    {
        var attrs = new PlayerAttributes();
        attrs.Update(Full(41, 1500, 1500));
        var changes = 0;
        attrs.Changed += () => changes++;

        attrs.RestoreStarts(new SavedExperience(new SavedLevelStart(41, 1000), new SavedLevelStart(3, 50)));

        Assert.Equal(1, changes);
        Assert.Equal(1000ul, attrs.LevelSpan.Start);
        Assert.Equal(new SavedExperience(new SavedLevelStart(41, 1000), null), attrs.SavedStarts());
    }

    [Fact]
    public void TheSavedStartsAreTheKnownStarts()
    {
        var attrs = new PlayerAttributes();
        attrs.Update(Full(99, 5000, 0, 3, 700, 300));
        attrs.Update(Full(99, 5000, 0, 4, 1100, 1900));

        Assert.Equal(new SavedExperience(null, new SavedLevelStart(4, 1000)), attrs.SavedStarts());
    }

    [Fact]
    public void TheSaveReadsBackWhatItWrote()
    {
        var saved = new SavedExperience(new SavedLevelStart(41, 1000), new SavedLevelStart(3, 5_000_000_000));
        var lines = LocalPlayerSettingsRepository.FormatExperience(saved);

        Assert.Equal(["Level: 41 1000", "Ability: 3 5000000000"], lines);
        Assert.Equal(saved, LocalPlayerSettingsRepository.ParseExperience(lines));
        Assert.Empty(LocalPlayerSettingsRepository.FormatExperience(new SavedExperience(null, null)));
    }

    [Theory]
    [InlineData("Level: 41")]
    [InlineData("Level: 41 x")]
    [InlineData("Level: 300 1000")]
    [InlineData("Level: -1 1000")]
    [InlineData("Level: +41 1000")]
    [InlineData("Level: 41 1000 7")]
    [InlineData("Level 41 1000")]
    [InlineData("Other: 41 1000")]
    [InlineData("")]
    public void ALineThatDoesNotParseGivesNoStart(string line)
        => Assert.Equal(new SavedExperience(null, null), LocalPlayerSettingsRepository.ParseExperience([line]));
}
