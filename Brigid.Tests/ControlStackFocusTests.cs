using Brigid.Controls.Components;
using Xunit;

namespace Brigid.Tests;

// A Hide on a panel that is not on the control stack, or the removal of a lower panel, moved the explicit focus from a
// focused text box in the top panel to the panel. The box kept IsFocused, so no key reached it. A lower panel that goes
// away now changes no focus in the top panel, and gives its stashed focus to the panel above it (BRIG-175).
[Collection("UITextBoxFocus")]
public class ControlStackFocusTests
{
    private static (UIPanel Panel, UITextBox Box) PanelWithBox()
    {
        var panel = new UIPanel { X = 100, Y = 100, Width = 200, Height = 100 };
        var box = new UITextBox { X = 10, Y = 10, Width = 100, Height = 14 };
        panel.AddChild(box);

        return (panel, box);
    }

    //the world screen always sets the chat box, and the hand-over to the new top compares the focus with it
    private static InputDispatcher WorldDispatcher() => new(registerAsInstance: false) { ChatInputTextBox = new UITextBox() };

    [Fact]
    public void RemoveControl_APanelThatIsNotOnTheStackKeepsTheTextFocus()
    {
        using var dispatcher = WorldDispatcher();
        var (notepad, box) = PanelWithBox();
        var hidden = new UIPanel { Visible = false };

        dispatcher.PushControl(notepad);
        box.IsFocused = true;

        dispatcher.RemoveControl(hidden);

        Assert.Same(box, dispatcher.ExplicitFocus);
        Assert.Same(notepad, dispatcher.TopControl);
    }

    [Fact]
    public void RemoveControl_APanelThatIsNotOnTheStackChangesNoFocus()
    {
        using var dispatcher = WorldDispatcher();
        var open = new UIPanel();
        var hidden = new UIPanel { Visible = false };

        dispatcher.PushControl(open);

        dispatcher.RemoveControl(hidden);

        Assert.Null(dispatcher.ExplicitFocus);
        Assert.Equal(1, dispatcher.ControlStackCount);
    }

    //the player clicked a box in the lower panel while the top panel was open, and the server closed the top panel: the new
    //top took the focus from the box, which kept its caret, and no key reached it
    [Fact]
    public void RemoveControl_TheTopPanelLeavesTheFocusOfABoxBelowIt()
    {
        using var dispatcher = WorldDispatcher();
        var (options, macroBox) = PanelWithBox();
        var dialog = new UIPanel();

        dispatcher.PushControl(options);
        dispatcher.PushControl(dialog);
        macroBox.IsFocused = true;

        dispatcher.RemoveControl(dialog);

        Assert.Same(macroBox, dispatcher.ExplicitFocus);
        Assert.True(macroBox.IsFocused);
    }

    //the stash of the top panel does not take the focus from a box that the player focused after the push
    [Fact]
    public void RemoveControl_TheStashDoesNotTakeTheFocusFromABoxFocusedLater()
    {
        using var dispatcher = WorldDispatcher();
        var (options, macroBox) = PanelWithBox();
        var friendBox = new UITextBox { X = 10, Y = 40, Width = 100, Height = 14 };
        options.AddChild(friendBox);
        var dialog = new UIPanel();

        dispatcher.PushControl(options);
        macroBox.IsFocused = true;
        dispatcher.PushControl(dialog);
        friendBox.IsFocused = true;

        dispatcher.RemoveControl(dialog);

        Assert.Same(friendBox, dispatcher.ExplicitFocus);
        Assert.False(macroBox.IsFocused);
    }

    [Fact]
    public void RemoveControl_ALowerPanelKeepsTheTextFocusInTheTopPanel()
    {
        using var dispatcher = WorldDispatcher();
        var lower = new UIPanel();
        var (notepad, box) = PanelWithBox();

        dispatcher.PushControl(lower);
        dispatcher.PushControl(notepad);
        box.IsFocused = true;

        dispatcher.RemoveControl(lower);

        Assert.Same(box, dispatcher.ExplicitFocus);
    }

    //the lower panel's stash is a box below both panels: it must not take the focus while the top panel is open, and it
    //comes back when the top panel closes
    [Fact]
    public void RemoveControl_ALowerPanelGivesItsStashToThePanelAbove()
    {
        using var dispatcher = WorldDispatcher();
        var (_, rootBox) = PanelWithBox();
        var lower = new UIPanel();
        var top = new UIPanel();

        rootBox.IsFocused = true;
        dispatcher.PushControl(lower);
        dispatcher.PushControl(top);

        dispatcher.RemoveControl(lower);

        Assert.NotSame(rootBox, dispatcher.ExplicitFocus);

        dispatcher.RemoveControl(top);

        Assert.Same(rootBox, dispatcher.ExplicitFocus);
    }

    //the lobby has no chat box: the new top gets no focus, because a panel that holds the explicit focus blocks each
    //click outside it
    [Fact]
    public void RemoveControl_WithNoChatBoxGivesNoFocusAndKeepsTheMouseFree()
    {
        using var dispatcher = new InputDispatcher(registerAsInstance: false);
        var lower = new UIPanel();
        var top = new UIPanel();

        dispatcher.PushControl(lower);
        dispatcher.PushControl(top);

        dispatcher.RemoveControl(top);

        Assert.Null(dispatcher.ExplicitFocus);
        Assert.False(dispatcher.IsMouseBlocked(10, 10));
    }

    //after a logout the world screen's chat box goes away, and the lobby keeps its rule (the dispatcher lives for the run)
    [Fact]
    public void ReleaseChatInput_TheLobbyAfterAWorldVisitGivesNoFocus()
    {
        using var dispatcher = WorldDispatcher();
        var chat = dispatcher.ChatInputTextBox!;
        var lower = new UIPanel();
        var top = new UIPanel();

        dispatcher.ReleaseChatInput(chat);
        dispatcher.PushControl(lower);
        dispatcher.PushControl(top);
        dispatcher.RemoveControl(top);

        Assert.Null(dispatcher.ChatInputTextBox);
        Assert.Null(dispatcher.ExplicitFocus);
    }

    //a box that is not the registered one does not release the chat input
    [Fact]
    public void ReleaseChatInput_AnotherBoxKeepsTheChatInput()
    {
        using var dispatcher = WorldDispatcher();
        var chat = dispatcher.ChatInputTextBox!;

        dispatcher.ReleaseChatInput(new UITextBox());

        Assert.Same(chat, dispatcher.ChatInputTextBox);
    }

    //an open drop-down list keeps the text focus and has no stash: the lower panel's stash goes past it to the panel above
    [Fact]
    public void RemoveControl_AStashPassesUpPastAPanelThatKeepsTheTextFocus()
    {
        using var dispatcher = WorldDispatcher();
        var (_, rootBox) = PanelWithBox();
        var (lower, lowerBox) = PanelWithBox();
        var dropDown = new UIPanel();
        var top = new UIPanel();

        rootBox.IsFocused = true;
        dispatcher.PushControl(lower);
        lowerBox.IsFocused = true;
        dispatcher.PushControl(dropDown, keepTextFocus: true);
        dispatcher.PushControl(top);

        dispatcher.RemoveControl(lower);
        dispatcher.RemoveControl(dropDown);
        dispatcher.RemoveControl(top);

        Assert.Same(rootBox, dispatcher.ExplicitFocus);
    }

    [Fact]
    public void RemoveControl_TheChatBoxKeepsItsFocus()
    {
        using var dispatcher = WorldDispatcher();
        var chat = dispatcher.ChatInputTextBox!;
        var lower = new UIPanel();
        var top = new UIPanel();

        dispatcher.PushControl(lower);
        dispatcher.PushControl(top);
        dispatcher.SetExplicitFocus(chat);

        dispatcher.RemoveControl(top);

        Assert.Same(chat, dispatcher.ExplicitFocus);
    }

    [Fact]
    public void RemoveControl_TheTopPanelStillGivesTheFocusToTheNewTop()
    {
        using var dispatcher = WorldDispatcher();
        var lower = new UIPanel();
        var top = new UIPanel();

        dispatcher.PushControl(lower);
        dispatcher.PushControl(top);

        dispatcher.RemoveControl(top);

        Assert.Same(lower, dispatcher.ExplicitFocus);
    }
}
