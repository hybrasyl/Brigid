#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Extensions;
using Chaos.Client.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Boards;

/// <summary>
///     Board selection panel using _nbdlist prefab. Displays a scrollable list of available bulletin boards. View button
///     opens the selected board; Quit closes. Slides in from the right edge of the viewport.
/// </summary>
public sealed class BoardListControl : PrefabPanel
{
    private const int ROW_HEIGHT = Constants.BOARD_ROW_HEIGHT;

    private readonly Rectangle BoardListRect;
    private readonly int MaxVisibleRows;
    private readonly UILabel[] RowLabels;
    private readonly ScrollBarControl ScrollBar;
    private List<(ushort BoardId, string Name)> Boards = [];
    private int DataVersion;
    private int RenderedVersion = -1;
    private int ScrollOffset;
    private int SelectedIndex = -1;
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

        if (ViewButton is not null)
            ViewButton.Clicked += () =>
            {
                if ((SelectedIndex >= 0) && (SelectedIndex < Boards.Count))
                    OnViewBoard?.Invoke(Boards[SelectedIndex].BoardId);
            };

        BoardListRect = GetRect("BoardList");
        MaxVisibleRows = BoardListRect.Height > 0 ? BoardListRect.Height / ROW_HEIGHT : 0;

        RowLabels = new UILabel[MaxVisibleRows];

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            RowLabels[i] = new UILabel
            {
                X = BoardListRect.X,
                Y = BoardListRect.Y + i * ROW_HEIGHT,
                Width = BoardListRect.Width - ScrollBarControl.DEFAULT_WIDTH,
                Height = ROW_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0
            };

            AddChild(RowLabels[i]);
        }

        ScrollBar = new ScrollBarControl
        {
            X = BoardListRect.AlignRight(ScrollBarControl.DEFAULT_WIDTH),
            Y = BoardListRect.Y,
            Height = BoardListRect.Height
        };

        ScrollBar.OnValueChanged += v => { ScrollOffset = v; DataVersion++; };
        AddChild(ScrollBar);
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

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        RefreshLabels();
        base.Draw(spriteBatch);
    }

    public override void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        Slide.Hide(this);
    }

    public event CloseHandler? OnClose;
    public event ViewBoardHandler? OnViewBoard;

    private void RefreshLabels()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            var boardIndex = ScrollOffset + i;

            if (boardIndex < Boards.Count)
            {
                var textColor = boardIndex == SelectedIndex ? new Color(100, 149, 237) : TextColors.Default;

                RowLabels[i].ForegroundColor = textColor;
                RowLabels[i].Text = Boards[boardIndex].Name;
            } else
                RowLabels[i].Text = string.Empty;
        }
    }

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
        SelectedIndex = boards.Count > 0 ? 0 : -1;
        ScrollOffset = 0;
        DataVersion++;
        ScrollBar.Value = 0;
        ScrollBar.TotalItems = boards.Count;
        ScrollBar.VisibleItems = MaxVisibleRows;
        ScrollBar.MaxValue = Math.Max(0, boards.Count - MaxVisibleRows);
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

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        if (ScrollBar.TotalItems <= ScrollBar.VisibleItems)
            return;

        var newValue = Math.Clamp(ScrollBar.Value - e.Delta, 0, ScrollBar.MaxValue);

        if (newValue != ScrollBar.Value)
        {
            ScrollBar.Value = newValue;
            ScrollOffset = newValue;
            DataVersion++;
        }

        e.Handled = true;
    }

    public override void OnClick(ClickEvent e)
    {
        base.OnClick(e);

        if (Slide.Sliding || (e.Button != MouseButton.Left))
            return;

        var localX = e.ScreenX - ScreenX - BoardListRect.X;
        var localY = e.ScreenY - ScreenY - BoardListRect.Y;

        if ((localX < 0) || (localX >= BoardListRect.Width) || (localY < 0) || (localY >= BoardListRect.Height))
            return;

        var row = localY / ROW_HEIGHT;

        if (row >= MaxVisibleRows)
            return;

        var entryIndex = ScrollOffset + row;

        if (entryIndex >= Boards.Count)
            return;

        SelectedIndex = entryIndex;
        DataVersion++;
        UpdateButtonStates();
    }

    public override void OnDoubleClick(DoubleClickEvent e)
    {
        base.OnDoubleClick(e);

        if (Slide.Sliding || (e.Button != MouseButton.Left))
            return;

        var localX = e.ScreenX - ScreenX - BoardListRect.X;
        var localY = e.ScreenY - ScreenY - BoardListRect.Y;

        if ((localX < 0) || (localX >= BoardListRect.Width) || (localY < 0) || (localY >= BoardListRect.Height))
            return;

        var row = localY / ROW_HEIGHT;

        if (row >= MaxVisibleRows)
            return;

        var entryIndex = ScrollOffset + row;

        if (entryIndex >= Boards.Count)
            return;

        SelectedIndex = entryIndex;
        DataVersion++;
        UpdateButtonStates();
        OnViewBoard?.Invoke(Boards[entryIndex].BoardId);
    }

    private void UpdateButtonStates()
    {
        var hasSelection = SelectedIndex >= 0;

        ViewButton?.Enabled = hasSelection;
    }
}