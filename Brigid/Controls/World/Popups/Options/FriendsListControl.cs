#region
using Brigid.Controls.Components;
using Brigid.Extensions;
using Brigid.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Options;

/// <summary>
///     Friends list popup using _nfriend prefab. Two-column layout of 12 slots each (24 total);
///     column 1 holds slots 1-12, column 2 holds slots 13-24. Row height 16px. OK/Cancel buttons at bottom.
/// </summary>
public sealed class FriendsListControl : PrefabPanel
{
    private const int ROW_HEIGHT = 16;
    private const int MAX_VISIBLE_ROWS = 12;
    private const int MAX_LENGTH = 28;

    private readonly Rectangle LeftColumnRect;

    private readonly UITextBox[] NamesColumn1 = new UITextBox[MAX_VISIBLE_ROWS];
    private readonly UITextBox[] NamesColumn2 = new UITextBox[MAX_VISIBLE_ROWS];
    private readonly Rectangle RightColumnRect;
    private bool ClosedWithOk;
    private int DataVersion;

    private List<string> Friends = [];
    private int RenderedVersion = -1;
    private SlideAnimator Slide;
    private int SlideAnchorY;
    private bool SlideMode;

    public UIButton? CancelButton { get; }

    public UIButton? OkButton { get; }

    public FriendsListControl()
        : base("_nfriend", false)
    {
        Name = "FriendsList";
        Visible = false;
        UsesControlStack = true;

        CancelButton = CreateButton("Cancel");
        OkButton = CreateButton("OK");

        if (CancelButton is not null)
            CancelButton.Clicked += Close;

        if (OkButton is not null)
            OkButton.Clicked += CloseWithOk;

        //column rects from prefab
        LeftColumnRect = GetRect("TextTopLeft");
        RightColumnRect = GetRect("TextTopRight");

        //if no rects found, use defaults based on prefab layout
        if (LeftColumnRect == Rectangle.Empty)
            LeftColumnRect = new Rectangle(
                40,
                40,
                175,
                MAX_VISIBLE_ROWS * ROW_HEIGHT);

        if (RightColumnRect == Rectangle.Empty)
            RightColumnRect = new Rectangle(
                251,
                40,
                175,
                MAX_VISIBLE_ROWS * ROW_HEIGHT);
        
        for (var i = 0; i < MAX_VISIBLE_ROWS; i++)
        {
            NamesColumn1[i] = new UITextBox
            {
                Name = $"Left{i}",
                X = LeftColumnRect.X,
                Y = LeftColumnRect.Y + i * ROW_HEIGHT,
                Width = LeftColumnRect.Width,
                Height = ROW_HEIGHT,
                MaxLength = MAX_LENGTH
            };

            NamesColumn2[i] = new UITextBox
            {
                Name = $"Right{i}",
                X = RightColumnRect.X,
                Y = RightColumnRect.Y + i * ROW_HEIGHT,
                Width = RightColumnRect.Width,
                Height = ROW_HEIGHT,
                MaxLength = MAX_LENGTH
            };

            AddChild(NamesColumn1[i]);
            AddChild(NamesColumn2[i]);
        }
    }

    private void Close()
    {
        ClosedWithOk = false;

        if (SlideMode)
        {
            InputDispatcher.Instance?.RemoveControl(this);
            Slide.SlideOut();
        } else
        {
            Hide();
            OnClose?.Invoke();
        }
    }

    private void CloseWithOk()
    {
        ClosedWithOk = true;

        if (SlideMode)
        {
            InputDispatcher.Instance?.RemoveControl(this);
            Slide.SlideOut();
        } else
        {
            OnOk?.Invoke();
            Hide();
            OnClose?.Invoke();
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        RefreshCaches();

        //textboxes are children — drawn by base.draw()
        base.Draw(spriteBatch);
    }

    /// <summary>
    ///     Returns all non-empty friend names currently entered in the textboxes
    ///     (both online and offline columns).
    /// </summary>
    /// <summary>
    ///     Returns all non-empty friend names currently entered in the textboxes.
    ///     Preserves the original saved order for existing friends and appends
    ///     any newly typed names at the end, so adding a friend never reorders
    ///     the existing list across save/reload cycles.
    /// </summary>
    /// <summary>
    ///     Returns all non-empty friend names from the textboxes in slot order
    ///     (column 1 top-to-bottom, then column 2 top-to-bottom).
    /// </summary>
    public List<string> GetFriendNames()
    {
        var names = new List<string>(MAX_VISIBLE_ROWS * 2);

        for (var i = 0; i < MAX_VISIBLE_ROWS; i++)
        {
            var text = NamesColumn1[i].Text;

            if (!string.IsNullOrWhiteSpace(text))
                names.Add(text.Trim());
        }

        for (var i = 0; i < MAX_VISIBLE_ROWS; i++)
        {
            var text = NamesColumn2[i].Text;

            if (!string.IsNullOrWhiteSpace(text))
                names.Add(text.Trim());
        }

        return names;
    }

    public override void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);

        if (SlideMode)
            Slide.Hide(this);
        else
            Visible = false;
    }

    public event CloseHandler? OnClose;
    public event OkHandler? OnOk;

    private void RefreshCaches()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        //slots fill column 1 first (rows 0-11), then column 2 (rows 12-23)
        for (var i = 0; i < MAX_VISIBLE_ROWS; i++)
        {
            NamesColumn1[i].Text = i < Friends.Count ? Friends[i] : string.Empty;

            var rightIndex = i + MAX_VISIBLE_ROWS;
            NamesColumn2[i].Text = rightIndex < Friends.Count ? Friends[rightIndex] : string.Empty;
        }
    }

    /// <summary>
    ///     Populates the friends list. Online friends on left, offline on right.
    /// </summary>
    /// <summary>
    ///     Populates the friends list. Slots fill column 1 first (rows 0-11),
    ///     then column 2 (rows 12-23).
    /// </summary>
    public void SetFriends(List<string> friends)
    {
        Friends = friends;
        DataVersion++;
    }

    public void SetSlideAnchor(int anchorX, int anchorY)
    {
        Slide.SetSlideAnchor(anchorX, Width);
        SlideAnchorY = anchorY;
    }

    /// <summary>
    ///     Shows immediately at top-center of screen (hotkey mode).
    /// </summary>
    public override void Show()
    {
        this.CenterHorizontallyOnScreen();
        Y = 0;
        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
        SlideMode = false;
    }

    /// <summary>
    ///     Slides out from the left edge of MainOptionsControl (button mode).
    /// </summary>
    public void SlideIn()
    {
        if (Visible)
            return;

        Y = SlideAnchorY;
        InputDispatcher.Instance?.PushControl(this);
        Slide.SlideIn(this);
        SlideMode = true;
    }

    public override void Update(GameTime gameTime)
    {
        if (!Visible || !Enabled)
            return;

        if (Slide.Update(gameTime, this))
        {
            if (ClosedWithOk)
                OnOk?.Invoke();

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
}