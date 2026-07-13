namespace Brigid.Systems.Keybinds;

/// <summary>
///     The focus context a command belongs to. The active context follows the same signals
///     <c>InputDispatcher</c> already routes by (explicit focus, top control), so each handler resolves
///     only its own context's commands. Context lets the <i>same physical chord</i> mean different things
///     by focus (e.g. Meta+C copies in both an editable field and a read-only view, but they are distinct
///     commands so either can be rebound independently).
/// </summary>
public enum KeybindContext
{
    /// <summary>Editable text field (<c>UITextBox</c>).</summary>
    TextEditing,

    /// <summary>Read-only selectable text (<c>UILabel</c>).</summary>
    ReadView,

    /// <summary>A focused popup/modal on the control stack.</summary>
    Modal,

    /// <summary>World HUD / global hotkeys (<c>WorldScreen.OnRootKeyDown</c>).</summary>
    WorldHud
}

/// <summary>
///     Stable identifier for a rebindable action. Names are the JSON keys in <c>keybinds.json</c>, so they
///     must stay stable across releases (renaming a member orphans a user's override; the loader tolerates
///     that by skipping unknown names). Grouped by context; each has a default binding and context in
///     <see cref="Keybinds" />.
///     <para>
///         The <c>&lt;Group&gt;_&lt;Action&gt;</c> underscore naming is a <b>deliberate</b> convention (C# can't
///         nest enums): the prefix namespaces the command and doubles as a readable JSON-key separator. It
///         diverges from the codebase's other enums intentionally, and scales to <c>World_ToggleInventory</c>,
///         <c>Slot_Use1</c>, etc. as later phases add commands.
///     </para>
///     <para>
///         Only the TextEditing/ReadView commands are populated in the B1 foundation (the B2 pilot surface).
///         WorldHud/Modal and held-action movement commands are added by their migration phases (B3/B4),
///         where their defaults can be verified byte-for-byte against the live handler.
///     </para>
/// </summary>
public enum CommandId
{
    // ── TextEditing (UITextBox) ──
    Editor_Copy,
    Editor_Cut,
    Editor_Paste,
    Editor_Undo,
    Editor_Redo,
    Editor_SelectAll,

    /// <summary>Readline line-start — literal Ctrl+A on every OS (native macOS text-field behavior).</summary>
    Editor_LineStart,

    /// <summary>Readline line-end — literal Ctrl+E on every OS.</summary>
    Editor_LineEnd,

    // ── ReadView (UILabel, read-only) ──
    Read_Copy,
    Read_SelectAll
}
