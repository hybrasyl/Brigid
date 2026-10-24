using Brigid.Controls.Components;
using Brigid.Definitions;
using Xunit;

namespace Brigid.Tests;

// A screen can close a popup on a press outside it before the dispatch, and then swallow that press. Before, the world
// screen skipped the whole frame's dispatch, so a right press in the same frame did not walk, and a key in that frame was
// lost. SwallowPress skips only the one press.
[Collection("UITextBoxFocus")]
public sealed class SwallowPressTests
{
    private sealed class Pane : UIPanel
    {
        public readonly List<MouseButton> Presses = [];

        public Pane()
        {
            Width = 100;
            Height = 100;
            UpdateClipRect();
        }

        public override void OnMouseDown(MouseDownEvent e)
        {
            Presses.Add(e.Button);
            e.Handled = true;
        }
    }

    private static BufferedInputEvent Press(MouseButton button) => BufferedInputEvent.ForMouseButton(button, true, 10, 10, KeyModifiers.None);

    private static BufferedInputEvent Release(MouseButton button) => BufferedInputEvent.ForMouseButton(button, false, 10, 10, KeyModifiers.None);

    [Fact]
    public void ASwallowedPressIsSkippedAndTheOtherPressesDispatch()
    {
        using var dispatcher = new InputDispatcher(registerAsInstance: false);
        var pane = new Pane();
        BufferedInputEvent[] events = [Press(MouseButton.Right), Release(MouseButton.Right), Press(MouseButton.Left), Release(MouseButton.Left)];

        dispatcher.SwallowPress(2);
        dispatcher.DispatchEvents(pane, events, 0);

        Assert.Equal([MouseButton.Right], pane.Presses);
    }

    [Fact]
    public void ASwallowedPressIsForgottenAfterTheDispatch()
    {
        using var dispatcher = new InputDispatcher(registerAsInstance: false);
        var pane = new Pane();
        BufferedInputEvent[] events = [Press(MouseButton.Left), Release(MouseButton.Left)];

        dispatcher.SwallowPress(0);
        dispatcher.DispatchEvents(pane, events, 0);
        dispatcher.DispatchEvents(pane, events, 1000);

        Assert.Equal([MouseButton.Left], pane.Presses);
    }
}
