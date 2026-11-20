using Brigid.Definitions;
using Brigid.Systems.Keybinds;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Brigid.Tests;

// A held A, S, D, F, G or H opened and closed its window of the new HUD at the repeat rate of the OS, and a held Q, W, E
// or T did the same to its window: only the slot keys read the repeat flag. The rule is on the command now: a key repeat
// matches only a command that repeats, so a toggle fires once a press and movement, assail, the scrolls and the text
// editing keys still repeat. The tests read the defaults and change no binding, so they run beside the other tests.
public sealed class KeybindRepeatTests
{
    private static KeyDownEvent Press(Keys key, bool repeat, KeyModifiers mods = KeyModifiers.None) => new() { Key = key, Modifiers = mods, IsRepeat = repeat };

    [Theory]
    [InlineData(CommandId.World_TabInventory, Keys.A)]
    [InlineData(CommandId.World_TabStats, Keys.G)]
    [InlineData(CommandId.World_TogglePauseMenu, Keys.Q)]
    [InlineData(CommandId.World_ToggleBoard, Keys.W)]
    [InlineData(CommandId.World_TownMap, Keys.T)]
    public void ARepeatOfAToggleDoesNotMatch(CommandId command, Keys key)
    {
        Assert.True(Keybinds.Matches(Press(key, false), command));
        Assert.False(Keybinds.Matches(Press(key, true), command));
    }

    [Fact]
    public void ARepeatOfAShiftedToggleDoesNotMatch()
    {
        Assert.True(Keybinds.Matches(Press(Keys.G, false, KeyModifiers.Shift), CommandId.World_TabStatsExtended));
        Assert.False(Keybinds.Matches(Press(Keys.G, true, KeyModifiers.Shift), CommandId.World_TabStatsExtended));
    }

    [Theory]
    [InlineData(CommandId.Move_Down, Keys.Down)]
    [InlineData(CommandId.Move_Right, Keys.V)]
    [InlineData(CommandId.World_Assail, Keys.Space)]
    [InlineData(CommandId.World_TabMapZoomIn, Keys.PageUp)]
    public void ARepeatOfARepeatingCommandMatches(CommandId command, Keys key)
    {
        Assert.True(Keybinds.Matches(Press(key, true), command));
    }

    [Fact]
    public void ARepeatOfAnEditingKeyResolves()
    {
        Assert.Equal(CommandId.Editor_Undo, Keybinds.Resolve(Press(Keys.Z, true, KeyModifiers.Ctrl | KeyModifiers.Meta), KeybindContext.TextEditing));
    }

    //the commands that repeat; a new command does not repeat unless it is added here
    [Fact]
    public void TheRepeatingCommandsAreMovementAssailScrollsAndText()
    {
        CommandId[] expected =
        [
            CommandId.Editor_Copy, CommandId.Editor_Cut, CommandId.Editor_Paste, CommandId.Editor_Undo, CommandId.Editor_Redo,
            CommandId.Editor_SelectAll, CommandId.Editor_LineStart, CommandId.Editor_LineEnd, CommandId.Read_Copy, CommandId.Read_SelectAll,
            CommandId.World_Assail, CommandId.World_TabMapZoomIn, CommandId.World_TabMapZoomOut, CommandId.World_PickupItem,
            CommandId.World_ChatScrollUp, CommandId.World_ChatScrollDown, CommandId.Move_Up, CommandId.Move_Down, CommandId.Move_Left,
            CommandId.Move_Right
        ];

        var repeating = Keybinds.Defaults.Where(pair => pair.Value.Repeats).Select(pair => pair.Key).Order().ToArray();

        Assert.Equal(expected.Order().ToArray(), repeating);
    }
}
