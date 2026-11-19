using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Screens;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Brigid.Tests;

// A held number key used its slot at each repeat of the OS: the buffer did not read the repeat flag, and the dispatcher
// forgets the keys that it dispatched at each frame. One long press on a potion drank several. The repeat flag reaches
// the key event now, and a slot key uses its slot once a press, on the new HUD and on the classic HUDs (Sabrael,
// 2026-11-19). Spacebar assail still takes the repeats.
[Collection("UITextBoxFocus")]
public sealed class SlotKeyRepeatTests
{
    private sealed class Root : UIPanel
    {
        public readonly List<bool> Repeats = [];
        public int Uses;

        public override void OnKeyDown(KeyDownEvent e)
        {
            Repeats.Add(e.IsRepeat);

            if ((WorldScreen.SlotHotkeyOf(e, true) is not null) && WorldScreen.UsesSlot(e))
                Uses++;
        }
    }

    private static BufferedInputEvent Down(Keys key, bool repeat) => BufferedInputEvent.ForKeyDown(key, KeyModifiers.None) with { IsRepeat = repeat };

    [Fact]
    public void AKeyHeldOverFiveFramesUsesItsSlotOnce()
    {
        using var dispatcher = new InputDispatcher(registerAsInstance: false);
        var root = new Root();

        dispatcher.DispatchEvents(root, [Down(Keys.D1, false)], 0);

        for (var frame = 1; frame < 5; frame++)
            dispatcher.DispatchEvents(root, [Down(Keys.D1, true)], frame * 16);

        Assert.Equal([false, true, true, true, true], root.Repeats);
        Assert.Equal(1, root.Uses);
    }

    //a press after a release is a new press
    [Fact]
    public void APressAfterAReleaseUsesTheSlotAgain()
    {
        using var dispatcher = new InputDispatcher(registerAsInstance: false);
        var root = new Root();

        dispatcher.DispatchEvents(root, [Down(Keys.D1, false), BufferedInputEvent.ForKeyUp(Keys.D1, KeyModifiers.None), Down(Keys.D1, false)], 0);

        Assert.Equal(2, root.Uses);
    }

    //one table decides which classic panels take the number keys, for the press and for its repeats
    [Theory]
    [InlineData(HudTab.Inventory, 3, WorldScreen.ClassicSlotKind.Item, 3)]
    [InlineData(HudTab.Skills, 3, WorldScreen.ClassicSlotKind.Skill, 3)]
    [InlineData(HudTab.SkillsAlt, 3, WorldScreen.ClassicSlotKind.Skill, 39)]
    [InlineData(HudTab.Spells, 12, WorldScreen.ClassicSlotKind.Spell, 12)]
    [InlineData(HudTab.SpellsAlt, 1, WorldScreen.ClassicSlotKind.Spell, 37)]
    [InlineData(HudTab.Tools, 6, WorldScreen.ClassicSlotKind.Skill, 78)]
    [InlineData(HudTab.Tools, 7, WorldScreen.ClassicSlotKind.Spell, 73)]
    [InlineData(HudTab.Chat, 2, WorldScreen.ClassicSlotKind.Macro, 2)]
    [InlineData(HudTab.MessageHistory, 2, WorldScreen.ClassicSlotKind.Macro, 2)]
    public void AClassicPanelGivesTheSlotOfANumberKey(HudTab tab, int slot, object kind, int bookSlot)
        => Assert.Equal(((WorldScreen.ClassicSlotKind)kind, (byte)bookSlot), WorldScreen.ClassicSlotUseOf(tab, slot));

    [Theory]
    [InlineData(HudTab.Stats)]
    [InlineData(HudTab.ExtendedStats)]
    public void AClassicPanelWithNoSlotsTakesNoNumberKey(HudTab tab) => Assert.Null(WorldScreen.ClassicSlotUseOf(tab, 1));

    [Fact]
    public void AKeyEventForgetsTheRepeatAtAReset()
    {
        var e = new KeyDownEvent { Key = Keys.D1, IsRepeat = true };

        e.Reset();

        Assert.False(e.IsRepeat);
    }
}
