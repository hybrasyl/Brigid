#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-135: an open drop-down list is on the control stack, as <see cref="PopupMenu" /> is, so it gets Escape before
///     its host, and it leaves the stack when the list closes, also when the host hides. The tests register the dispatcher
///     as <see cref="InputDispatcher.Instance" />, so they run in a collection that runs alone.
/// </summary>
[Collection("UITextBoxFocus")]
public class DropDownStackTests
{
    private sealed class Host : UIPanel
    {
        public List<Keys> Received { get; } = [];

        public override void OnKeyDown(KeyDownEvent e)
        {
            Received.Add(e.Key);
            e.Handled = true;
        }
    }

    private static (InputDispatcher Dispatcher, Host Host, DropDown DropDown) Make()
    {
        var dispatcher = new InputDispatcher();
        var host = new Host { Width = 200, Height = 200 };
        var dropDown = new DropDown(["Human", "Elf"], 100, 18);
        host.AddChild(dropDown);

        return (dispatcher, host, dropDown);
    }

    [Fact]
    public void AnOpenListIsOnTheStack_AndEscapeClosesItThere()
    {
        var (dispatcher, host, dropDown) = Make();

        try
        {
            dropDown.Open();

            Assert.Same(dropDown, dispatcher.TopControl);

            dispatcher.DispatchKeyboardEvent(host, new KeyDownEvent { Key = Keys.Escape });

            Assert.False(dropDown.IsOpen);
            Assert.Empty(host.Received);
            Assert.Equal(0, dispatcher.ControlStackCount);
        } finally
        {
            dispatcher.Dispose();
        }
    }

    [Fact]
    public void TheOtherKeysGoOnToTheHost()
    {
        var (dispatcher, host, dropDown) = Make();

        try
        {
            dropDown.Open();
            dispatcher.DispatchKeyboardEvent(host, new KeyDownEvent { Key = Keys.F2 });

            Assert.Equal([Keys.F2], host.Received);
            Assert.True(dropDown.IsOpen);
        } finally
        {
            dropDown.Close();
            dispatcher.Dispose();
        }
    }

    [Fact]
    public void AHostThatHidesWithTheListOpenLeavesNoStackEntry()
    {
        var (dispatcher, host, dropDown) = Make();

        try
        {
            dropDown.Open();
            host.Visible = false;

            Assert.False(dropDown.IsOpen);
            Assert.Equal(0, dispatcher.ControlStackCount);
        } finally
        {
            dispatcher.Dispose();
        }
    }

    [Fact]
    public void EachWayOfClosingTakesTheListOffTheStack()
    {
        var (dispatcher, _, dropDown) = Make();

        try
        {
            dropDown.Open();
            dropDown.OnMouseLeave();

            Assert.Equal(0, dispatcher.ControlStackCount);

            dropDown.Open();
            dropDown.Close();

            Assert.Equal(0, dispatcher.ControlStackCount);

            //a click on an entry
            dropDown.Open();
            dropDown.OnClick(new ClickEvent { Button = MouseButton.Left, ScreenY = dropDown.ScreenY + 18 + 2 });

            Assert.False(dropDown.IsOpen);
            Assert.Equal(0, dispatcher.ControlStackCount);

            //a control that is disabled while its list is open
            dropDown.Open();
            dropDown.Enabled = false;
            dropDown.Update(new Microsoft.Xna.Framework.GameTime());

            Assert.Equal(0, dispatcher.ControlStackCount);
        } finally
        {
            dispatcher.Dispose();
        }
    }

    //the review of BRIG-135: a focused text box in the host keeps its focus while the list is open, so the player can
    //still type in it, and Escape, which the text box lets through, still closes the list
    [Fact]
    public void AFocusedTextBoxKeepsItsFocus_AndEscapeStillClosesTheList()
    {
        var (dispatcher, host, dropDown) = Make();
        //the field sits in a plain pane, as the lobby forms' fields do, so Escape bubbles past it to the stack
        var pane = new UIPanel { Y = 40, Width = 200, Height = 20 };
        var name = new UITextBox { Width = 100, Height = 14 };
        pane.AddChild(name);
        host.AddChild(pane);

        try
        {
            name.IsFocused = true;
            dropDown.Open();

            //the field still gets the typed keys while the list is open
            Assert.Same(name, dispatcher.ExplicitFocus);

            dispatcher.DispatchKeyboardEvent(host, new KeyDownEvent { Key = Keys.Escape });

            Assert.False(dropDown.IsOpen);
            Assert.True(name.IsFocused);
            Assert.Same(name, dispatcher.ExplicitFocus);
            Assert.Equal(0, dispatcher.ControlStackCount);
        } finally
        {
            name.IsFocused = false;
            dispatcher.Dispose();
        }
    }
}
