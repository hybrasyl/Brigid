using Brigid.Models;
using Brigid.Rendering.Definitions;
using Brigid.Systems;
using Chaos.Geometry.Abstractions.Definitions;
using Microsoft.Xna.Framework;
using Xunit;

namespace Brigid.Tests;

// A creature with no sprite (sprite id 0) has no walk frames. When the server moves it, its walk snaps to the destination
// tile instead of sliding for the length of the human walk. The local player and a paperdoll aisling keep the human walk.
public class WalkAnimationTests
{
    [Fact]
    public void StartWalk_ACreatureWithNoSpriteSnapsToTheDestination()
    {
        var entity = new WorldEntity { Type = ClientEntityType.Creature, SpriteId = 0 };

        AnimationSystem.StartWalk(entity, Direction.Right, isCreature: true, walkFrameOverride: null);
        AnimationSystem.Advance(entity, 1);

        Assert.Equal(0, entity.AnimFrameCount);
        Assert.Equal(Vector2.Zero, entity.VisualOffset);
    }

    [Fact]
    public void StartWalk_ACreatureWithWalkFramesSlides()
    {
        var entity = new WorldEntity { Type = ClientEntityType.Creature, SpriteId = 40 };

        AnimationSystem.StartWalk(entity, Direction.Right, isCreature: true, walkFrameOverride: 4);
        AnimationSystem.Advance(entity, 1);

        Assert.Equal(4, entity.AnimFrameCount);
        Assert.NotEqual(Vector2.Zero, entity.VisualOffset);
    }

    [Fact]
    public void StartWalk_TheLocalPlayerWithNoSpriteKeepsTheHumanWalkLength()
    {
        //the walk length paces the local player's walk input; a 75 ms walk would outrun the server's walk limit
        var entity = new WorldEntity { Type = ClientEntityType.Aisling, SpriteId = 40 };

        AnimationSystem.StartWalk(entity, Direction.Right, isCreature: true, isLocalPlayer: true, walkFrameOverride: null);

        Assert.Equal(4, entity.AnimFrameCount);
    }

    [Fact]
    public void StartWalk_APaperdollAislingUsesTheHumanWalk()
    {
        var entity = new WorldEntity { Type = ClientEntityType.Aisling };

        AnimationSystem.StartWalk(entity, Direction.Right);

        Assert.Equal(4, entity.AnimFrameCount);
    }
}
