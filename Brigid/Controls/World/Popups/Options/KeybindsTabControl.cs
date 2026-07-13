#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.Systems.Keybinds;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Popups.Options;

/// <summary>
///     The Options modal's Keybinds tab: a scrollable, section-grouped list of every rebindable command with
///     its current chord(s) and a per-command reset. Wraps the shared
///     <see cref="VirtualizedListView{TItem,TRow}" /> (row pool + scroll bar + wheel), supplying a single
///     row type that renders either a section header or a command row.
///     <para>
///         A command row shows one chord field (single-chord commands) or two — Primary and Alternate — for
///         commands whose default has a secondary (movement). A field click raises <see cref="ChordActivated" />
///         with the slot; the reset button raises <see cref="ResetActivated" />. The host (Options modal) opens
///         the capture modal / applies the reset and calls <see cref="Refresh" /> to repaint.
///     </para>
/// </summary>
internal sealed class KeybindsTabControl : UIPanel
{
    private const int ROW_H = 20;
    private const int ROW_RIGHT_INSET = 4;

    //one display row: either a section header or a rebindable command.
    private readonly record struct Row(bool IsHeader, string Header, CommandId Command);

    private readonly VirtualizedListView<Row, KeybindRowView> List;

    /// <summary>Raised when a command's chord field is clicked (the host arms chord capture for that slot).</summary>
    public event Action<CommandId, ChordSlot>? ChordActivated;

    /// <summary>Raised when a command's reset button is clicked (the host resets the binding + persists).</summary>
    public event Action<CommandId>? ResetActivated;

    public KeybindsTabControl(Rectangle bounds)
    {
        X = bounds.X;
        Y = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;

        List = new VirtualizedListView<Row, KeybindRowView>(
            new Rectangle(0, 0, Width, Height),
            ROW_H,
            (contentWidth, index) =>
            {
                var row = new KeybindRowView(contentWidth) { Name = $"KeybindRow{index}" };
                row.ChordClicked += (id, slot) => ChordActivated?.Invoke(id, slot);
                row.ResetClicked += id => ResetActivated?.Invoke(id);

                return row;
            },
            BindRow,
            rowInsetRight: ROW_RIGHT_INSET);

        AddChild(List);
        List.SetItems(BuildItems());
    }

    private static List<Row> BuildItems()
    {
        var items = new List<Row>();

        foreach (var section in KeybindCatalog.SectionOrder)
        {
            var entries = KeybindCatalog.Entries.Where(e => e.Section == section).ToList();

            if (entries.Count == 0)
                continue;

            items.Add(new Row(true, KeybindCatalog.SectionLabel(section), default));

            foreach (var entry in entries)
                items.Add(new Row(false, string.Empty, entry.Id));
        }

        return items;
    }

    //re-reads Effective(id) live on every bind, so Refresh() repaints current chords after a rebind.
    private static void BindRow(KeybindRowView row, VirtualRow<Row> slot)
    {
        if (slot.Kind == VirtualRowKind.Empty)
        {
            row.BindEmpty();

            return;
        }

        var item = slot.Item;

        if (item.IsHeader)
        {
            row.BindHeader(item.Header);

            return;
        }

        var name = KeybindCatalog.Entry(item.Command)?.DisplayName ?? item.Command.ToString();
        var binding = Keybinds.Effective(item.Command);
        var primary = KeybindCatalog.Format(binding.Primary);

        //a second field only for commands whose default carries a secondary; an empty override slot shows "—".
        string? secondary = Keybinds.SupportsSecondary(item.Command)
            ? binding.Secondary is { } alt ? KeybindCatalog.Format(alt) : "—"
            : null;

        row.BindCommand(item.Command, name, primary, secondary);
    }

    /// <summary>Repaints the visible rows, re-reading each command's current chord (call when the tab is shown).</summary>
    public void Refresh() => List.Refresh();

    /// <summary>
    ///     A pooled row that renders either a section header (a single spanning label) or a command
    ///     (name · primary chord · optional alternate chord · reset), toggled by
    ///     <see cref="BindHeader" />/<see cref="BindCommand" /> (or blanked by <see cref="BindEmpty" /> for an
    ///     out-of-range slot).
    /// </summary>
    private sealed class KeybindRowView : UIPanel
    {
        private const int LEFT_PAD = 6;
        private const int BUTTON_H = 16;
        private const int RESET_W = 24;
        private const int CHORD_W = 92;
        private const int INNER_GAP = 6;

        private readonly UILabel HeaderLabel;
        private readonly UILabel NameLabel;
        private readonly TextButton PrimaryButton;
        private readonly TextButton SecondaryButton;
        private readonly TextButton ResetButton;

        private CommandId Bound;

        public event Action<CommandId, ChordSlot>? ChordClicked;
        public event Action<CommandId>? ResetClicked;

        public KeybindRowView(int width)
        {
            Width = width;
            Height = ROW_H;

            var buttonY = (ROW_H - BUTTON_H) / 2;
            var resetX = width - RESET_W;
            var secondaryX = resetX - INNER_GAP - CHORD_W;
            var primaryX = secondaryX - INNER_GAP - CHORD_W;
            var nameW = primaryX - INNER_GAP - LEFT_PAD;

            HeaderLabel = new UILabel
            {
                X = 0,
                Y = 0,
                Width = width,
                Height = ROW_H,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                ForegroundColor = DialogPalette.Title,
                IsHitTestVisible = false,
                Visible = false
            };
            AddChild(HeaderLabel);

            NameLabel = new UILabel
            {
                X = LEFT_PAD,
                Y = 0,
                Width = nameW,
                Height = ROW_H,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                ForegroundColor = TextColors.Default,
                IsHitTestVisible = false
            };
            AddChild(NameLabel);

            PrimaryButton = new TextButton(string.Empty, CHORD_W, BUTTON_H) { X = primaryX, Y = buttonY };
            PrimaryButton.Clicked += () => ChordClicked?.Invoke(Bound, ChordSlot.Primary);
            AddChild(PrimaryButton);

            SecondaryButton = new TextButton(string.Empty, CHORD_W, BUTTON_H) { X = secondaryX, Y = buttonY };
            SecondaryButton.Clicked += () => ChordClicked?.Invoke(Bound, ChordSlot.Secondary);
            AddChild(SecondaryButton);

            ResetButton = new TextButton("↺", RESET_W, BUTTON_H) { X = resetX, Y = buttonY };
            ResetButton.Clicked += () => ResetClicked?.Invoke(Bound);
            AddChild(ResetButton);
        }

        public void BindEmpty()
        {
            HeaderLabel.Visible = false;
            NameLabel.Visible = false;
            PrimaryButton.Visible = false;
            SecondaryButton.Visible = false;
            ResetButton.Visible = false;
        }

        public void BindHeader(string header)
        {
            HeaderLabel.Text = header;
            HeaderLabel.Visible = true;
            NameLabel.Visible = false;
            PrimaryButton.Visible = false;
            SecondaryButton.Visible = false;
            ResetButton.Visible = false;
        }

        public void BindCommand(CommandId id, string name, string primary, string? secondary)
        {
            Bound = id;
            NameLabel.Text = name;
            PrimaryButton.SetText(primary);
            HeaderLabel.Visible = false;
            NameLabel.Visible = true;
            PrimaryButton.Visible = true;
            ResetButton.Visible = true;

            //secondary field shown only for two-chord commands; single-chord rows leave the slot empty.
            if (secondary is null)
                SecondaryButton.Visible = false;
            else
            {
                SecondaryButton.SetText(secondary);
                SecondaryButton.Visible = true;
            }
        }
    }
}
