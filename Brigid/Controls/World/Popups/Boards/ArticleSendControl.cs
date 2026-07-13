#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Boards;

/// <summary>
///     Article compose panel using _nartin prefab. Provides subject and body text entry fields for posting to a public
///     board. No recipient field — public board posts have no addressee.
/// </summary>
public sealed class ArticleSendControl : PrefabPanel
{
    //the anchor art (_nartin.txt references the shared _nmails.spf[0]) has a bare black
    //scrollbar strip at (483,65), 16px wide by 214px tall — measured from the rendered art;
    //it does NOT align with the prefab Content rect (21,69)-(501,273), sitting 18px inside
    //its right edge and 4px above its top. A live scrollbar is overlaid on the strip and
    //the body text stops at its left edge (matching the retail client).
    private const int SCROLLBAR_X = 483;
    private const int SCROLLBAR_Y = 65;
    private const int SCROLLBAR_HEIGHT = 214;

    private readonly UILabel? AuthorLabel;
    private readonly UITextBox BodyBox;
    private readonly ScrollBarControl BodyScrollBar;
    private readonly UITextBox? TitleBox;
    private int TargetX;

    public ushort BoardId { get; set; }
    public UIButton? CancelButton { get; }
    public UIButton? SendButton { get; }

    public ArticleSendControl()
        : base("_nartin", false)
    {
        Name = "ArticleSend";
        Visible = false;
        UsesControlStack = true;

        SendButton = CreateButton("Send");
        CancelButton = CreateButton("Cancel");

        if (SendButton is not null)
            SendButton.Clicked += HandleSend;

        if (CancelButton is not null)
            CancelButton.Clicked += () =>
            {
                Hide();
                OnCancel?.Invoke();
            };

        AuthorLabel = CreateLabel("Author");
        TitleBox = CreateTextBox("Title", 60);
        TitleBox?.ForegroundColor = LegendColors.White;
        TitleBox?.IsTabStop = true;

        //content rect for multi-line body text entry
        var contentRect = GetRect("Content");

        BodyBox = new UITextBox
        {
            X = contentRect.X,
            Y = contentRect.Y,
            Width = SCROLLBAR_X - contentRect.X,
            Height = contentRect.Height,
            IsMultiLine = true,
            IsSelectable = true,
            MaxLength = 10000,
            PaddingLeft = 0,
            PaddingRight = 0,
            PaddingTop = 0,
            PaddingBottom = 0,
            ForegroundColor = TextColors.Default,
            IsTabStop = true
        };

        AddChild(BodyBox);

        BodyScrollBar = new ScrollBarControl
        {
            Name = "BodyScrollBar",
            X = SCROLLBAR_X,
            Y = SCROLLBAR_Y,
            Height = SCROLLBAR_HEIGHT
        };

        TextBoxScrollSync.Wire(BodyBox, BodyScrollBar);
        AddChild(BodyScrollBar);
    }

    /// <summary>
    ///     Keeps the scrollbar range and thumb in sync with the editable body as it changes.
    /// </summary>
    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        TextBoxScrollSync.Sync(BodyBox, BodyScrollBar);
    }

    private void HandleSend()
    {
        var subject = TitleBox?.Text ?? string.Empty;
        OnSend?.Invoke(subject, BodyBox.Text);
    }

    public override void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        Visible = false;
    }

    public event CancelHandler? OnCancel;
    public event ArticleSendHandler? OnSend; //subject, body

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
    ///     Shows the compose dialog for a new public board post.
    /// </summary>
    public void ShowCompose(string authorName)
    {
        AuthorLabel?.Text = authorName;

        if (TitleBox is not null)
        {
            TitleBox.Text = string.Empty;
            TitleBox.IsFocused = true;
            TitleBox.ResetUndoHistory();
        }

        BodyBox.Text = string.Empty;
        BodyBox.ScrollOffset = 0;
        BodyBox.CursorPosition = 0;

        //this compose panel is a reused singleton — drop undo so Ctrl+Z can't resurrect a prior post's contents
        BodyBox.ResetUndoHistory();

        Show();
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case Keys.Escape:
                Hide();
                OnCancel?.Invoke();
                e.Handled = true;

                break;

            case Keys.Tab when TitleBox?.IsFocused == true:
                TitleBox.IsFocused = false;
                BodyBox.IsFocused = true;
                e.Handled = true;

                break;
        }
    }
}