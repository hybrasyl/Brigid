#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client;

public sealed class InputDispatcher
{
    private const float DOUBLE_CLICK_MS = 500f;
    private const int DRAG_THRESHOLD_SQ = 16; //4px squared

    //control stack — ordered list of panels that participate in keyboard dispatch.
    //last element is the "top" (active keyboard target).
    private readonly List<UIPanel> ControlStack = [];

    //explicit focus — the single element (typically a uitextbox) that receives
    //keyboard events before the control stack. set via textboxfocusgained.
    private UIElement? ExplicitFocusElement;


    //hover state
    private UIElement? HoveredElement;

    //mouse capture state
    private UIElement? CapturedElement;
    private Point MouseDownPosition;
    private MouseButton MouseDownButton;

    //click synthesis
    private UIElement? LastClickTarget;
    private float LastClickTime;

    //drag state
    private bool DragActive;
    private object? DragPayload;

    //previous mouse position for delta
    private int PreviousMouseX;
    private int PreviousMouseY;

    //pooled event instances — reused each frame to avoid per-frame allocations.
    //safe because dispatch is synchronous (event is fully consumed before the next dispatch).
    private readonly MouseDownEvent MouseDown = new();
    private readonly MouseUpEvent MouseUp = new();
    private readonly ClickEvent Click = new();
    private readonly DoubleClickEvent DoubleClick = new();
    private readonly MouseMoveEvent MouseMove = new();
    private readonly MouseScrollEvent MouseScroll = new();
    private readonly KeyDownEvent KeyDown = new();
    private readonly KeyUpEvent KeyUp = new();
    private readonly TextInputEvent TextInput = new();
    private readonly DragStartEvent DragStart = new();
    private readonly DragMoveEvent DragMove = new();
    private readonly DragDropEvent DragDrop = new();

    //per-frame scratch set used to filter out OS key-repeat KeyDowns. cleared at
    //the top of ProcessInput so the same allocation is reused every frame.
    private readonly HashSet<Keys> DispatchedKeys = [];

    /// <summary>
    ///     Singleton accessor for use by UI controls that need to push/remove themselves
    ///     from the control stack (e.g. PrefabPanel.Show/Hide).
    /// </summary>
    public static InputDispatcher? Instance { get; private set; }

    public InputDispatcher()
    {
        Instance = this;
        UITextBox.TextBoxFocusGained += OnTextBoxFocusGained;
    }

    /// <summary>
    ///     Routes keyboard events to the newly focused textbox by setting it as the explicit focus target.
    /// </summary>
    private void OnTextBoxFocusGained(UITextBox textBox) => SetExplicitFocus(textBox);

    //── explicit focus ──

    /// <summary>
    ///     Current explicit focus target (typically a UITextBox). When set, keyboard events
    ///     are delivered to this element first (Phase 1) before the control stack (Phase 2).
    /// </summary>
    public UIElement? ExplicitFocus => ExplicitFocusElement;

    /// <summary>
    ///     The chat input textbox. When set, focus on this element survives popup push/pop so
    ///     typing is never interrupted by transient popups. Set once by ChatInputControl.
    /// </summary>
    public UITextBox? ChatInputTextBox { get; set; }

    /// <summary>
    ///     True when a drag operation is in progress.
    /// </summary>
    public bool IsDragging => DragActive;

    /// <summary>
    ///     The active drag payload, or null.
    /// </summary>
    public object? ActiveDragPayload => DragPayload;

    /// <summary>
    ///     Sets explicit focus to the specified element. Pass null to clear.
    /// </summary>
    public void SetExplicitFocus(UIElement? element) => ExplicitFocusElement = element;

    /// <summary>
    ///     Clears explicit focus.
    /// </summary>
    public void ClearExplicitFocus() => SetExplicitFocus(null);

    //── control stack ──

    /// <summary>
    ///     Pushes a panel onto the control stack. Idempotent — if already present, removes
    ///     and re-adds to the top. The topmost panel receives keyboard events via Phase 2
    ///     when explicit focus doesn't handle them.
    /// </summary>
    public void PushControl(UIPanel panel)
    {
        ControlStack.Remove(panel);
        ControlStack.Add(panel);

        //focus follows the stack top. clear any stale focus sitting in a panel below
        //the new top so keys don't leak to it (e.g. escape hitting a login textbox
        //behind an error popup). panels that want a specific child focused on open
        //can assign IsFocused themselves during their Show() override.
        //exception: the chat input textbox is sticky — popups must not interrupt typing.
        if (ExplicitFocusElement is not null
            && (ExplicitFocusElement != ChatInputTextBox)
            && (ExplicitFocusElement != panel)
            && !IsDescendantOf(panel, ExplicitFocusElement))
            ClearExplicitFocus();
    }

    /// <summary>
    ///     Removes a panel from the control stack. If the removed panel contains the
    ///     explicitly focused element, clears explicit focus.
    /// </summary>
    public void RemoveControl(UIPanel panel)
    {
        ControlStack.Remove(panel);

        if (ExplicitFocusElement is not null && IsDescendantOf(panel, ExplicitFocusElement))
            ClearExplicitFocus();

        //focus follows the stack top: after removal, the new top becomes the focus
        //target so keys route into it via Phase 1/1.5 before Phase 2 stack dispatch.
        //when the stack is now empty, leave focus clear (root-level hotkeys take over).
        //exception: don't override the chat input textbox — it owns its focus track.
        if (TopControl is { } newTop
            && IsEffectivelyVisible(newTop)
            && (ExplicitFocusElement != ChatInputTextBox))
            SetExplicitFocus(newTop);
    }

    /// <summary>
    ///     The topmost panel on the control stack, or null if the stack is empty.
    /// </summary>
    public UIPanel? TopControl => ControlStack.Count > 0 ? ControlStack[^1] : null;

    /// <summary>
    ///     Number of panels on the control stack.
    /// </summary>
    public int ControlStackCount => ControlStack.Count;

    /// <summary>
    ///     Resets all dispatcher state. Called by ScreenManager.Switch to prevent stale
    ///     elements from persisting across screen transitions.
    /// </summary>
    public void Clear()
    {
        ControlStack.Clear();
        ClearExplicitFocus();
        CapturedElement = null;

        DragActive = false;
        DragPayload = null;
        HoveredElement = null;
    }

    //── main per-frame entry point ──

    /// <summary>
    ///     Main per-frame entry point. Reads buffered input, produces events, dispatches them.
    /// </summary>
    public void ProcessInput(UIPanel root, GameTime gameTime)
    {
        var totalMs = (float)gameTime.TotalGameTime.TotalMilliseconds;
        var mouseX = InputBuffer.MouseX;
        var mouseY = InputBuffer.MouseY;
        var modifiers = GetModifiers();

        //mouse blocking: when a textbox has explicit focus, restrict mouse events
        //to the panel containing the focused textbox. clicks outside are consumed.
        var mouseBlocked = false;

        if (ExplicitFocusElement is not null)
        {
            var containingPanel = FindContainingStackEntry(ExplicitFocusElement) ?? ExplicitFocusElement.Parent;

            if (containingPanel is not null && !containingPanel.ContainsPoint(mouseX, mouseY))
                mouseBlocked = true;
        }

        if (!mouseBlocked)
        {
            //── mouse position changed -> mousemove + hover tracking ──
            if ((mouseX != PreviousMouseX) || (mouseY != PreviousMouseY))
            {
                var deltaX = mouseX - PreviousMouseX;
                var deltaY = mouseY - PreviousMouseY;
                PreviousMouseX = mouseX;
                PreviousMouseY = mouseY;

                //hit-test once for hover tracking, drag-move, and free-movement dispatch
                var hitUnderCursor = HitTest(root, mouseX, mouseY);

                if (CapturedElement is not null)
                {
                    //route mousemove to captured element (for scrollbar drag, text selection, etc.)
                    MouseMove.Reset();
                    MouseMove.ScreenX = mouseX;
                    MouseMove.ScreenY = mouseY;
                    MouseMove.DeltaX = deltaX;
                    MouseMove.DeltaY = deltaY;
                    MouseMove.Modifiers = modifiers;
                    MouseMove.Target = CapturedElement;
                    CapturedElement.OnMouseMove(MouseMove);

                    //check drag threshold
                    if (!DragActive)
                    {
                        var dx = mouseX - MouseDownPosition.X;
                        var dy = mouseY - MouseDownPosition.Y;

                        if (((dx * dx) + (dy * dy)) >= DRAG_THRESHOLD_SQ)
                        {
                            DragStart.Reset();
                            DragStart.ScreenX = mouseX;
                            DragStart.ScreenY = mouseY;
                            DragStart.Button = MouseDownButton;
                            DragStart.Source = CapturedElement;
                            DragStart.Target = CapturedElement;
                            CapturedElement.OnDragStart(DragStart);

                            if (DragStart.Payload is not null)
                            {
                                DragActive = true;
                                DragPayload = DragStart.Payload;
                            }
                        }
                    }

                    //dragmove fires on element under cursor (hit-tested, not captured)
                    if (DragActive)
                    {
                        DragMove.Reset();
                        DragMove.ScreenX = mouseX;
                        DragMove.ScreenY = mouseY;
                        DragMove.Button = MouseDownButton;
                        DragMove.Payload = DragPayload;
                        DispatchBubble(hitUnderCursor ?? root, DragMove);
                    }
                } else
                {
                    //no capture — dispatch mousemove to the hit-tested element (for hover effects)
                    MouseMove.Reset();
                    MouseMove.ScreenX = mouseX;
                    MouseMove.ScreenY = mouseY;
                    MouseMove.DeltaX = deltaX;
                    MouseMove.DeltaY = deltaY;
                    MouseMove.Modifiers = modifiers;
                    DispatchBubble(hitUnderCursor ?? root, MouseMove);
                }

                //hover tracking — reuse cached hit-test result
                var newHover = hitUnderCursor;

                if (newHover != HoveredElement)
                {
                    HoveredElement?.OnMouseLeave();
                    HoveredElement = newHover;
                    HoveredElement?.OnMouseEnter();
                }
            }

        } else
        {
            //mouse is blocked — still track position for delta calculation next frame
            PreviousMouseX = mouseX;
            PreviousMouseY = mouseY;
        }

        //── unified event stream (keyboard + mouse button + wheel in true OS order) ──
        //every event the SDL watcher captured this frame lives in a single chronologically
        //ordered list. this matters for macros that mix input kinds: a sequence of
        //key → click → scroll → key fires in exactly that order instead of being
        //reordered to one-mode-then-another-mode (which would cause the first event to
        //miss the state the earlier event was supposed to set up).
        //
        //mouse button and wheel events carry their own SDL-captured cursor position
        //and modifier state. keyboard events use a running modifier state tracked
        //per-event, because fast macros can press shift, tap a key, release shift,
        //and press another key within a single frame — if we used end-of-frame state
        //both keys would see shift=false and shift-chorded hotkeys would silently lose
        //their modifier.
        //
        //WM_KEYDOWN always precedes its WM_CHAR, so when a KeyDown handler gains
        //textbox focus we suppress the immediately following TextInput to prevent the
        //hotkey character from leaking into the newly-focused textbox.
        var events = InputBuffer.Events;

        //derive start-of-frame modifier state by walking backwards from the
        //authoritative end-of-frame held state, undoing each key-modifier transition.
        //non-key events are skipped — they don't affect modifier state.
        var runningMods = GetModifiers();

        for (var i = events.Length - 1; i >= 0; i--)
        {
            var evt = events[i];

            if (evt.Kind is not (BufferedInputKind.KeyDown or BufferedInputKind.KeyUp))
                continue;

            var bit = ModifierBitFor(evt.Key);

            if (bit == KeyModifiers.None)
                continue;

            if (evt.Kind == BufferedInputKind.KeyDown)
                runningMods &= ~bit;
            else
                runningMods |= bit;
        }

        var suppressNextTextInput = false;
        DispatchedKeys.Clear();

        //walk forward, dispatching each event in OS order. keyboard events update and
        //stamp the running modifier state; mouse button events carry their own
        //SDL-captured modifiers and are dispatched (unless mouseBlocked) via the same
        //ProcessMouseButton path the old two-pass implementation used.
        foreach (var evt in events)
            switch (evt.Kind)
            {
                case BufferedInputKind.MouseButton:
                {
                    if (!mouseBlocked)
                        ProcessMouseButton(
                            root,
                            evt.X,
                            evt.Y,
                            totalMs,
                            evt.Modifiers,
                            evt.Button,
                            evt.IsPress,
                            !evt.IsPress);

                    break;
                }

                case BufferedInputKind.MouseWheel:
                {
                    //per-event wheel dispatch — hit-test at the captured cursor position
                    //so a scroll that lands in a different panel than the frame-end cursor
                    //is still routed correctly.
                    if (!mouseBlocked)
                    {
                        var scrollTarget = HitTest(root, evt.X, evt.Y);

                        MouseScroll.Reset();
                        MouseScroll.ScreenX = evt.X;
                        MouseScroll.ScreenY = evt.Y;
                        MouseScroll.Delta = evt.WheelDelta;
                        MouseScroll.Modifiers = evt.Modifiers;
                        DispatchBubble(scrollTarget ?? root, MouseScroll);
                    }

                    break;
                }

                case BufferedInputKind.KeyDown:
                {
                    var bit = ModifierBitFor(evt.Key);

                    if (bit != KeyModifiers.None)
                        runningMods |= bit;

                    //OS key-repeat emits repeated WM_KEYDOWN for a single held press.
                    //DispatchedKeys is reset per-key on KeyUp so genuine re-presses
                    //inside the same frame still dispatch.
                    if (!DispatchedKeys.Add(evt.Key))
                        break;

                    var focusBefore = ExplicitFocusElement;

                    KeyDown.Reset();
                    KeyDown.Key = evt.Key;
                    KeyDown.Modifiers = runningMods;
                    DispatchKeyboardEvent(root, KeyDown);

                    if ((focusBefore is null) && (ExplicitFocusElement is not null))
                        suppressNextTextInput = true;

                    break;
                }

                case BufferedInputKind.KeyUp:
                {
                    var bit = ModifierBitFor(evt.Key);

                    if (bit != KeyModifiers.None)
                        runningMods &= ~bit;

                    DispatchedKeys.Remove(evt.Key);

                    KeyUp.Reset();
                    KeyUp.Key = evt.Key;
                    KeyUp.Modifiers = runningMods;
                    DispatchKeyboardEvent(root, KeyUp);

                    break;
                }

                case BufferedInputKind.TextInput:
                {
                    if (suppressNextTextInput)
                    {
                        suppressNextTextInput = false;

                        break;
                    }

                    TextInput.Reset();
                    TextInput.Character = evt.Character;
                    DispatchKeyboardEvent(root, TextInput);

                    break;
                }
            }
    }

    private void ProcessMouseButton(
        UIPanel root,
        int mouseX,
        int mouseY,
        float totalMs,
        KeyModifiers modifiers,
        MouseButton button,
        bool wasPressed,
        bool wasReleased)
    {
        if (wasPressed)
        {
            var target = HitTest(root, mouseX, mouseY) ?? root;

            //set capture
            CapturedElement = target;

            MouseDownPosition = new Point(mouseX, mouseY);
            MouseDownButton = button;

            MouseDown.Reset();
            MouseDown.ScreenX = mouseX;
            MouseDown.ScreenY = mouseY;
            MouseDown.Button = button;
            MouseDown.Modifiers = modifiers;
            DispatchBubble(target, MouseDown);
        }

        if (wasReleased)
        {
            var wasDragging = DragActive;

            //drag drop
            if (DragActive && (button == MouseDownButton))
            {
                var dropTarget = HitTest(root, mouseX, mouseY);

                DragDrop.Reset();
                DragDrop.ScreenX = mouseX;
                DragDrop.ScreenY = mouseY;
                DragDrop.Button = button;
                DragDrop.Payload = DragPayload;
                DragDrop.DropTarget = dropTarget;
                DispatchBubble(dropTarget ?? root, DragDrop);

                DragActive = false;
                DragPayload = null;

            }

            //mouseup routes to captured element
            var upTarget = CapturedElement ?? HitTest(root, mouseX, mouseY) ?? root;

            MouseUp.Reset();
            MouseUp.ScreenX = mouseX;
            MouseUp.ScreenY = mouseY;
            MouseUp.Button = button;
            MouseUp.Modifiers = modifiers;
            DispatchBubble(upTarget, MouseUp);

            //click synthesis — cursor must still be within captured element's bounds, and no drag occurred
            if ((CapturedElement is not null) && !wasDragging && CapturedElement.ContainsPoint(mouseX, mouseY))
            {
                Click.Reset();
                Click.ScreenX = mouseX;
                Click.ScreenY = mouseY;
                Click.Button = button;
                Click.Modifiers = modifiers;
                DispatchBubble(CapturedElement, Click);

                //doubleclick synthesis
                if ((CapturedElement == LastClickTarget) && ((totalMs - LastClickTime) < DOUBLE_CLICK_MS))
                {
                    DoubleClick.Reset();
                    DoubleClick.ScreenX = mouseX;
                    DoubleClick.ScreenY = mouseY;
                    DoubleClick.Button = button;
                    DoubleClick.Modifiers = modifiers;
                    DispatchBubble(CapturedElement, DoubleClick);
                    LastClickTarget = null;
                    LastClickTime = 0;
                } else
                {
                    LastClickTarget = CapturedElement;
                    LastClickTime = totalMs;
                }
            }

            //release capture
            CapturedElement = null;
    
        }
    }

    //── hit-test ──

    /// <summary>
    ///     Walks the element tree top-down, deepest-child-first, highest-ZIndex-first.
    ///     Returns the deepest visible, enabled, hit-test-visible element under the cursor, or null.
    /// </summary>
    public static UIElement? HitTest(UIPanel panel, int screenX, int screenY)
    {
        if (!panel.Visible || !panel.Enabled || !panel.IsHitTestVisible)
            return null;

        //ensure children are sorted before hit-testing — processinput runs before update,
        //so the sort from update/draw may not have run yet this frame.
        panel.EnsureChildOrder();

        //children are sorted by zindex ascending — iterate in reverse for highest-first
        var children = panel.Children;

        for (var i = children.Count - 1; i >= 0; i--)
        {
            var child = children[i];

            if (!child.Visible || !child.Enabled || !child.IsHitTestVisible)
                continue;

            if (child is UIPanel childPanel)
            {
                //skip panels that don't contain the cursor — children don't extend beyond parent bounds
                if (!childPanel.ContainsPoint(screenX, screenY))
                    continue;

                var hit = HitTest(childPanel, screenX, screenY);

                if (hit is not null)
                    return hit;
            } else if (child.ContainsPoint(screenX, screenY))
                return child;
        }

        //check the panel itself — pass-through panels only match children, never themselves
        if (!panel.IsPassThrough && panel.ContainsPoint(screenX, screenY))
            return panel;

        return null;
    }

    //── dispatch ──

    private static void DispatchBubble(UIElement target, InputEvent e)
    {
        e.Target = target;
        var current = target;

        while (current is not null)
        {
            DispatchSingle(current, e);

            if (e.Handled)
                return;

            current = current.Parent;
        }
    }

    /// <summary>
    ///     Two-phase keyboard dispatch.
    ///     Phase 1: If ExplicitFocus is set and visible, deliver to it (no bubbling). Falls through to Phase 2 if unhandled.
    ///     Phase 2: Target = TopControl or root. Dispatches with normal parent bubbling.
    /// </summary>
    private void DispatchKeyboardEvent(UIPanel root, InputEvent e)
    {
        //phase 0: global hotkeys — always delivered to root, bypass focus and stack
        if (e is KeyDownEvent { Key: Keys.Enter, Alt: true })
        {
            DispatchSingle(root, e);

            if (e.Handled)
                return;
        }

        //phase 1: explicit focus (single target, no bubbling)
        if (ExplicitFocusElement is not null && IsEffectivelyVisible(ExplicitFocusElement))
        {
            DispatchSingle(ExplicitFocusElement, e);

            if (e.Handled)
                return;

            //phase 1.5: if the focused element is not a panel, bubble to its immediate
            //parent panel. this lets parent controls handle keys their children don't
            //(e.g. dialogtextentrypanel closes on escape, chat panel unfocuses on escape).
            if (ExplicitFocusElement is not UIPanel && ExplicitFocusElement!.Parent is { } parentPanel)
            {
                DispatchSingle(parentPanel, e);

                if (e.Handled)
                    return;
            }
        } else if (ExplicitFocusElement is not null)

            //explicit focus is no longer visible — clear it
            ClearExplicitFocus();

        //phase 2: stack dispatch with bubbling
        var target = TopControl ?? root;
        DispatchBubble(target, e);
    }

    private static bool IsEffectivelyVisible(UIElement element)
    {
        var current = element;

        while (current is not null)
        {
            if (!current.Visible)
                return false;

            current = current.Parent;
        }

        return true;
    }

    private static void DispatchSingle(UIElement element, InputEvent e)
    {
        switch (e)
        {
            case MouseDownEvent md:
                element.OnMouseDown(md);

                break;
            case MouseUpEvent mu:
                element.OnMouseUp(mu);

                break;
            case ClickEvent click:
                element.OnClick(click);

                break;
            case DoubleClickEvent dbl:
                element.OnDoubleClick(dbl);

                break;
            case MouseMoveEvent move:
                element.OnMouseMove(move);

                break;
            case MouseScrollEvent scroll:
                element.OnMouseScroll(scroll);

                break;
            case KeyDownEvent kd:
                element.OnKeyDown(kd);

                break;
            case KeyUpEvent ku:
                element.OnKeyUp(ku);

                break;
            case TextInputEvent ti:
                element.OnTextInput(ti);

                break;
            case DragStartEvent ds:
                element.OnDragStart(ds);

                break;
            case DragMoveEvent dm:
                element.OnDragMove(dm);

                break;
            case DragDropEvent dd:
                element.OnDragDrop(dd);

                break;
        }
    }

    //── helpers ──

    private static KeyModifiers GetModifiers()
    {
        var mods = KeyModifiers.None;

        if (InputBuffer.IsKeyHeld(Keys.LeftShift) || InputBuffer.IsKeyHeld(Keys.RightShift))
            mods |= KeyModifiers.Shift;

        if (InputBuffer.IsKeyHeld(Keys.LeftControl) || InputBuffer.IsKeyHeld(Keys.RightControl))
            mods |= KeyModifiers.Ctrl;

        if (InputBuffer.IsKeyHeld(Keys.LeftAlt) || InputBuffer.IsKeyHeld(Keys.RightAlt))
            mods |= KeyModifiers.Alt;

        return mods;
    }

    private static KeyModifiers ModifierBitFor(Keys key) => key switch
    {
        Keys.LeftShift or Keys.RightShift     => KeyModifiers.Shift,
        Keys.LeftControl or Keys.RightControl => KeyModifiers.Ctrl,
        Keys.LeftAlt or Keys.RightAlt         => KeyModifiers.Alt,
        _                                     => KeyModifiers.None
    };

    /// <summary>
    ///     True if <paramref name="descendant" /> is a child, grandchild, etc. of <paramref name="ancestor" />.
    ///     Unlike the old IsDescendantOrSelf, this does NOT return true when descendant == ancestor.
    /// </summary>
    private static bool IsDescendantOf(UIPanel ancestor, UIElement descendant)
    {
        var current = descendant.Parent;

        while (current is not null)
        {
            if (current == ancestor)
                return true;

            current = current.Parent;
        }

        return false;
    }

    /// <summary>
    ///     Walks up from the element to find the nearest ancestor that is on the control stack.
    /// </summary>
    private UIPanel? FindContainingStackEntry(UIElement element)
    {
        var current = element.Parent;

        while (current is not null)
        {
            if (ControlStack.Contains(current))
                return current;

            current = current.Parent;
        }

        return null;
    }

}