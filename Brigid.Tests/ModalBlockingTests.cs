using Brigid.Controls.Components;
using Brigid.Definitions;
using Xunit;

namespace Brigid.Tests;

// BRIG-107 phase 4: with EnforceModals (the lobby), the topmost visible modal on the control stack has the mouse. A
// mouse event outside it, and outside the stack panels above it, is consumed. A capture that started inside it keeps
// its mouse-up, and a blocked mouse-up still ends a capture that started outside it.
[Collection("UITextBoxFocus")]
public class ModalBlockingTests
{
    //ContainsPoint uses the clip rect from the last Draw; a test sets it from the bounds without a Draw
    private sealed class Panel : UIPanel
    {
        public int MouseUps;
        public int Clicks;
        public int DragCancels;
        public bool StartsDrag;

        public Panel(int x, int y, int width, int height, bool modal = false)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            IsModal = modal;
            UsesControlStack = true;
        }

        public Panel Laid()
        {
            UpdateClipRect();

            foreach (var child in Children)
                if (child is Panel panel)
                    panel.Laid();

            return this;
        }

        public override void OnMouseUp(MouseUpEvent e)
        {
            MouseUps++;
            e.Handled = true;
        }

        public override void OnClick(ClickEvent e)
        {
            Clicks++;
            e.Handled = true;
        }

        public override void OnDragStart(DragStartEvent e)
        {
            if (StartsDrag)
                e.Payload = this;
        }

        public override void OnDragCancel() => DragCancels++;
    }

    private static InputDispatcher NewDispatcher() => new(registerAsInstance: false) { EnforceModals = true };

    private static void Press(InputDispatcher dispatcher, UIPanel root, int x, int y)
        => dispatcher.DispatchMouseButton(root, x, y, 0, KeyModifiers.None, MouseButton.Left, isPress: true);

    private static void Release(InputDispatcher dispatcher, UIPanel root, int x, int y)
        => dispatcher.DispatchMouseButton(root, x, y, 0, KeyModifiers.None, MouseButton.Left, isPress: false);

    [Fact]
    public void WithNoModal_NothingIsBlocked()
    {
        using var dispatcher = NewDispatcher();
        dispatcher.PushControl(new Panel(100, 100, 50, 50).Laid());

        Assert.Null(dispatcher.TopModal);
        Assert.False(dispatcher.IsMouseBlocked(10, 10));
    }

    [Fact]
    public void AModalBlocksTheMouseOutsideIt_AndNotInsideIt()
    {
        using var dispatcher = NewDispatcher();
        var modal = new Panel(100, 100, 50, 50, modal: true).Laid();
        dispatcher.PushControl(modal);

        Assert.Same(modal, dispatcher.TopModal);
        Assert.True(dispatcher.IsMouseBlocked(10, 10));
        Assert.False(dispatcher.IsMouseBlocked(120, 120));
    }

    [Fact]
    public void WithoutEnforceModals_AModalBlocksNothing_ButIsStillTheTopModal()
    {
        using var dispatcher = NewDispatcher();
        dispatcher.EnforceModals = false;
        var modal = new Panel(100, 100, 50, 50, modal: true).Laid();
        dispatcher.PushControl(modal);

        Assert.Same(modal, dispatcher.TopModal);
        Assert.False(dispatcher.IsMouseBlocked(10, 10));
    }

    [Fact]
    public void Clear_TurnsEnforceModalsOff()
    {
        using var dispatcher = NewDispatcher();

        dispatcher.Clear();

        Assert.False(dispatcher.EnforceModals);
    }

    [Fact]
    public void AHiddenModal_BlocksNothing()
    {
        using var dispatcher = NewDispatcher();
        var modal = new Panel(100, 100, 50, 50, modal: true).Laid();
        dispatcher.PushControl(modal);
        modal.Visible = false;

        Assert.Null(dispatcher.TopModal);
        Assert.False(dispatcher.IsMouseBlocked(10, 10));
    }

    [Fact]
    public void AModalInAHiddenParent_BlocksNothing()
    {
        using var dispatcher = NewDispatcher();
        var parent = new UIPanel { Visible = false };
        var modal = new Panel(100, 100, 50, 50, modal: true).Laid();
        parent.AddChild(modal);
        dispatcher.PushControl(modal);

        Assert.Null(dispatcher.TopModal);
    }

    [Fact]
    public void AModalThatIsTheChildOfAPanelOffTheStack_IsTheTopModal()
    {
        using var dispatcher = NewDispatcher();
        var menu = new UIPanel { Width = 640, Height = 480 };
        var story = new Panel(100, 100, 50, 50, modal: true);
        menu.AddChild(story);
        story.Laid();
        dispatcher.PushControl(story);

        Assert.Same(story, dispatcher.TopModal);
        Assert.True(dispatcher.IsMouseBlocked(10, 10));
    }

    [Fact]
    public void AModalInTheTreeButNotOnTheStack_BlocksNothing()
    {
        using var dispatcher = NewDispatcher();
        var root = new UIPanel { Width = 640, Height = 480 };
        root.AddChild(new Panel(100, 100, 50, 50, modal: true).Laid());

        Assert.Null(dispatcher.TopModal);
        Assert.False(dispatcher.IsMouseBlocked(10, 10));
    }

    [Fact]
    public void APanelAboveTheModal_TakesTheMouse()
    {
        using var dispatcher = NewDispatcher();
        var modal = new Panel(100, 100, 50, 50, modal: true).Laid();
        var above = new Panel(0, 0, 50, 50).Laid();
        dispatcher.PushControl(modal);
        dispatcher.PushControl(above);

        Assert.Same(modal, dispatcher.TopModal);
        Assert.False(dispatcher.IsMouseBlocked(10, 10));
        Assert.True(dispatcher.IsMouseBlocked(300, 300));
    }

    [Fact]
    public void AHiddenPanelAboveTheModal_DoesNotTakeTheMouse()
    {
        using var dispatcher = NewDispatcher();
        var modal = new Panel(100, 100, 50, 50, modal: true).Laid();
        var above = new Panel(0, 0, 50, 50).Laid();
        dispatcher.PushControl(modal);
        dispatcher.PushControl(above);
        above.Visible = false;

        Assert.True(dispatcher.IsMouseBlocked(10, 10));
    }

    [Fact]
    public void APanelBelowTheModal_IsBlocked()
    {
        using var dispatcher = NewDispatcher();
        var below = new Panel(0, 0, 50, 50).Laid();
        var modal = new Panel(100, 100, 50, 50, modal: true).Laid();
        dispatcher.PushControl(below);
        dispatcher.PushControl(modal);

        Assert.True(dispatcher.IsMouseBlocked(10, 10));
    }

    [Fact]
    public void TheTopmostOfTwoModals_HasTheInput()
    {
        using var dispatcher = NewDispatcher();
        var first = new Panel(0, 0, 50, 50, modal: true).Laid();
        var second = new Panel(100, 100, 50, 50, modal: true).Laid();
        dispatcher.PushControl(first);
        dispatcher.PushControl(second);

        Assert.Same(second, dispatcher.TopModal);
        Assert.True(dispatcher.IsMouseBlocked(10, 10));
    }

    [Fact]
    public void ACaptureInTheModal_IsNotBlockedOutsideIt_AndGetsItsClick()
    {
        using var dispatcher = NewDispatcher();
        var root = new Panel(0, 0, 640, 480);
        var modal = new Panel(100, 100, 50, 50, modal: true);
        var thumb = new Panel(5, 5, 10, 10);
        modal.AddChild(thumb);
        root.AddChild(modal);
        root.Laid();
        dispatcher.PushControl(modal);

        Press(dispatcher, root, 107, 107);
        Assert.True(dispatcher.HasCapture);
        Assert.False(dispatcher.IsMouseBlocked(10, 10));

        Release(dispatcher, root, 107, 107);
        Assert.Equal(1, thumb.MouseUps);
        Assert.Equal(1, thumb.Clicks);
        Assert.False(dispatcher.HasCapture);
    }

    [Fact]
    public void ACaptureOfTheModalItself_IsNotBlockedOutsideIt()
    {
        using var dispatcher = NewDispatcher();
        var root = new Panel(0, 0, 640, 480);
        var modal = new Panel(100, 100, 50, 50, modal: true);
        root.AddChild(modal);
        root.Laid();
        dispatcher.PushControl(modal);

        Press(dispatcher, root, 140, 140);

        Assert.True(dispatcher.HasCapture);
        Assert.False(dispatcher.IsMouseBlocked(10, 10));
    }

    [Fact]
    public void ACaptureInAPanelAboveTheModal_IsNotBlockedOutsideIt()
    {
        using var dispatcher = NewDispatcher();
        var root = new Panel(0, 0, 640, 480);
        var modal = new Panel(100, 100, 50, 50, modal: true);
        var list = new Panel(200, 200, 50, 50);
        root.AddChild(modal);
        root.AddChild(list);
        root.Laid();
        dispatcher.PushControl(modal);
        dispatcher.PushControl(list);

        Press(dispatcher, root, 210, 210);

        Assert.False(dispatcher.IsMouseBlocked(10, 10));
    }

    [Fact]
    public void AModalThatOpensDuringAPress_StillEndsTheCapture_WithNoClick()
    {
        using var dispatcher = NewDispatcher();
        var root = new Panel(0, 0, 640, 480);
        var button = new Panel(10, 10, 40, 20);
        var modal = new Panel(100, 100, 50, 50, modal: true);
        root.AddChild(button);
        root.AddChild(modal);
        root.Laid();

        Press(dispatcher, root, 20, 15);
        dispatcher.PushControl(modal);
        Release(dispatcher, root, 20, 15);

        Assert.Equal(1, button.MouseUps);
        Assert.Equal(0, button.Clicks);
        Assert.False(dispatcher.HasCapture);
    }

    [Fact]
    public void ADragThatABlockedReleaseEnds_TellsItsSource()
    {
        using var dispatcher = NewDispatcher();
        var root = new Panel(0, 0, 640, 480);
        var slot = new Panel(10, 10, 40, 20) { StartsDrag = true };
        var modal = new Panel(100, 100, 50, 50, modal: true);
        root.AddChild(slot);
        root.AddChild(modal);
        root.Laid();

        Press(dispatcher, root, 20, 15);
        Assert.True(dispatcher.TryStartDrag(30, 30));
        dispatcher.PushControl(modal);
        Release(dispatcher, root, 30, 30);

        Assert.Equal(1, slot.DragCancels);
        Assert.False(dispatcher.HasCapture);
    }

    [Fact]
    public void ABlockedPress_DoesNotReachThePanelBehindTheModal()
    {
        using var dispatcher = NewDispatcher();
        var root = new Panel(0, 0, 640, 480);
        var button = new Panel(10, 10, 40, 20);
        var modal = new Panel(100, 100, 50, 50, modal: true);
        root.AddChild(button);
        root.AddChild(modal);
        root.Laid();
        dispatcher.PushControl(modal);

        Press(dispatcher, root, 20, 15);
        Release(dispatcher, root, 20, 15);

        Assert.False(dispatcher.HasCapture);
        Assert.Equal(0, button.MouseUps);
        Assert.Equal(0, button.Clicks);
    }
}
