#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A primitive, asset-free drop-down list: a box that shows the selected entry, and a list of the entries that opens
///     below it. A click on the box opens or closes the list. A click on an entry selects it and closes the list. The list
///     also closes when the mouse leaves the control, when the control is disabled or hidden, when the host calls
///     <see cref="Close" />, and on Escape: while the list is open, the control is on the control stack, as
///     <see cref="PopupMenu" /> is, so it gets Escape before its host (BRIG-135). The other keys go on to the host. A
///     focused text box in the host keeps its focus while the list is open, so the player can still type in it; a text
///     box lets Escape through, so the list still closes on Escape.
///     <para>
///         While the list is open, the control is taller, so the list can cover the rows below it. Give the control a
///         <see cref="UIElement.ZIndex" /> above the siblings that the open list covers, so the list draws above them and
///         gets their clicks. The parent must be tall enough to hold the open list, because the list is clipped to the
///         parent.
///     </para>
///     <para>
///         The box uses the theme's button parts and the list uses <see cref="ThemeRoles.DropDownList" />. With no theme
///         part, each has the flat <see cref="DialogPalette" /> look. The text and the arrow keep clear of the border art
///         of a sliced part.
///     </para>
///     <para>
///         An entry can be disabled with <see cref="SetEntryEnabled" />. A disabled entry shows in the list in the
///         disabled text colour and in italics, does not highlight under the mouse, and the player cannot select it. A
///         click on it leaves the list open.
///     </para>
/// </summary>
public sealed class DropDown : UIPanel
{
    private const int TEXT_INSET = 4;
    private const int ARROW_W = 7;

    private readonly string[] Items;
    private readonly bool[] DisabledItems;
    private readonly int RowHeight;

    private bool BoxHovered;
    private int HoveredRow = -1;

    /// <summary>Raised when the player selects an entry, with its index. Not raised by <see cref="SetSelected" />.</summary>
    public event Action<int>? SelectionChanged;

    /// <summary>The selected entry index, or -1 when there are no entries.</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>The text of the selected entry, or an empty string when there are no entries.</summary>
    public string SelectedText => SelectedIndex >= 0 ? Items[SelectedIndex] : string.Empty;

    /// <summary>True while the list is open.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The entries, in order.</summary>
    public IReadOnlyList<string> Entries => Items;

    /// <param name="items">The entries, in order. The first entry is selected.</param>
    /// <param name="width">Width of the box and of the list.</param>
    /// <param name="rowHeight">Height of the box and of each list row. Must be more than 0.</param>
    public DropDown(IReadOnlyList<string> items, int width, int rowHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowHeight);

        Items = [.. items];
        DisabledItems = new bool[Items.Length];
        RowHeight = rowHeight;
        Width = width;
        Height = rowHeight;
        SelectedIndex = Items.Length > 0 ? 0 : -1;
    }

    /// <summary>
    ///     Moves the selection without raising <see cref="SelectionChanged" />. An index out of range is ignored. The host can
    ///     select a disabled entry, for example a value that the server set; only the player cannot.
    /// </summary>
    public void SetSelected(int index)
    {
        if ((index >= 0) && (index < Items.Length))
            SelectedIndex = index;
    }

    /// <summary>Enables or disables the entry at <paramref name="index" />. An index out of range is ignored.</summary>
    public void SetEntryEnabled(int index, bool enabled)
    {
        if ((index >= 0) && (index < Items.Length))
            DisabledItems[index] = !enabled;
    }

    /// <summary>False when the entry at <paramref name="index" /> is disabled or out of range.</summary>
    public bool IsEntryEnabled(int index) => (index >= 0) && (index < Items.Length) && !DisabledItems[index];

    /// <summary>Opens the list. A control that has no entries or is disabled does not open.</summary>
    public void Open()
    {
        if (IsOpen || (Items.Length == 0) || !Enabled)
            return;

        IsOpen = true;
        HoveredRow = -1;
        Height = RowHeight * (Items.Length + 1);
        InputDispatcher.Instance?.PushControl(this, keepTextFocus: true);
    }

    /// <summary>Closes the list. Does nothing when it is closed.</summary>
    public void Close()
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        HoveredRow = -1;
        Height = RowHeight;
        InputDispatcher.Instance?.RemoveControl(this);
    }

    //Escape closes the open list and goes no further; the other keys go on to the host
    public override void OnKeyDown(KeyDownEvent e)
    {
        if (IsOpen && (e.Key == Keys.Escape))
        {
            Close();
            e.Handled = true;

            return;
        }

        base.OnKeyDown(e);
    }

    public override void ResetInteractionState()
    {
        BoxHovered = false;
        Close();
    }

    public override void Update(GameTime gameTime)
    {
        if (IsOpen && !Enabled)
            Close();

        base.Update(gameTime);
    }

    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        DrawBox(spriteBatch, new Rectangle(bounds.X, bounds.Y, bounds.Width, RowHeight));

        if (IsOpen)
            DrawList(spriteBatch, new Rectangle(bounds.X, bounds.Y + RowHeight, bounds.Width, RowHeight * Items.Length));
    }

    private void DrawBox(SpriteBatch spriteBatch, Rectangle box)
    {
        var role = !Enabled ? ThemeRoles.ButtonDisabled
            : IsOpen ? ThemeRoles.ButtonPressed
            : BoxHovered ? ThemeRoles.ButtonHover : ThemeRoles.ButtonNormal;

        if (!TryDrawThemePart(spriteBatch, role, box, ThemeRoles.ButtonNormal))
        {
            var fill = !Enabled ? DialogPalette.ButtonIdleFill
                : IsOpen ? DialogPalette.ButtonPressFill
                : BoxHovered ? DialogPalette.ButtonHoverFill : DialogPalette.ButtonIdleFill;

            DrawRectClipped(spriteBatch, box, fill);
            DrawBorderClipped(
                spriteBatch,
                box,
                Enabled ? ThemeColor(ThemeRoles.Border) : ThemeColor(ThemeRoles.Divider));
        }

        var textColor = Enabled
            ? ThemeColor(ThemeRoles.Text)
            : ThemeColor(ThemeRoles.TextDisabled);

        var inset = ContentInset(ThemeRoles.ButtonNormal);
        DrawRow(spriteBatch, box, SelectedText, textColor, inset, FontStyle.Regular);
        DrawArrow(spriteBatch, box, textColor, inset);
    }

    private void DrawList(SpriteBatch spriteBatch, Rectangle list)
    {
        if (!TryDrawThemePart(spriteBatch, ThemeRoles.DropDownList, list))
        {
            DrawRectClipped(spriteBatch, list, DialogPalette.FrameFill);
            DrawBorderClipped(spriteBatch, list, ThemeColor(ThemeRoles.Border));
        }

        var inset = ContentInset(ThemeRoles.DropDownList);

        for (var i = 0; i < Items.Length; i++)
        {
            var row = new Rectangle(list.X, list.Y + i * RowHeight, list.Width, RowHeight);
            var selected = i == SelectedIndex;

            if (i == HoveredRow)
                DrawRectClipped(spriteBatch, Inner(row), ThemeColor(ThemeRoles.RowHover));
            else if (selected)
                DrawRectClipped(spriteBatch, Inner(row), ThemeColor(ThemeRoles.RowSelected));

            if (DisabledItems[i])
            {
                DrawRow(spriteBatch, row, Items[i], ThemeColor(ThemeRoles.TextDisabled), inset, FontStyle.Italic);

                continue;
            }

            var color = selected
                ? ThemeColor(ThemeRoles.TextSelected)
                : ThemeColor(ThemeRoles.Text);

            DrawRow(spriteBatch, row, Items[i], color, inset, FontStyle.Regular);
        }
    }

    //a row highlight keeps clear of the 1 px border
    private static Rectangle Inner(Rectangle row) => new(row.X + 1, row.Y, row.Width - 2, row.Height);

    //the text and the arrow keep clear of the border art of a sliced theme part
    private int ContentInset(string role) => TryGetThemeBorder(role, out var border) ? Math.Max(TEXT_INSET, border) : TEXT_INSET;

    private void DrawRow(SpriteBatch spriteBatch, Rectangle row, string text, Color color, int inset, FontStyle style)
        => FontEngine.Instance.DrawLine(
            spriteBatch,
            text,
            new Vector2(row.X + inset, row.Y + (row.Height - TextRenderer.CHAR_HEIGHT) / 2),
            color,
            ClipRect,
            style: style);

    //a small down-pointing triangle at the right of the box, drawn as rows of pixels so it needs no glyph
    private void DrawArrow(SpriteBatch spriteBatch, Rectangle box, Color color, int inset)
    {
        var rows = (ARROW_W + 1) / 2;
        var left = box.Right - inset - ARROW_W;
        var top = box.Y + (box.Height - rows) / 2;

        for (var i = 0; i < rows; i++)
            DrawRectClipped(spriteBatch, new Rectangle(left + i, top + i, ARROW_W - 2 * i, 1), color);
    }

    //the list row under a point, or -1 for the box or outside the list
    private int RowAt(int screenY)
    {
        var row = (screenY - ScreenY) / RowHeight - 1;

        return (row >= 0) && (row < Items.Length) ? row : -1;
    }

    public override void OnMouseMove(MouseMoveEvent e)
    {
        BoxHovered = e.ScreenY < ScreenY + RowHeight;

        var row = IsOpen ? RowAt(e.ScreenY) : -1;
        HoveredRow = IsEntryEnabled(row) ? row : -1;
    }

    public override void OnMouseLeave()
    {
        BoxHovered = false;
        Close();
    }

    public override void OnMouseDown(MouseDownEvent e)
    {
        if (e.Button == MouseButton.Left)
            e.Handled = true;
    }

    public override void OnClick(ClickEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        e.Handled = true;

        if (!Enabled)
            return;

        if (!IsOpen)
        {
            Open();

            return;
        }

        var row = RowAt(e.ScreenY);

        if ((row >= 0) && DisabledItems[row])
            return;

        Close();

        if ((row < 0) || (row == SelectedIndex))
            return;

        SelectedIndex = row;
        SelectionChanged?.Invoke(row);
    }
}
