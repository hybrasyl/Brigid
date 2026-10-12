using Brigid.Controls.Components;
using Xunit;

namespace Brigid.Tests;

// While a textbox holds the dispatcher's explicit focus, clicks outside its panel are consumed. Hiding the login or
// create panel unfocused its textboxes but left the explicit focus on one of them, and ContainsPoint ignores
// visibility, so the hidden panel's area kept the lobby's other buttons from taking a click until a key was pressed.
[Collection("UITextBoxFocus")]
public class StaleTextBoxFocusTests
{
    private static (InputDispatcher Dispatcher, UIPanel Panel, UITextBox Box) FocusedLoginBox()
    {
        var dispatcher = new InputDispatcher();
        var panel = new UIPanel { X = 100, Y = 100, Width = 200, Height = 100 };
        var box = new UITextBox { X = 10, Y = 10, Width = 100, Height = 14 };
        panel.AddChild(box);
        box.IsFocused = true;

        return (dispatcher, panel, box);
    }

    [Fact]
    public void IsMouseBlocked_BlocksAClickOutsideTheFocusedPanel()
    {
        var (dispatcher, _, box) = FocusedLoginBox();

        //ContainsPoint uses the clip rect from the last Draw; with no Draw in a test, every point is outside the panel
        Assert.Same(box, dispatcher.ExplicitFocus);
        Assert.True(dispatcher.IsMouseBlocked(10, 10));
    }

    [Fact]
    public void IsFocused_LosingFocusReleasesTheExplicitFocus()
    {
        var (dispatcher, _, box) = FocusedLoginBox();

        box.IsFocused = false;

        Assert.Null(dispatcher.ExplicitFocus);
        Assert.False(dispatcher.IsMouseBlocked(10, 10));
    }

    [Fact]
    public void IsMouseBlocked_ClearsAnExplicitFocusThatIsHidden()
    {
        var (dispatcher, panel, box) = FocusedLoginBox();

        //a popup can restore a stashed focus without the box gaining focus again
        dispatcher.SetExplicitFocus(box);
        panel.Visible = false;

        Assert.False(dispatcher.IsMouseBlocked(10, 10));
        Assert.Null(dispatcher.ExplicitFocus);
    }
}
