using Brigid.Collections;
using Brigid.Models;
using Brigid.Rendering;
using Brigid.Rendering.Definitions;
using Brigid.Definitions;
using Brigid.Systems;
using Chaos.DarkAges.Definitions;
using Xunit;
using static Brigid.Tests.DisplayUserPackets;

namespace Brigid.Tests;

// An aisling in monster form draws as a creature sprite, so it follows creature animation rules: its idle cycle ticks
// even without paperdoll idle frames. Entering or leaving the form restarts the idle cycle, so a tick from one form
// cannot select a frame that the other form does not have.
//WorldState is static, so the tests that change it run alone
[CollectionDefinition("WorldState", DisableParallelization = true)]
public sealed class WorldStateCollection;

[Collection("WorldState")]
public sealed class MonsterFormAnimationTests
{
    private static WorldEntity MonsterFormAisling()
        => new() { Id = 1, Type = ClientEntityType.Aisling, Appearance = null, SpriteId = 40 };

    private static WorldEntity PaperdollAisling()
        => new() { Id = 2, Type = ClientEntityType.Aisling, Appearance = new AislingAppearance { Gender = default }, SpriteId = 0 };

    private static WorldEntity Creature() => new() { Id = 3, Type = ClientEntityType.Creature, SpriteId = 40 };

    [Fact]
    public void Advance_TicksTheIdleCycleOfAMonsterFormAisling()
    {
        var entity = MonsterFormAisling();

        AnimationSystem.Advance(entity, 1000);

        Assert.True(entity.IdleAnimTick > 0);
    }

    [Fact]
    public void Advance_TicksTheIdleCycleOfACreature()
    {
        var entity = Creature();

        AnimationSystem.Advance(entity, 1000);

        Assert.True(entity.IdleAnimTick > 0);
    }

    [Fact]
    public void Advance_LeavesAGroundItemStill()
    {
        var entity = new WorldEntity { Type = ClientEntityType.GroundItem, SpriteId = 40 };

        AnimationSystem.Advance(entity, 1000);

        Assert.False(entity.IsRenderedAsCreatureSprite);
        Assert.Equal(0, entity.IdleAnimTick);
    }

    [Fact]
    public void Advance_LeavesAPaperdollAislingWithoutIdleFramesStill()
    {
        var entity = PaperdollAisling();

        AnimationSystem.Advance(entity, 1000);

        Assert.Equal(0, entity.IdleAnimTick);
    }

    //walk 0-7, attack 8-13 (3 per facing), standing 14+
    private static CreatureAnimInfo Info()
        => new(
            WalkFrameIndex: 0,
            WalkFrameCount: 4,
            AttackFrameIndex: 8,
            AttackFrameCount: 3,
            Attack2StartIndex: 0,
            Attack2FrameCount: 0,
            Attack3StartIndex: 0,
            Attack3FrameCount: 0,
            StandingFrameIndex: 14,
            StandingFrameCount: 2,
            OptionalAnimationFrameCount: 2,
            OptionalAnimationProbability: 0,
            AnimationIntervalMs: 300,
            TotalFrameCount: 20);

    [Fact]
    public void StartCreatureBodyAnimation_PlaysTheCreatureAttackForAMonsterFormAisling()
    {
        var entity = MonsterFormAisling();
        var info = Info();

        AnimationSystem.StartCreatureBodyAnimation(entity, BodyAnimation.Assail, 0, in info);

        Assert.Equal(EntityAnimState.BodyAnim, entity.AnimState);
        Assert.Equal(3, entity.AnimFrameCount);
        Assert.InRange(AnimationSystem.GetCreatureFrame(entity, in info).FrameIndex, 8, 13);
    }

    [Fact]
    public void StartCreatureBodyAnimation_PlaysTheCreatureAttackForACreature()
    {
        var entity = Creature();
        var info = Info();

        AnimationSystem.StartCreatureBodyAnimation(entity, BodyAnimation.Assail, 0, in info);

        Assert.Equal(3, entity.AnimFrameCount);
        Assert.InRange(AnimationSystem.GetCreatureFrame(entity, in info).FrameIndex, 8, 13);
    }

    [Fact]
    public void StartCreatureBodyAnimation_IgnoresAnEmote()
    {
        var entity = MonsterFormAisling();
        var info = Info();

        AnimationSystem.StartCreatureBodyAnimation(entity, BodyAnimation.Smile, 0, in info);

        Assert.Equal(EntityAnimState.Idle, entity.AnimState);
    }

    [Fact]
    public void StartBodyAnimation_KeepsThePaperdollFrameCountsForAPaperdollAisling()
    {
        var entity = PaperdollAisling();

        AnimationSystem.StartBodyAnimation(entity, BodyAnimation.Assail, 0);

        Assert.Equal(EntityAnimState.BodyAnim, entity.AnimState);
        Assert.NotEqual(3, entity.AnimFrameCount);
    }

    [Fact]
    public void ResetIdleCycle_ClearsEveryIdleField()
    {
        var entity = Creature();
        entity.IdleAnimTick = 9;
        entity.IdleAnimElapsedMs = 120;
        entity.IdleCycleEndsAt = 14;
        entity.IdleOptionalActive = true;
        entity.IdleFrameIntervalMs = 250;
        entity.IdleAnimFrameCount = 4;

        AnimationSystem.ResetIdleCycle(entity);

        Assert.Equal(0, entity.IdleAnimTick);
        Assert.Equal(0f, entity.IdleAnimElapsedMs);
        Assert.Equal(0, entity.IdleCycleEndsAt);
        Assert.False(entity.IdleOptionalActive);
        Assert.Equal(0f, entity.IdleFrameIntervalMs);
        Assert.Equal(0, entity.IdleAnimFrameCount);
    }

    [Fact]
    public void AddOrUpdateAisling_RestartsTheIdleCycleWhenTheFormChanges()
    {
        const uint ID = 0x7E57_0001;

        try
        {
            WorldState.AddOrUpdateAisling(Display(ID, Body()));

            var entity = WorldState.GetEntity(ID)!;
            entity.IdleAnimTick = 7;
            entity.IdleCycleEndsAt = 12;
            entity.IdleFrameIntervalMs = 300;

            WorldState.AddOrUpdateAisling(Display(ID, Monster(40)));

            Assert.True(entity.IsRenderedAsCreatureSprite);
            Assert.Equal(0, entity.IdleAnimTick);
            Assert.Equal(0, entity.IdleCycleEndsAt);
            Assert.Equal(0f, entity.IdleFrameIntervalMs);

            entity.IdleAnimTick = 5;
            WorldState.AddOrUpdateAisling(Display(ID, Monster(41)));

            //same form: the cycle keeps running
            Assert.Equal(5, entity.IdleAnimTick);
        } finally
        {
            WorldState.RemoveEntity(ID);
        }
    }
}
