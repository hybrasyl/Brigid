using Brigid.Controls.Components;
using Brigid.Definitions;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Brigid.Tests;

// Verifies that the UITextBox / UILabel editor commands actually route through the keybind resolver after the B2
// migration — driving real controls end-to-end (not just the resolver in isolation). Clipboard commands (copy/cut/
// paste) touch the OS clipboard and are left to the resolver-level tests; these cover the selection / caret commands
// whose effect is observable without external state.
[Collection("UITextBoxFocus")]
public class TextEditingKeybindTests
{
    //mirrors InputBuffer.TranslateSdlMods on Windows/Linux: a physical Ctrl press sets BOTH Ctrl and Meta.
    private static KeyDownEvent WinEvent(Keys key, bool ctrl = false, bool shift = false, bool alt = false)
    {
        var mods = KeyModifiers.None;

        if (ctrl)
            mods |= KeyModifiers.Ctrl | KeyModifiers.Meta;

        if (shift)
            mods |= KeyModifiers.Shift;

        if (alt)
            mods |= KeyModifiers.Alt;

        return new KeyDownEvent { Key = key, Modifiers = mods };
    }

    private static UITextBox FocusedBox()
    {
        return new UITextBox { Width = 200, Height = 14, MaxLength = 1000, IsFocused = true };
    }

    [Fact]
    public void UITextBox_CtrlA_SelectsAll()
    {
        var box = FocusedBox();
        foreach (var c in "hello")
            box.OnTextInput(new TextInputEvent { Character = c });

        box.OnKeyDown(WinEvent(Keys.A, ctrl: true));

        Assert.True(box.HasSelection);
        Assert.Equal(5, box.SelectionLength);
    }

    [Fact]
    public void UITextBox_CtrlShiftA_IsNoLongerSelectAll()
    {
        var box = FocusedBox();
        foreach (var c in "hello")
            box.OnTextInput(new TextInputEvent { Character = c });

        //Ctrl+Shift+A used to be select-all; it is now unbound (select-all moved to Ctrl+A) and simply consumed.
        box.OnKeyDown(WinEvent(Keys.A, ctrl: true, shift: true));

        Assert.False(box.HasSelection);
    }

    [Fact]
    public void UITextBox_AltArrows_MoveToLineStartAndEnd()
    {
        var box = FocusedBox();
        foreach (var c in "hello")
            box.OnTextInput(new TextInputEvent { Character = c });

        box.OnKeyDown(WinEvent(Keys.Left, alt: true));
        Assert.Equal(0, box.CursorPosition);

        box.OnKeyDown(WinEvent(Keys.Right, alt: true));
        Assert.Equal(5, box.CursorPosition);
    }

    [Fact]
    public void UITextBox_PlainA_DoesNotSelectAll()
    {
        var box = FocusedBox();
        foreach (var c in "hello")
            box.OnTextInput(new TextInputEvent { Character = c });

        //a bare A must not select — proves select-all routes on the Ctrl+A chord, not the bare key. (Bare A
        //would arrive as OnTextInput in the real pipeline; OnKeyDown must not treat it as the command.)
        box.OnKeyDown(WinEvent(Keys.A));

        Assert.False(box.HasSelection);
    }

    //UILabel (ReadView) select-all/copy route through the same resolver mechanism (TryHandleReadCommand); its
    //text setter measures glyphs through the font backend, which isn't available in headless tests, so the
    //ReadView routing is covered at the resolver level (KeybindsTests.ContextIsolation_*) instead.
}
