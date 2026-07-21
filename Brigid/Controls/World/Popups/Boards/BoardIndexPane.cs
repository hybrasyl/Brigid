#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Popups.Boards;

/// <summary>
///     The list of available bulletin boards — the first view of the Boards tab, replacing the <c>_nbdlist</c>
///     prefab panel. Selecting a row and activating it (double-click, Enter, or the modal's View action) raises
///     <see cref="BoardActivated" />; the host requests that board and the modal swaps to
///     <see cref="PostListPane" />.
/// </summary>
internal sealed class BoardIndexPane : UIPanel
{
    private const int ROW_HEIGHT = Constants.BOARD_ROW_HEIGHT;
    private const int ROW_INSET_X = 4;

    private readonly VirtualizedListView<(ushort BoardId, string Name), UILabel> ListView;

    private List<(ushort BoardId, string Name)> Boards = [];

    /// <summary>Raised when a board row is activated. The parameter is the board id to request.</summary>
    public event ViewBoardHandler? BoardActivated;

    /// <summary>Raised when the selection changes, so the modal can enable/disable its View action.</summary>
    public event Action? SelectionChanged;

    /// <summary>Whether a row is selected — gates the modal's View action.</summary>
    public bool HasSelection => (ListView.SelectedIndex >= 0) && (ListView.SelectedIndex < Boards.Count);

    public BoardIndexPane(Rectangle pane)
    {
        X = pane.X;
        Y = pane.Y;
        Width = pane.Width;
        Height = pane.Height;

        ListView = new VirtualizedListView<(ushort BoardId, string Name), UILabel>(
            new Rectangle(0, 0, pane.Width, pane.Height),
            ROW_HEIGHT,
            (w, i) => new UILabel
            {
                Name = $"Board{i}",
                Width = w,
                Height = ROW_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0,
                VerticalAlignment = VerticalAlignment.Center
            },
            BindRow,
            ROW_INSET_X)
        {
            Selectable = true
        };

        ListView.SelectionChanged += _ => SelectionChanged?.Invoke();
        ListView.ItemActivated += ActivateBoard;

        AddChild(ListView);
    }

    private static void BindRow(UILabel label, VirtualRow<(ushort BoardId, string Name)> row)
    {
        if (row.Kind != VirtualRowKind.Item)
        {
            label.Text = string.Empty;

            return;
        }

        label.Text = row.Item.Name;
        label.ForegroundColor = row.Selected ? DialogPalette.SelectedText : TextColors.Default;
    }

    /// <summary>Populates the pane and selects the first board (matching the legacy panel).</summary>
    public void SetBoards(List<(ushort BoardId, string Name)> boards)
    {
        Boards = [..boards];
        ListView.SetItems(Boards);
        ListView.SetSelectedIndex(boards.Count > 0 ? 0 : -1);
        SelectionChanged?.Invoke();
    }

    /// <summary>Activates the selected board (the modal's View action).</summary>
    public void ActivateSelected() => ActivateBoard(ListView.SelectedIndex);

    private void ActivateBoard(int index)
    {
        if ((index >= 0) && (index < Boards.Count))
            BoardActivated?.Invoke(Boards[index].BoardId);
    }

    /// <summary>Moves the selection by <paramref name="delta" /> rows, keeping it on screen.</summary>
    public void MoveSelection(int delta) => ListView.MoveSelection(delta);

    /// <summary>Rows visible at once — the paging step for PageUp/PageDown.</summary>
    public int VisibleRows => ListView.VisibleRows;
}
