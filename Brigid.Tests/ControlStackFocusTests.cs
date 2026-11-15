using Brigid.Controls.Components;
using Brigid.Definitions;
using Xunit;

namespace Brigid.Tests;

// BRIG-175. A Hide on a panel that is not on the control stack, or the removal of a lower panel, moved the explicit
// focus from a focused text box in the top panel to the panel. The box kept IsFocused, so no key reached it, and a
// click on it did not link it to the dispatcher again.
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

    [Fact]
    public void RemoveControl_APanelThatIsNotOnTheStackKeepsTheTextFocus()
    {
        using var dispatcher = new InputDispatcher(registerAsInstance: false);
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
        using var dispatcher = new InputDispatcher(registerAsInstance: false) { ChatInputTextBox = new UITextBox() };
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
        using var dispatcher = new InputDispatcher(registerAsInstance: false);
        var lower = new UIPanel();
        var (notepad, box) = PanelWithBox();

        dispatcher.PushControl(lower);
        dispatcher.PushControl(notepad);
        box.IsFocused = true;

        dispatcher.RemoveControl(lower);

        Assert.Same(box, dispatcher.ExplicitFocus);
    }

    [Fact]
    public void RemoveControl_TheTopPanelStillGivesTheFocusToTheNewTop()
    {
        //the world screen always sets the chat box, and the hand-over to the new top compares the focus with it
        using var dispatcher = new InputDispatcher(registerAsInstance: false) { ChatInputTextBox = new UITextBox() };
        var lower = new UIPanel();
        var top = new UIPanel();

        dispatcher.PushControl(lower);
        dispatcher.PushControl(top);

        dispatcher.RemoveControl(top);

        Assert.Same(lower, dispatcher.ExplicitFocus);
    }

    [Fact]
    public void OnMouseDown_AStaleFocusedBoxTakesTheExplicitFocusAgain()
    {
        using var dispatcher = new InputDispatcher(registerAsInstance: false);
        var (_, box) = PanelWithBox();
        var focusedEvents = 0;
        box.OnFocused += _ => focusedEvents++;

        box.IsFocused = true;
        dispatcher.ClearExplicitFocus();

        box.OnMouseDown(new MouseDownEvent { Button = MouseButton.Left, ScreenX = box.ScreenX + 1, ScreenY = box.ScreenY + 1 });

        Assert.Same(box, dispatcher.ExplicitFocus);
        Assert.Equal(0, focusedEvents);
    }

    [Fact]
    public void OnMouseDown_AnUnfocusedBoxRaisesOnFocusedOnce()
    {
        using var dispatcher = new InputDispatcher(registerAsInstance: false);
        var (_, box) = PanelWithBox();
        var focusedEvents = 0;
        box.OnFocused += _ => focusedEvents++;

        box.OnMouseDown(new MouseDownEvent { Button = MouseButton.Left, ScreenX = box.ScreenX + 1, ScreenY = box.ScreenY + 1 });

        Assert.True(box.IsFocused);
        Assert.Same(box, dispatcher.ExplicitFocus);
        Assert.Equal(1, focusedEvents);
    }
}
