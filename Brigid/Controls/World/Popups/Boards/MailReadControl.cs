#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Boards;

/// <summary>
///     Mail reading panel using _nmailr prefab. Displays a received mail message with author, title, date, and body text.
///     Buttons: Prev, Next, New, Reply, Delete, Up (back to list), Quit (close).
/// </summary>
public sealed class MailReadControl : PrefabPanel
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
    public UIButton? DeleteButton { get; }
    public UIButton? NewButton { get; }
    public UIButton? NextButton { get; }
    public UIButton? PrevButton { get; }
    public UIButton? QuitButton { get; }
    public UIButton? ReplyButton { get; }
    public UIButton? UpButton { get; }

    public MailReadControl()
        : base("_nmailr", false)
    {
        Name = "MailRead";
        Visible = false;
        UsesControlStack = true;

        PrevButton = CreateButton("Prev");
        NextButton = CreateButton("Next");
        NewButton = CreateButton("New");
        ReplyButton = CreateButton("Reply");
        DeleteButton = CreateButton("Delete");
        UpButton = CreateButton("Up");
        QuitButton = CreateButton("Quit");

        if (QuitButton is not null)
            QuitButton.Clicked += () => OnQuit?.Invoke();

        if (UpButton is not null)
            UpButton.Clicked += () => OnUp?.Invoke();

        if (PrevButton is not null)
            PrevButton.Clicked += () => OnPrev?.Invoke();

        if (NextButton is not null)
            NextButton.Clicked += () => OnNext?.Invoke();

        if (NewButton is not null)
            NewButton.Clicked += () => OnNewMail?.Invoke();

        if (ReplyButton is not null)
            ReplyButton.Clicked += () => OnReplyPost?.Invoke(CurrentPostId);

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

    public event DeletePostHandler? OnDeletePost;
    public event NewMailHandler? OnNewMail;
    public event NextHandler? OnNext;
    public event PrevHandler? OnPrev;
    public event QuitHandler? OnQuit;
    public event ReplyPostHandler? OnReplyPost;
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
    ///     Displays a mail message.
    /// </summary>
    public void ShowMail(
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