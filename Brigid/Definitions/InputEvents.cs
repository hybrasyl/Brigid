#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Definitions;

[Flags]
public enum KeyModifiers
{
    None  = 0,
    Shift = 1,
    Ctrl  = 2,
    Alt   = 4,

    //platform primary modifier: literal Ctrl on Windows/Linux, Cmd (SDL GUI) on macOS. Set alongside
    //Ctrl on Windows/Linux, so a Meta-chord and a Ctrl-chord can collide there (keybind conflict
    //detection flags this per context).
    Meta  = 8
}

public enum MouseButton
{
    Left,
    Right
}

public abstract class InputEvent
{
    public bool Handled { get; set; }
    public UIElement? Target { get; internal set; }

    /// <summary>
    ///     Resets dispatch state for pooled reuse. Subclasses should reset their own fields
    ///     and call base.Reset().
    /// </summary>
    public virtual void Reset()
    {
        Handled = false;
        Target = null;
    }
}

//── mouse events ──

public abstract class MouseEvent : InputEvent
{
    public int ScreenX { get; set; }
    public int ScreenY { get; set; }
    public MouseButton Button { get; set; }
    public KeyModifiers Modifiers { get; set; }

    public bool Shift => (Modifiers & KeyModifiers.Shift) != 0;
    public bool Ctrl => (Modifiers & KeyModifiers.Ctrl) != 0;
    public bool Alt => (Modifiers & KeyModifiers.Alt) != 0;
    public bool Meta => (Modifiers & KeyModifiers.Meta) != 0;
}

public sealed class MouseDownEvent : MouseEvent;

public sealed class MouseUpEvent : MouseEvent;

public sealed class ClickEvent : MouseEvent;

public sealed class DoubleClickEvent : MouseEvent;

public sealed class MouseMoveEvent : MouseEvent
{
    public int DeltaX { get; set; }
    public int DeltaY { get; set; }
}

public sealed class MouseScrollEvent : MouseEvent
{
    public int Delta { get; set; }
}

//── key events ──

public abstract class KeyEvent : InputEvent
{
    public Keys Key { get; set; }
    public KeyModifiers Modifiers { get; set; }

    public bool Shift => (Modifiers & KeyModifiers.Shift) != 0;
    public bool Ctrl => (Modifiers & KeyModifiers.Ctrl) != 0;
    public bool Alt => (Modifiers & KeyModifiers.Alt) != 0;
    public bool Meta => (Modifiers & KeyModifiers.Meta) != 0;
}

public sealed class KeyDownEvent : KeyEvent;

public sealed class KeyUpEvent : KeyEvent;

public sealed class TextInputEvent : InputEvent
{
    public char Character { get; set; }
}

//── drag events ──

public abstract class DragEvent : InputEvent
{
    public int ScreenX { get; set; }
    public int ScreenY { get; set; }
    public MouseButton Button { get; set; }
}

public sealed class DragStartEvent : DragEvent
{
    public UIElement? Source { get; set; }
    public object? Payload { get; set; }

    public override void Reset()
    {
        base.Reset();
        Source = null;
        Payload = null;
    }
}

public sealed class DragMoveEvent : DragEvent
{
    public object? Payload { get; set; }

    public override void Reset()
    {
        base.Reset();
        Payload = null;
    }
}

public sealed class DragDropEvent : DragEvent
{
    public object? Payload { get; set; }
    public UIElement? DropTarget { get; set; }

    public override void Reset()
    {
        base.Reset();
        Payload = null;
        DropTarget = null;
    }
}
