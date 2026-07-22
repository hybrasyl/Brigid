using Brigid.Systems;
using Chaos.DarkAges.Definitions;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     An unresolvable motion must play nothing. Retail's image session starts a body motion only when it can select
///     one from the current body resources; Brigid used to fall back to the assail swing, so any motion id outside the
///     mapped set — a spell's own cast motion among them — rendered as an unrequested attack.
/// </summary>
public sealed class BodyAnimationResolutionTests
{
    [Fact]
    public void None_ResolvesToNoFrames() => Assert.False(AnimationSystem.HasBodyAnimation(BodyAnimation.None));

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(20)]
    [InlineData(146)]
    [InlineData(200)]
    [InlineData(255)]
    public void UnmappedId_ResolvesToNoFrames(byte animation)
        => Assert.False(AnimationSystem.HasBodyAnimation((BodyAnimation)animation));

    [Theory]
    [InlineData(BodyAnimation.Assail)]
    [InlineData(BodyAnimation.PriestCast)]
    [InlineData(BodyAnimation.WizardCast)]
    [InlineData(BodyAnimation.TwoHandAtk)]
    [InlineData(BodyAnimation.Kick)]
    [InlineData(BodyAnimation.Stab)]
    [InlineData(BodyAnimation.Summon)]
    [InlineData(BodyAnimation.HandsUp)]
    public void MappedMotion_StillAnimates(BodyAnimation animation) => Assert.True(AnimationSystem.HasBodyAnimation(animation));

    /// <summary>
    ///     BlowKiss and Wave are the two emotes that also drive a body animation, so they must keep resolving frames
    ///     from the emote arm rather than being swept into the unmapped default.
    /// </summary>
    [Theory]
    [InlineData(BodyAnimation.BlowKiss)]
    [InlineData(BodyAnimation.Wave)]
    public void EmoteWithBodyAnimation_StillAnimates(BodyAnimation animation)
    {
        (var suffix, var framesPerDirection, _, _) = AnimationSystem.ResolveBodyAnimParams(animation);

        Assert.True(framesPerDirection > 0);
        Assert.Equal("03", suffix);
    }

    /// <summary>
    ///     Retail's monster image session accepts only these four motion ids and rejects the rest; Brigid used to map
    ///     everything else onto Attack1.
    /// </summary>
    [Theory]
    [InlineData(BodyAnimation.Assail, true)]
    [InlineData(BodyAnimation.Kick, true)]
    [InlineData(BodyAnimation.Punch, true)]
    [InlineData(BodyAnimation.RoundHouseKick, true)]
    [InlineData(BodyAnimation.None, false)]
    [InlineData(BodyAnimation.HandsUp, false)]
    [InlineData(BodyAnimation.PriestCast, false)]
    [InlineData(BodyAnimation.TwoHandAtk, false)]
    [InlineData(BodyAnimation.Summon, false)]
    public void CreatureMotion_MatchesRetailAcceptSet(BodyAnimation animation, bool accepted)
        => Assert.Equal(accepted, AnimationSystem.HasCreatureBodyAnimation(animation));
}
