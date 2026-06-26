#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Boards;

/// <summary>
///     Article reading panel using _narti prefab. Displays a public board post with author, title, date, and body text.
///     Buttons: Prev, Next, New, Delete, Up (back to list), Close.
/// </summary>
public sealed class ArticleReadControl : PrefabPanel
{
    private readonly UILabel? AuthorLabel;
    private readonly UILabel BodyLabel;
    private readonly UILabel? DateLabel;
    private readonly ScrollBarControl ScrollBar;
    private readonly UILabel? TitleLabel;
    private readonly int VisibleHeight;
    private int TargetX;

    public ushort BoardId { get; set; }
    public string CurrentAuthor { get; private set; } = string.Empty;
    public short CurrentPostId { get; private set; }
    public UIButton? CloseButton { get; }
    public UIButton? DeleteButton { get; }
    public UIButton? NewButton { get; }
    public UIButton? NextButton { get; }
    public UIButton? PrevButton { get; }
    public UIButton? UpButton { get; }

    public ArticleReadControl()
        : base("_narti", false)
    {
        Name = "ArticleRead";
        Visible = false;
        UsesControlStack = true;

        PrevButton = CreateButton("Prev");
        NextButton = CreateButton("Next");
        NewButton = CreateButton("New");
        DeleteButton = CreateButton("Delete");
        UpButton = CreateButton("Up");
        CloseButton = CreateButton("Close");

        if (CloseButton is not null)
            CloseButton.Clicked += () => OnClose?.Invoke();

        if (UpButton is not null)
            UpButton.Clicked += () => OnUp?.Invoke();

        if (PrevButton is not null)
            PrevButton.Clicked += () => OnPrev?.Invoke();

        if (NextButton is not null)
            NextButton.Clicked += () => OnNext?.Invoke();

        if (NewButton is not null)
            NewButton.Clicked += () => OnNewPost?.Invoke();

        if (DeleteButton is not null)
            DeleteButton.Clicked += () => OnDeletePost?.Invoke(CurrentPostId);

        //labels
        AuthorLabel = CreateLabel("Author");
        AuthorLabel?.ForegroundColor = LegendColors.White;
        
        TitleLabel = CreateLabel("Title");
        TitleLabel?.ForegroundColor = LegendColors.White;
        
        DateLabel = CreateLabel("Mmdd");
        DateLabel?.ForegroundColor = LegendColors.White;

        //body content area
        var contentRect = GetRect("Content");
        VisibleHeight = contentRect.Height;

        BodyLabel = new UILabel
        {
            X = contentRect.X,
            Y = contentRect.Y,
            Width = contentRect.Width - ScrollBarControl.DEFAULT_WIDTH,
            Height = contentRect.Height,
            PaddingLeft = 0,
            PaddingRight = 2,
            PaddingTop = 0,
            WordWrap = true,
            ForegroundColor = TextColors.Default,
            IsSelectable = true
        };

        AddChild(BodyLabel);

        ScrollBar = new ScrollBarControl
        {
            Name = "ScrollBar",
            X = contentRect.X + contentRect.Width - ScrollBarControl.DEFAULT_WIDTH,
            Y = contentRect.Y,
            Height = contentRect.Height
        };

        ScrollBar.OnValueChanged += v =>
        {
            BodyLabel.ScrollOffset = v * TextRenderer.CHAR_HEIGHT;
        };

        AddChild(ScrollBar);
    }

    public override void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        Visible = false;
    }

    public event CloseHandler? OnClose;
    public event DeletePostHandler? OnDeletePost;
    public event NewPostHandler? OnNewPost;
    public event NextHandler? OnNext;
    public event PrevHandler? OnPrev;
    public event UpHandler? OnUp;

    public void SetViewportBounds(Rectangle viewport)
    {
        TargetX = viewport.X + viewport.Width - Width;
        Y = viewport.Y;
    }

    public override void Show()
    {
        X = TargetX;
        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
    }

    /// <summary>
    ///     Displays a public board article.
    /// </summary>
    public void ShowArticle(
        short postId,
        string author,
        int month,
        int day,
        string subject,
        string message,
        bool enablePrev)
    {
        CurrentPostId = postId;
        CurrentAuthor = author;

        AuthorLabel?.Text = author;
        TitleLabel?.Text = subject;
        DateLabel?.Text = $"{month}/{day}";

        PrevButton?.Enabled = enablePrev;

        BodyLabel.ScrollOffset = 0;
        BodyLabel.Text = message;
        UpdateScrollBar();

        Show();
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            OnUp?.Invoke();
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
            BodyLabel.ScrollOffset = newValue * TextRenderer.CHAR_HEIGHT;
        }

        e.Handled = true;
    }

    private void UpdateScrollBar()
    {
        var totalLines = BodyLabel.ContentHeight / TextRenderer.CHAR_HEIGHT;
        var visibleLines = VisibleHeight / TextRenderer.CHAR_HEIGHT;

        ScrollBar.TotalItems = totalLines;
        ScrollBar.VisibleItems = visibleLines;
        ScrollBar.MaxValue = Math.Max(0, totalLines - visibleLines);
        ScrollBar.Value = 0;
    }
}