using Brigid.Collections;
using Chaos.DarkAges.Definitions;
using Xunit;
using static Brigid.Tests.DisplayUserPackets;

namespace Brigid.Tests;

// A dead aisling draws as a ghost at the ghost alpha, with the pale ghost face, whatever skin byte the server sends.
// Dead wins over transparent, as on the server, which sends the ghost body for a dead player even when the Hide flag is
// set; the invisible outline on top would stack a second alpha.
[Collection("WorldState")]
public sealed class GhostAislingTests
{
    private const uint ID = 0x7E57_0002;

    private static void Show(byte bodySprite, bool isHidden = false, byte bodyColor = 0)
        => WorldState.AddOrUpdateAisling(Display(ID, Body(bodySprite, isHidden, bodyColor)));

    [Theory]
    [InlineData(0x30)]
    [InlineData(0x40)]
    public void AddOrUpdateAisling_ADeadAislingIsAGhost(byte bodySprite)
    {
        try
        {
            Show(bodySprite);

            var entity = WorldState.GetEntity(ID)!;
            Assert.True(entity.IsDead);
            Assert.False(entity.IsTransparent);
            Assert.Equal(2, entity.Appearance!.Value.BodySpriteId);
        } finally
        {
            WorldState.RemoveEntity(ID);
        }
    }

    [Theory]
    [InlineData(0x30)]
    [InlineData(0x40)]
    public void AddOrUpdateAisling_ADeadAislingWithTheHideFlagIsStillAGhost(byte bodySprite)
    {
        try
        {
            Show(bodySprite, isHidden: true);

            var entity = WorldState.GetEntity(ID)!;
            Assert.True(entity.IsDead);
            Assert.False(entity.IsTransparent);
            Assert.Equal(2, entity.Appearance!.Value.BodySpriteId);
        } finally
        {
            WorldState.RemoveEntity(ID);
        }
    }

    [Theory]
    [InlineData(0x30, 0)]
    [InlineData(0x40, 0)]
    [InlineData(0x30, 4)]
    [InlineData(0x40, 9)]
    public void AddOrUpdateAisling_AGhostFaceUsesThePaleGhostPalette(byte bodySprite, byte sentSkinColor)
    {
        try
        {
            Show(bodySprite, bodyColor: sentSkinColor);

            Assert.Equal((int)BodyColor.LightBlue, WorldState.GetEntity(ID)!.Appearance!.Value.BodyColor);
        } finally
        {
            WorldState.RemoveEntity(ID);
        }
    }

    [Fact]
    public void AddOrUpdateAisling_ALivingAislingWithTheHideFlagIsTransparent()
    {
        try
        {
            Show(0x10, isHidden: true, bodyColor: 4);

            var entity = WorldState.GetEntity(ID)!;
            Assert.False(entity.IsDead);
            Assert.True(entity.IsTransparent);
            Assert.Equal(3, entity.Appearance!.Value.BodySpriteId);
            Assert.Equal(4, entity.Appearance!.Value.BodyColor);
        } finally
        {
            WorldState.RemoveEntity(ID);
        }
    }

    [Fact]
    public void AddOrUpdateAisling_DeathAndRevivalChangeTheStateBothWays()
    {
        try
        {
            Show(0x10, isHidden: true);
            var entity = WorldState.GetEntity(ID)!;

            Show(0x30, isHidden: true);
            Assert.True(entity.IsDead);
            Assert.False(entity.IsTransparent);

            Show(0x10, isHidden: true, bodyColor: 4);
            Assert.False(entity.IsDead);
            Assert.True(entity.IsTransparent);
            Assert.Equal(4, entity.Appearance!.Value.BodyColor);

            Show(0x10);
            Assert.False(entity.IsDead);
            Assert.False(entity.IsTransparent);
        } finally
        {
            WorldState.RemoveEntity(ID);
        }
    }
}
