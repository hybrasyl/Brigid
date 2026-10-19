#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A primitive, asset-free drop-down list: a box that shows the selected entry, and a list of the entries that opens
///     below it. A click on the box opens or closes the list. A click on an entry selects it and closes the list. The list
///     also closes when the mouse leaves the control, when the control is disabled or hidden, and when the host calls
///     <see cref="Close" /> (for example on Escape).
///     <para>
///         While the list is open, the control is taller, so the list can cover the rows below it. Give the control a
///         <see cref="UIElement.ZIndex" /> above the siblings that the open list covers, so the list draws above them and
///         gets their clicks. The parent must be tall enough to hold the open list, because the list is clipped to the
///         parent.
///     </para>
///     <para>
///         The box uses the theme's button parts and the list uses <see cref="ThemeRoles.DropDownList" />. With no theme
///         part, each has the flat <see cref="DialogPalette" /> look.
///     </para>
/// </summary>
public sealed class DropDown : UIPanel
{
    private const int TEXT_INSET = 4;
    private const int ARROW_W = 7;

    private readonly string[] Items;
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
        RowHeight = rowHeight;
        Width = width;
        Height = rowHeight;
        SelectedIndex = Items.Length > 0 ? 0 : -1;
    }

    /// <summary>Moves the selection without raising <see cref="SelectionChanged" />. An index out of range is ignored.</summary>
    public void SetSelected(int index)
    {
        if ((index >= 0) && (index < Items.Length))
            SelectedIndex = index;
    }

    /// <summary>Opens the list. A control that has no entries or is disabled does not open.</summary>
    public void Open()
    {
        if (IsOpen || (Items.Length == 0) || !Enabled)
            return;

        IsOpen = true;
        HoveredRow = -1;
        Height = RowHeight * (Items.Length + 1);
    }

    /// <summary>Closes the list. Does nothing when it is closed.</summary>
    public void Close()
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        HoveredRow = -1;
        Height = RowHeight;
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
            DrawBorderClipped(spriteBatch, box, Enabled ? DialogPalette.FrameBorder : DialogPalette.Divider);
        }

        var textColor = Enabled
            ? ThemeColor(ThemeRoles.Text, LegendColors.White)
            : ThemeColor(ThemeRoles.TextDisabled, DialogPalette.DisabledText);

        DrawRow(spriteBatch, box, SelectedText, textColor);
        DrawArrow(spriteBatch, box, textColor);
    }

    private void DrawList(SpriteBatch spriteBatch, Rectangle list)
    {
        if (!TryDrawThemePart(spriteBatch, ThemeRoles.DropDownList, list))
        {
            DrawRectClipped(spriteBatch, list, DialogPalette.FrameFill);
            DrawBorderClipped(spriteBatch, list, DialogPalette.FrameBorder);
        }

        for (var i = 0; i < Items.Length; i++)
        {
            var row = new Rectangle(list.X, list.Y + i * RowHeight, list.Width, RowHeight);
            var selected = i == SelectedIndex;

            if (i == HoveredRow)
                DrawRectClipped(spriteBatch, Inner(row), DialogPalette.RowHoverFill);
            else if (selected)
                DrawRectClipped(spriteBatch, Inner(row), DialogPalette.RowSelectedFill);

            var color = selected
                ? ThemeColor(ThemeRoles.TextSelected, DialogPalette.SelectedText)
                : ThemeColor(ThemeRoles.Text, LegendColors.White);

            DrawRow(spriteBatch, row, Items[i], color);
        }
    }

    //a row highlight keeps clear of the 1 px border
    private static Rectangle Inner(Rectangle row) => new(row.X + 1, row.Y, row.Width - 2, row.Height);

    private void DrawRow(SpriteBatch spriteBatch, Rectangle row, string text, Color color)
        => DrawTextClipped(
            spriteBatch,
            new Vector2(row.X + TEXT_INSET, row.Y + (row.Height - TextRenderer.CHAR_HEIGHT) / 2),
            text,
            color,
            colorCodesEnabled: false);

    //a small down-pointing triangle at the right of the box, drawn as rows of pixels so it needs no glyph
    private void DrawArrow(SpriteBatch spriteBatch, Rectangle box, Color color)
    {
        var rows = (ARROW_W + 1) / 2;
        var left = box.Right - TEXT_INSET - ARROW_W;
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
        HoveredRow = IsOpen ? RowAt(e.ScreenY) : -1;
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
        Close();

        if ((row < 0) || (row == SelectedIndex))
            return;

        SelectedIndex = row;
        SelectionChanged?.Invoke(row);
    }
}
