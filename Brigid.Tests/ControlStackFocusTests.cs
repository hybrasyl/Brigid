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
