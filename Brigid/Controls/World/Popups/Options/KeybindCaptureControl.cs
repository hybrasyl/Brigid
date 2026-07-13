#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Systems.Keybinds;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Options;

/// <summary>
///     A small capture modal for rebinding one chord slot of a command. Opened from the Keybinds tab via
///     <see cref="Show(CommandId,ChordSlot)" />; it live-captures the next non-modifier keystroke through
///     <see cref="Keybinds.ChordFromEvent" />, echoes the chord it read plus a reserved/conflict status, and
///     commits on Set — raising <see cref="Committed" /> for the host to apply + persist. Escape or Close
///     cancels without changing anything. Sits on top of the Options modal in the control stack, so it captures
///     keys while the tab behind it stays put.
/// </summary>
internal sealed class KeybindCaptureControl : CenteredModalPanel
{
    private const int PANEL_W = 320;
    private const int PANEL_H = 150;
    private const int SET_W = 52;

    private readonly UILabel PromptLabel;
    private readonly UILabel CapturedLabel;
    private readonly UILabel StatusLabel;
    private readonly TextButton SetButton;

    private CommandId Command;
    private ChordSlot Slot;
    private KeyChord? Captured;

    /// <summary>Raised when the user commits a captured chord (command, slot, chord). Never raised on cancel.</summary>
    public event Action<CommandId, ChordSlot, KeyChord>? Committed;

    public KeybindCaptureControl() : base("Rebind Key", PANEL_W, PANEL_H)
    {
        var content = ContentBounds;

        PromptLabel = new UILabel
        {
            X = content.X,
            Y = content.Y + 4,
            Width = content.Width,
            Height = TextRenderer.CHAR_HEIGHT,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ForegroundColor = TextColors.Default,
            IsHitTestVisible = false
        };
        AddChild(PromptLabel);

        CapturedLabel = new UILabel
        {
            X = content.X,
            Y = content.Y + content.Height / 2 - TextRenderer.CHAR_HEIGHT / 2,
            Width = content.Width,
            Height = TextRenderer.CHAR_HEIGHT,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ForegroundColor = DialogPalette.SelectedText,
            IsHitTestVisible = false
        };
        AddChild(CapturedLabel);

        StatusLabel = new UILabel
        {
            X = content.X,
            Y = content.Bottom - TextRenderer.CHAR_HEIGHT - 2,
            Width = content.Width,
            Height = TextRenderer.CHAR_HEIGHT,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        AddChild(StatusLabel);

        SetButton = AddBottomBarButton("Set", SET_W, ApplyCapture);
    }

    //the base parameterless Show() would reveal the modal with stale Command/Slot/Captured; capture is always
    //opened for a specific command via the two-arg overload, so guard the paramterless path.
    public override void Show() =>
        throw new InvalidOperationException($"{nameof(KeybindCaptureControl)} must be opened via Show(CommandId, ChordSlot).");

    /// <summary>Opens the modal to capture a new chord for one slot of <paramref name="command" />.</summary>
    public void Show(CommandId command, ChordSlot slot)
    {
        Command = command;
        Slot = slot;
        Captured = null;

        var name = KeybindCatalog.Entry(command)?.DisplayName ?? command.ToString();

        var slotLabel = Keybinds.SupportsSecondary(command)
            ? slot == ChordSlot.Primary ? "Primary" : "Alternate"
            : "Key";

        SetTitle($"Rebind — {name}");
        PromptLabel.Text = $"{name} · {slotLabel}";
        CapturedLabel.Text = "Press a key…";
        CapturedLabel.ForegroundColor = DialogPalette.DisabledText;
        StatusLabel.Text = string.Empty;
        SetButton.SetEnabled(false);

        base.Show();
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            Hide();
            e.Handled = true;

            return;
        }

        //ChordFromEvent returns null for a bare modifier press — keep waiting for a real key rather than
        //recording Shift/Ctrl/Alt on their own.
        if (Keybinds.ChordFromEvent(e) is { } chord)
        {
            Captured = chord;
            UpdateCapturedDisplay(chord);
        }

        //swallow every key while capturing so nothing behind the modal reacts (the Options tab, world hotkeys).
        e.Handled = true;
    }

    private void UpdateCapturedDisplay(KeyChord chord)
    {
        CapturedLabel.Text = KeybindCatalog.Format(chord);
        CapturedLabel.ForegroundColor = DialogPalette.SelectedText;

        var context = Keybinds.Defaults[Command].Context;

        //reserved keys (the HUD tab-switch letters) are swallowed by a literal handler before the resolver runs,
        //so a binding onto one would be dead — block Set outright.
        if (Keybinds.IsShadowed(context, chord))
        {
            StatusLabel.Text = "Reserved by HUD tab keys — pick another";
            StatusLabel.ForegroundColor = LegendColors.Red;
            SetButton.SetEnabled(false);

            return;
        }

        //soft warnings — the bind is allowed but flagged, so a green "OK" never hides a live collision:
        //  · a conflict (another command fires on the same keystroke), or
        //  · a number-row key the slot/emote hotkeys contest while a HUD panel is open.
        var warnings = new List<string>();

        var conflicts = Keybinds.FindConflicts(Command, chord, context);

        if (conflicts.Count > 0)
            warnings.Add($"Also used by: {string.Join(", ", conflicts.Select(id => KeybindCatalog.Entry(id)?.DisplayName ?? id.ToString()))}");

        if (Keybinds.UsesContestedNumberRow(context, chord))
            warnings.Add("May be overridden by slot/emote hotkeys");

        if (warnings.Count > 0)
        {
            StatusLabel.Text = string.Join(" · ", warnings);
            StatusLabel.ForegroundColor = TextColors.Shout;
        } else
        {
            StatusLabel.Text = "OK";
            StatusLabel.ForegroundColor = LegendColors.SpringGreen;
        }

        SetButton.SetEnabled(true);
    }

    private void ApplyCapture()
    {
        //re-validate the reserved guard at commit time; Set is disabled for reserved/uncaptured, but a direct
        //call must never push a dead binding.
        if (Captured is { } chord && !Keybinds.IsShadowed(Keybinds.Defaults[Command].Context, chord))
            Committed?.Invoke(Command, Slot, chord);

        Hide();
    }
}
