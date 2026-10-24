#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Rendering;
using Brigid.Systems;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A primitive, asset-free theme choice: a label, a drop-down of theme entries and a hint, in one row. The host owns
///     the value: it gives the entries, the stored value and a test for the active theme, and handles
///     <see cref="ThemeChosen" />. The hint "Restart to apply" shows while the chosen entry is not the active theme, because
///     a choice applies the next time Brigid starts. A stored value that no entry has selects the first entry.
///     <para>
///         The row is as tall as the drop-down: while the list is open, the row grows with it, so the list draws and
///         takes clicks below the row. Give the row a <see cref="UIElement.ZIndex" /> above the siblings that the open list
///         covers. The children are named <c>label</c>, <c>choice</c> and <c>hint</c>.
///     </para>
/// </summary>
public sealed class ThemePicker : UIPanel
{
    private const int LABEL_W = 56;
    private const int CHOICE_W = 180;
    private const int GAP = 8;

    private readonly UiThemeChoices.Entry[] Entries;
    private readonly Func<string?, bool> IsActive;
    private readonly UILabel Label;
    private readonly UILabel Hint;

    /// <summary>Raised when the player chooses an entry, with its value.</summary>
    public event Action<string?>? ThemeChosen;

    /// <summary>The drop-down. Internal for the tests.</summary>
    internal DropDown Choice { get; }

    /// <summary>True while the hint shows. Internal for the tests.</summary>
    internal bool ShowsHint => Hint.Visible;

    /// <param name="width">Width of the row.</param>
    /// <param name="rowHeight">Height of the row, and of the drop-down box and its list rows.</param>
    /// <param name="entries">The entries, in order (<see cref="UiThemeChoices.EntriesFor" />). The first entry is the default.</param>
    /// <param name="stored">The stored value. A value that no entry has (case ignored) selects the first entry.</param>
    /// <param name="isActive">True when the theme of a value is the active theme.</param>
    public ThemePicker(int width, int rowHeight, IReadOnlyList<UiThemeChoices.Entry> entries, string? stored, Func<string?, bool> isActive)
    {
        Width = width;
        Height = rowHeight;
        Entries = [.. entries];
        IsActive = isActive;

        Label = new UILabel
        {
            Name = "label",
            Text = "Theme",
            X = 0,
            Y = 0,
            Width = LABEL_W,
            Height = rowHeight,
            PaddingLeft = 0,
            PaddingTop = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        Choice = new DropDown(Entries.Select(entry => entry.Label).ToArray(), CHOICE_W, rowHeight) { Name = "choice", X = LABEL_W, Y = 0 };
        Choice.SetSelected(UiThemeChoices.IndexOf(Entries, stored));
        Choice.SelectionChanged += OnSelectionChanged;
        //the list is clipped to this row and a click outside the row does not reach it, so the row grows with the list at
        //once: a row that grows in Update misses a click on the list in the frame that opened it
        Choice.OpenChanged += () => Height = Choice.Height;

        Hint = new UILabel
        {
            Name = "hint",
            Text = "Restart to apply",
            X = LABEL_W + CHOICE_W + GAP,
            Y = 0,
            Width = Math.Max(0, width - LABEL_W - CHOICE_W - GAP),
            Height = rowHeight,
            PaddingLeft = 0,
            PaddingTop = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        Label.SetForegroundRole(ThemeRoles.Text);
        Hint.SetForegroundRole(ThemeRoles.TextDisabled);

        AddChild(Label);
        AddChild(Hint);
        AddChild(Choice);
        UpdateHint();
    }

    /// <summary>Chooses an entry, as a click on it does. Internal for the tests.</summary>
    internal void Choose(int index)
    {
        Choice.SetSelected(index);
        OnSelectionChanged(index);
    }

    private void OnSelectionChanged(int index)
    {
        UpdateHint();
        ThemeChosen?.Invoke(Entries[index].Value);
    }

    private void UpdateHint() => Hint.Visible = (Choice.SelectedIndex >= 0) && !IsActive(Entries[Choice.SelectedIndex].Value);
}
