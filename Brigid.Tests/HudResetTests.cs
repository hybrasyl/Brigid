using Brigid.Collections;
using Brigid.Rendering;
using Brigid.ViewModel;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     Rules of the new HUD that a review removed and no test caught (BRIG-146, BRIG-148): the reset at a logout keeps one
///     character's hotbar from the next character, and the head of the player plate draws no weapon and no shield, which
///     can reach above the head.
/// </summary>
[Collection("WorldState")]
public sealed class HudResetTests : IDisposable
{
    public HudResetTests() => WorldState.ResetAll();

    public void Dispose() => WorldState.ResetAll();

    [Fact]
    public void AResetForgetsTheHotbarOfTheCharacterBefore()
    {
        WorldState.Hotbar.Load(["page 4", "0 0 skill 1 0 Assail"], "qa.example/Hybrasyl");

        WorldState.ResetAll();

        Assert.Null(WorldState.Hotbar.Get(0, 0));
        Assert.Equal(0, WorldState.Hotbar.Page);
    }

    [Fact]
    public void TheHeadDrawsNoWeaponAndNoShield()
    {
        var appearance = new AislingAppearance { Gender = default, WeaponSprite = 12, ShieldSprite = 7, HeadSprite = 3 };

        var head = AislingRenderer.HeadAppearance(in appearance);

        Assert.Equal(0, head.WeaponSprite);
        Assert.Equal(0, head.ShieldSprite);
        Assert.Equal(3, head.HeadSprite);
    }
}
