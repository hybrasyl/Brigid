#region
using Brigid.Controls.Components;
using Brigid.Data.AssetPacks;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

    private readonly Entry[] Entries;
    private readonly Func<string?, bool> IsActive;
    private readonly UILabel Label;
    private readonly UILabel Hint;

    /// <summary>One entry: the stored value (null for the default) and the text that the list shows.</summary>
    public readonly record struct Entry(string? Value, string Label);

    /// <summary>Raised when the player chooses an entry, with its value.</summary>
    public event Action<string?>? ThemeChosen;

    /// <summary>The drop-down. Internal for the tests.</summary>
    internal DropDown Choice { get; }

    /// <summary>True while the hint shows. Internal for the tests.</summary>
    internal bool ShowsHint => Hint.Visible;

    /// <summary>True while the list is open.</summary>
    public bool IsOpen => Choice.IsOpen;

    /// <param name="width">Width of the row.</param>
    /// <param name="rowHeight">Height of the row, and of the drop-down box and its list rows.</param>
    /// <param name="entries">The entries, in order. The first entry is the default.</param>
    /// <param name="stored">The stored value. A value that no entry has (case ignored) selects the first entry.</param>
    /// <param name="isActive">True when the theme of a value is the active theme.</param>
    public ThemePicker(int width, int rowHeight, IReadOnlyList<Entry> entries, string? stored, Func<string?, bool> isActive)
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
        Choice.SetSelected(IndexOf(Entries, stored));
        Choice.SelectionChanged += OnSelectionChanged;

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

        AddChild(Label);
        AddChild(Hint);
        AddChild(Choice);
        UpdateHint();
    }

    /// <summary>
    ///     The entries for <paramref name="themes" />, in their order: Automatic (the first theme), each theme by its display
    ///     name, and None (the flat look). Two themes with the same display name show their <c>pack_id</c> too.
    /// </summary>
    public static List<Entry> EntriesFor(IEnumerable<AssetPackManifest> themes)
    {
        var list = themes.ToList();

        var shared = list.GroupBy(manifest => manifest.DisplayName, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1)
                         .Select(group => group.Key)
                         .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            new Entry(null, "Automatic"),
            .. list.Select(manifest => new Entry(
                manifest.PackId,
                shared.Contains(manifest.DisplayName) ? $"{manifest.DisplayName} ({manifest.PackId})" : manifest.DisplayName)),
            new Entry(AssetPackRegistry.NO_UI_THEME, "None")
        ];
    }

    /// <summary>
    ///     The index of the entry with the stored value (case ignored). A value that no entry has, or no value, gives the
    ///     first entry.
    /// </summary>
    public static int IndexOf(IReadOnlyList<Entry> entries, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return 0;

        for (var i = 0; i < entries.Count; i++)
            if (string.Equals(entries[i].Value, stored.Trim(), StringComparison.OrdinalIgnoreCase))
                return i;

        return 0;
    }

    /// <summary>Closes the open list, for example on Escape.</summary>
    public void Close() => Choice.Close();

    /// <summary>Chooses an entry, as a click on it does. Internal for the tests.</summary>
    internal void Choose(int index)
    {
        Choice.SetSelected(index);
        OnSelectionChanged(index);
    }

    public override void Update(GameTime gameTime)
    {
        //the list is clipped to this row and a click outside the row does not reach it, so the row grows with the list
        Height = Choice.Height;

        base.Update(gameTime);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        //the theme can come from a parent, which can change after construction, so the colours are resolved per draw
        Label.ForegroundColor = ThemeColor(ThemeRoles.Text);
        Hint.ForegroundColor = ThemeColor(ThemeRoles.TextDisabled);

        base.Draw(spriteBatch);
    }

    private void OnSelectionChanged(int index)
    {
        UpdateHint();
        ThemeChosen?.Invoke(Entries[index].Value);
    }

    private void UpdateHint() => Hint.Visible = (Choice.SelectedIndex >= 0) && !IsActive(Entries[Choice.SelectedIndex].Value);
}
