using Brigid.Controls.Components;
using Brigid.Models;
using Brigid.Rendering.Definitions;
using Brigid.Screens;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     BRIG-111 item 7: two PR #60 fixes in the world input, as pure rules. J3: a right-button press on a creature or
///     another aisling is the first half of the double right-click that pursues it, and does not walk. J13: F1 (help)
///     works while the NPC window is the top popup, and any other popup on top blocks it.
/// </summary>
public sealed class WorldInputRuleTests
{
    private const uint PLAYER_ID = 7;

    [Theory]
    [InlineData(ClientEntityType.Creature, false, 20u, true)]
    [InlineData(ClientEntityType.Aisling, false, 20u, true)]
    [InlineData(ClientEntityType.Aisling, true, 20u, false)]
    [InlineData(ClientEntityType.Aisling, false, PLAYER_ID, false)]
    [InlineData(ClientEntityType.GroundItem, false, 20u, false)]
    public void APressOnACreatureOrAnotherAislingIsADoubleClickTarget(
        ClientEntityType type,
        bool hidden,
        uint id,
        bool expected)
    {
        var entity = new WorldEntity { Type = type, IsHidden = hidden, Id = id };

        Assert.Equal(expected, WorldScreen.IsDoubleClickTarget(entity, PLAYER_ID));
    }

    [Fact]
    public void APressOnTheGroundIsNoDoubleClickTarget()
        => Assert.False(WorldScreen.IsDoubleClickTarget(null, PLAYER_ID));

    [Fact]
    public void HelpPassesWithNoPopupOrUnderTheNpcWindowOnly()
    {
        var npcSession = new UIPanel { Name = "npc" };
        var other = new UIPanel { Name = "other" };

        Assert.True(WorldScreen.HelpPassesTheStack(0, null, npcSession));
        Assert.True(WorldScreen.HelpPassesTheStack(1, npcSession, npcSession));
        Assert.True(WorldScreen.HelpPassesTheStack(2, npcSession, npcSession));
        Assert.False(WorldScreen.HelpPassesTheStack(1, other, npcSession));
        Assert.False(WorldScreen.HelpPassesTheStack(2, other, npcSession));
        Assert.False(WorldScreen.HelpPassesTheStack(1, null, null));
    }
}
