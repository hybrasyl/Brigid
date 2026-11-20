using Brigid.Controls.Components;
using Brigid.Definitions;
using Xunit;

namespace Brigid.Tests;

// BRIG-151: a double click is two clicks of the same button on the same element. A right click and then a left click
// gave a left double click before, so a right click on a slot of the Equipment window and a quick left click took the
// item off.
[Collection("UITextBoxFocus")]
public class DoubleClickTests : IDisposable
{
    private readonly List<InputDispatcher> Dispatchers = [];

    public void Dispose()
    {
        foreach (var dispatcher in Dispatchers)
            dispatcher.Dispose();
    }

    private sealed class Panel : UIPanel
    {
        public readonly List<MouseButton> DoubleClicks = [];

        public Panel()
        {
            Width = 50;
            Height = 50;
        }

        public Panel Laid()
        {
            UpdateClipRect();

            return this;
        }

        public override void OnClick(ClickEvent e) => e.Handled = true;

        public override void OnDoubleClick(DoubleClickEvent e)
        {
            DoubleClicks.Add(e.Button);
            e.Handled = true;
        }
    }

    private static void Click(InputDispatcher dispatcher, UIPanel root, MouseButton button, float ms)
    {
        dispatcher.DispatchMouseButton(root, 10, 10, ms, KeyModifiers.None, button, isPress: true);
        dispatcher.DispatchMouseButton(root, 10, 10, ms, KeyModifiers.None, button, isPress: false);
    }

    private (InputDispatcher Dispatcher, Panel Root) NewRoot()
    {
        var dispatcher = new InputDispatcher(registerAsInstance: false);
        Dispatchers.Add(dispatcher);

        return (dispatcher, new Panel().Laid());
    }

    [Fact]
    public void TwoLeftClicksAreADoubleClick()
    {
        var (dispatcher, root) = NewRoot();

        Click(dispatcher, root, MouseButton.Left, 0);
        Click(dispatcher, root, MouseButton.Left, 100);

        Assert.Equal([MouseButton.Left], root.DoubleClicks);
    }

    [Theory]
    [InlineData(MouseButton.Right, MouseButton.Left)]
    [InlineData(MouseButton.Left, MouseButton.Right)]
    public void TwoClicksOfDifferentButtonsAreNoDoubleClick(MouseButton first, MouseButton second)
    {
        var (dispatcher, root) = NewRoot();

        Click(dispatcher, root, first, 0);
        Click(dispatcher, root, second, 100);

        Assert.Empty(root.DoubleClicks);
    }

    //the second click starts a new pair with its own button
    [Fact]
    public void ARightClickThenTwoLeftClicksIsOneLeftDoubleClick()
    {
        var (dispatcher, root) = NewRoot();

        Click(dispatcher, root, MouseButton.Right, 0);
        Click(dispatcher, root, MouseButton.Left, 100);
        Click(dispatcher, root, MouseButton.Left, 200);

        Assert.Equal([MouseButton.Left], root.DoubleClicks);
    }
}
