#region
using Brigid.Controls.Components;
using Brigid.Controls.Scrolling;
using Brigid.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Boards;

/// <summary>
///     Board selection panel using _nbdlist prefab. Displays a scrollable list of available bulletin boards. View button
///     opens the selected board; Quit closes. Slides in from the right edge of the viewport.
/// </summary>
public sealed class BoardListControl : PrefabPanel
{
    private const int ROW_HEIGHT = Constants.BOARD_ROW_HEIGHT;

    private static readonly Color SelectedColor = new(100, 149, 237);

    private readonly VirtualizedListView<(ushort BoardId, string Name), UILabel> ListView;
    private List<(ushort BoardId, string Name)> Boards = [];
    private SlideAnimator Slide;
    private bool SlideMode;

    public UIButton? QuitButton { get; }
    public UIButton? ViewButton { get; }

    public BoardListControl()
        : base("_nbdlist", false)
    {
        Name = "BoardList";
        Visible = false;
        UsesControlStack = true;

        ViewButton = CreateButton("View");
        QuitButton = CreateButton("Quit");

        if (QuitButton is not null)
            QuitButton.Clicked += Close;

        var boardListRect = GetRect("BoardList");

        ListView = new VirtualizedListView<(ushort BoardId, string Name), UILabel>(
            boardListRect,
            ROW_HEIGHT,
            w => new UILabel
            {
                Width = w,
                Height = ROW_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0
            },
            BindRow)
        {
            Selectable = true,
            //match the legacy guard: no row selection/activation while the panel is sliding in/out
            InteractionGate = () => !Slide.Sliding
        };

        ListView.SelectionChanged += _ => UpdateButtonStates();
        ListView.ItemActivated += ActivateBoard;
        AddChild(ListView);

        if (ViewButton is not null)
            ViewButton.Clicked += () => ActivateBoard(ListView.SelectedIndex);
    }

    private static void BindRow(UILabel label, VirtualRow<(ushort BoardId, string Name)> row)
    {
        if (row.Kind == VirtualRowKind.Item)
        {
            label.ForegroundColor = row.Selected ? SelectedColor : TextColors.Default;
            label.Text = row.Item.Name;
        } else
            label.Text = string.Empty;
    }

    private void ActivateBoard(int index)
    {
        if ((index >= 0) && (index < Boards.Count))
            OnViewBoard?.Invoke(Boards[index].BoardId);
    }

    public void Close()
    {
        InputDispatcher.Instance?.RemoveControl(this);

        if (SlideMode)
            Slide.SlideOut();
        else
        {
            Slide.Hide(this);
            OnClose?.Invoke();
        }
    }

    public override void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        Slide.Hide(this);
    }

    public event CloseHandler? OnClose;
    public event ViewBoardHandler? OnViewBoard;

    public void SetViewportBounds(Rectangle viewport)
    {
        Slide.SetViewportBounds(viewport, Width);
        Y = viewport.Y;
    }

    public override void Show()
    {
        if (!Visible)
        {
            SlideMode = true;
            InputDispatcher.Instance?.PushControl(this);
            Slide.SlideIn(this);
        }
    }

    public void ShowBoards(List<(ushort BoardId, string Name)> boards, bool slide = true)
    {
        Boards = boards;
        ListView.SetItems(boards);
        ListView.SetSelectedIndex(boards.Count > 0 ? 0 : -1);
        UpdateButtonStates();

        if (slide)
            Show();
        else
        {
            SlideMode = false;
            InputDispatcher.Instance?.PushControl(this);
            Slide.ShowInPlace(this);
        }
    }

    public void SlideClose() => Close();

    public override void Update(GameTime gameTime)
    {
        if (!Visible || !Enabled)
            return;

        if (Slide.Update(gameTime, this))
        {
            OnClose?.Invoke();

            return;
        }

        base.Update(gameTime);
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (Slide.Sliding)
            return;

        if (e.Key == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void UpdateButtonStates()
    {
        if (ViewButton is not null)
            ViewButton.Enabled = ListView.SelectedIndex >= 0;
    }
}
