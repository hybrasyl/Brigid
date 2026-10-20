using Brigid.Models;
using Brigid.Rendering;
using Brigid.Rendering.Definitions;
using Brigid.Systems;
using Chaos.Geometry.Abstractions.Definitions;
using Xunit;

namespace Brigid.Tests;

// The static idle picker (portraits, previews) and the world picker follow one rule, so a portrait shows the frame the
// creature stands on in the world. A StaticNoIdle creature (no optional loop) holds its first walk frame, even when it
// declares standing frames.
public class CreatureIdleFrameTests
{
    private static readonly Direction[] Directions = [Direction.Up, Direction.Right, Direction.Down, Direction.Left];

    //walk 0-7 (4 per facing), standing 8+, total frames as given
    private static CreatureAnimInfo Info(byte standingCount, byte optionalCount, int total = 24)
        => new(
            WalkFrameIndex: 0,
            WalkFrameCount: 4,
            AttackFrameIndex: 0,
            AttackFrameCount: 0,
            Attack2StartIndex: 0,
            Attack2FrameCount: 0,
            Attack3StartIndex: 0,
            Attack3FrameCount: 0,
            StandingFrameIndex: 8,
            StandingFrameCount: standingCount,
            OptionalAnimationFrameCount: optionalCount,
            OptionalAnimationProbability: 0,
            AnimationIntervalMs: 300,
            TotalFrameCount: total);

    private static (int, bool) InWorld(in CreatureAnimInfo info, Direction direction)
        => AnimationSystem.GetCreatureFrame(new WorldEntity { Type = ClientEntityType.Creature, Direction = direction }, in info);

    [Theory]
    [InlineData(Direction.Up, 0, false)]
    [InlineData(Direction.Right, 4, false)]
    [InlineData(Direction.Down, 4, true)]
    [InlineData(Direction.Left, 0, true)]
    public void GetCreatureIdleFrame_StaticNoIdleHoldsTheFirstWalkFrame(Direction direction, int frame, bool flip)
        => Assert.Equal((frame, flip), AnimationSystem.GetCreatureIdleFrame(Info(standingCount: 2, optionalCount: 0), direction));

    [Theory]
    [InlineData(Direction.Up, 8, false)]
    [InlineData(Direction.Right, 11, false)]
    [InlineData(Direction.Down, 11, true)]
    [InlineData(Direction.Left, 8, true)]
    public void GetCreatureIdleFrame_NormalIdleStartsAtTheStandingFrame(Direction direction, int frame, bool flip)
        => Assert.Equal((frame, flip), AnimationSystem.GetCreatureIdleFrame(Info(standingCount: 3, optionalCount: 3), direction));

    [Theory]
    [InlineData(Direction.Up, 8, false)]
    [InlineData(Direction.Right, 13, false)]
    [InlineData(Direction.Down, 13, true)]
    [InlineData(Direction.Left, 8, true)]
    public void GetCreatureIdleFrame_OptionalIdleOffsetsTheSecondFacingByTheOptionalLoop(Direction direction, int frame, bool flip)
        => Assert.Equal((frame, flip), AnimationSystem.GetCreatureIdleFrame(Info(standingCount: 2, optionalCount: 5), direction));

    [Fact]
    public void GetCreatureIdleFrame_ASingleFacingSpriteReusesItsFramesForEveryFacing()
    {
        var info = Info(standingCount: 3, optionalCount: 3, total: 11);

        foreach (var direction in Directions)
            Assert.Equal(8, AnimationSystem.GetCreatureIdleFrame(in info, direction).FrameIndex);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 3)]
    [InlineData(2, 5)]
    [InlineData(0, 4)]
    public void GetCreatureIdleFrame_MatchesTheWorldPickerAtTheFirstTick(byte standingCount, byte optionalCount)
    {
        var info = Info(standingCount, optionalCount);

        foreach (var direction in Directions)
            Assert.Equal(InWorld(in info, direction), AnimationSystem.GetCreatureIdleFrame(in info, direction));
    }

    [Theory]
    [InlineData(2, 0, 3)]
    [InlineData(3, 3, 9)]
    [InlineData(2, 5, 10)]
    [InlineData(0, 4, 1)]
    public void GetCreatureIdleFrame_NeverSelectsAFramePastTheEnd(byte standingCount, byte optionalCount, int total)
    {
        var info = Info(standingCount, optionalCount, total);

        foreach (var direction in Directions)
            Assert.InRange(AnimationSystem.GetCreatureIdleFrame(in info, direction).FrameIndex, 0, total - 1);
    }
}
