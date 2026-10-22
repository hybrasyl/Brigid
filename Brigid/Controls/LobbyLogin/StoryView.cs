#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Brigid.Networking;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.LobbyLogin;

/// <summary>
///     The main menu's Story (BRIG-107): a full-window view that shows the pages of a <see cref="StoryScript" /> one at a
///     time, as the old client told its story with images and text.
///     <list type="bullet">
///         <item>A <b>slide</b> fills the window with its theme part, cover fit, and puts the text in a caption box.</item>
///         <item>A <b>text</b> page puts the markdown in a framed box under the page title.</item>
///         <item>A <b>cutscene</b> page shows a placeholder frame until cutscene playback exists (BRIG-26), with the text in a caption box.</item>
///     </list>
///     Back and Next move through the pages, as do the Left and Right arrows, Space and Enter. Next on the last page closes
///     the view, and so does Escape. The view is modal: it covers the menu and takes every key while it is open.
/// </summary>
public sealed class StoryView : UIPanel
{
    private const int TITLE_SIZE = 40;
    private const int MARGIN = 40;
    private const int NAV_BUTTON_WIDTH = 90;
    private const int NAV_BUTTON_HEIGHT = 22;
    private const int NAV_BOTTOM = 8;
    private const int FRAME_PADDING = 10;
    private const int CAPTION_MIN_HEIGHT = 40;
    private const int CAPTION_MAX_HEIGHT = 150;

    private static readonly Rectangle TitleBounds = new(60, 24, 520, 70);
    private static readonly Rectangle TextFrameBounds = new(60, 100, 520, 300);
    private static readonly Rectangle PlaceholderBounds = new(160, 105, 320, 180);

    private readonly DisplayTitle Title;
    private readonly FramePanel TextFrame;
    private readonly MarkdownBlock Text;
    private readonly FramePanel Placeholder;
    private readonly UILabel PlaceholderHeading;
    private readonly UILabel PlaceholderDetail;
    private readonly UILabel Counter;

    private StoryScript Script = StoryScript.Empty;

    public TextButton BackButton { get; }
    public TextButton NextButton { get; }

    /// <summary>The index of the page on show.</summary>
    public int PageIndex { get; private set; }

    /// <summary>The page on show, or null when the story has no pages.</summary>
    public StoryPage? Page => PageIndex < Script.Pages.Count ? Script.Pages[PageIndex] : null;

    /// <summary>Raised when the view closes.</summary>
    public event CloseHandler? OnClose;

    private int NavTop => Height - NAV_BOTTOM - NAV_BUTTON_HEIGHT;

    public StoryView()
    {
        Name = "storyview";
        X = 0;
        Y = 0;
        Width = ChaosGame.VIRTUAL_WIDTH;
        Height = ChaosGame.VIRTUAL_HEIGHT;
        Visible = false;
        IsModal = true;
        UsesControlStack = true;

        Title = new DisplayTitle
        {
            Name = "title",
            X = TitleBounds.X,
            Y = TitleBounds.Y,
            Width = TitleBounds.Width,
            Height = TitleBounds.Height,
            Size = TITLE_SIZE,
            IsHitTestVisible = false
        };

        AddChild(Title);

        Placeholder = new FramePanel
        {
            Name = "placeholder",
            X = PlaceholderBounds.X,
            Y = PlaceholderBounds.Y,
            Width = PlaceholderBounds.Width,
            Height = PlaceholderBounds.Height,
            IsHitTestVisible = false
        };

        PlaceholderHeading = AddPlaceholderLabel("heading", Placeholder.Height / 2 - TextRenderer.CHAR_HEIGHT - 2);
        PlaceholderDetail = AddPlaceholderLabel("detail", Placeholder.Height / 2 + 2);
        AddChild(Placeholder);

        TextFrame = new FramePanel
        {
            Name = "frame",
            IsHitTestVisible = false
        };

        Text = new MarkdownBlock
        {
            Name = "text",
            X = FRAME_PADDING,
            Y = FRAME_PADDING,
            IsHitTestVisible = false
        };

        Text.LaidOut += LayoutFrame;
        TextFrame.AddChild(Text);
        AddChild(TextFrame);

        var nav = new UIPanel
        {
            Name = "nav",
            X = MARGIN,
            Y = NavTop,
            Width = Width - 2 * MARGIN,
            Height = NAV_BUTTON_HEIGHT,
            IsPassThrough = true
        };

        BackButton = new TextButton("Back", NAV_BUTTON_WIDTH, NAV_BUTTON_HEIGHT) { Name = "back" };
        BackButton.Clicked += Back;

        NextButton = new TextButton("Next", NAV_BUTTON_WIDTH, NAV_BUTTON_HEIGHT)
        {
            Name = "next",
            X = nav.Width - NAV_BUTTON_WIDTH
        };

        NextButton.Clicked += Next;

        Counter = new UILabel
        {
            Name = "counter",
            X = NAV_BUTTON_WIDTH,
            Width = nav.Width - 2 * NAV_BUTTON_WIDTH,
            Height = NAV_BUTTON_HEIGHT,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ShrinkToFit = false,
            IsHitTestVisible = false
        };

        nav.AddChild(BackButton);
        nav.AddChild(Counter);
        nav.AddChild(NextButton);
        AddChild(nav);
    }

    /// <summary>Opens the view on the first page of <paramref name="script" />. A story with no pages does not open.</summary>
    public void Show(StoryScript script)
    {
        if (script.Pages.Count == 0)
            return;

        Script = script;
        ShowPage(0);

        if (Visible)
            return;

        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
    }

    /// <summary>Closes the view and raises <see cref="OnClose" />.</summary>
    public void Hide()
    {
        if (!Visible)
            return;

        Visible = false;
        InputDispatcher.Instance?.RemoveControl(this);
        OnClose?.Invoke();
    }

    /// <summary>Goes to the next page, or closes the view on the last page.</summary>
    public void Next()
    {
        if (PageIndex + 1 < Script.Pages.Count)
            ShowPage(PageIndex + 1);
        else
            Hide();
    }

    /// <summary>Goes to the page before. On the first page it does nothing.</summary>
    public void Back()
    {
        if (PageIndex > 0)
            ShowPage(PageIndex - 1);
    }

    private void ShowPage(int index)
    {
        PageIndex = index;
        var page = Script.Pages[index];

        Title.Text = page.Title ?? string.Empty;
        Title.Visible = !string.IsNullOrEmpty(page.Title);

        Counter.Text = $"{index + 1} / {Script.Pages.Count}";
        BackButton.SetEnabled(index > 0);
        NextButton.SetText(index + 1 < Script.Pages.Count ? "Next" : "Close");

        TextFrame.Visible = !string.IsNullOrWhiteSpace(page.Text);

        //the frame sets the text width; LayoutFrame then fits the frame to the text
        if (page.Kind == StoryPageKind.Text)
            TextFrame.Width = TextFrameBounds.Width;
        else
            TextFrame.Width = Width - 2 * MARGIN;

        var inset = TextFrame.InsetFor(FRAME_PADDING);

        Text.X = inset;
        Text.Y = inset;
        Text.Width = TextFrame.Width - 2 * inset;
        Text.SetMarkdown(page.Text);

        RefreshPlaceholder();
    }

    //a text page has a fixed frame under the title; a caption box sits above the buttons and fits the text, up to a limit
    private void LayoutFrame()
    {
        if (Page is not { } page)
            return;

        if (page.Kind == StoryPageKind.Text)
        {
            TextFrame.X = TextFrameBounds.X;
            TextFrame.Y = TextFrameBounds.Y;
            TextFrame.Height = TextFrameBounds.Height;
        } else
        {
            TextFrame.X = MARGIN;
            TextFrame.Height = Math.Clamp(Text.ContentHeight + 2 * Text.Y, CAPTION_MIN_HEIGHT, CAPTION_MAX_HEIGHT);
            TextFrame.Y = NavTop - NAV_BOTTOM - TextFrame.Height;
        }

        Text.Height = TextFrame.Height - 2 * Text.Y;

        //the frame clips text that does not fit; the log tells the author to shorten the page
        if (Text.ContentHeight > Text.Height)
            NoticeDebugLog.Write($"[story] page {PageIndex + 1} needs {Text.ContentHeight} px of text height; the frame has {Text.Height}");
    }

    //a cutscene page, and a slide whose art the theme does not have, show the placeholder frame
    private void RefreshPlaceholder()
    {
        var (heading, detail) = Page switch
        {
            { Kind: StoryPageKind.Cutscene } page => ("A cutscene plays here.", page.Video),
            { Kind: StoryPageKind.Slide } page when string.IsNullOrWhiteSpace(page.Image) || !HasThemePart(page.Image)
                => ("Slide art goes here.", page.Image),
            _ => ((string?)null, (string?)null)
        };

        Placeholder.Visible = heading is not null;

        if (heading is null)
            return;

        if (PlaceholderHeading.Text != heading)
            PlaceholderHeading.Text = heading;

        if (PlaceholderDetail.Text != (detail ?? string.Empty))
            PlaceholderDetail.Text = detail ?? string.Empty;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        Counter.ForegroundColor = ThemeColor(ThemeRoles.Text, DialogPalette.StatusText);
        PlaceholderHeading.ForegroundColor = ThemeColor(ThemeRoles.TextTitle);
        PlaceholderDetail.ForegroundColor = ThemeColor(ThemeRoles.Text, DialogPalette.StatusText);

        base.Draw(spriteBatch);
    }

    //a slide draws its art over the whole window; the other pages draw the story background, else the menu background
    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if (Page is { Kind: StoryPageKind.Slide, Image: { Length: > 0 } image } && TryDrawThemePart(spriteBatch, image, bounds))
            return;

        if (TryDrawThemePart(spriteBatch, ThemeRoles.StoryBackground, bounds, ThemeRoles.MainMenuBackground))
            return;

        DrawRectClipped(spriteBatch, bounds, DialogPalette.Backdrop);
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case Keys.Escape:
                Hide();

                break;

            case Keys.Left:
                Back();

                break;

            case Keys.Right:
            case Keys.Space:
            //Alt+Enter changes the window size; it must not turn the page
            case Keys.Enter when !e.Alt:
                Next();

                break;
        }

        //the view is modal: a key it does not use must not reach the menu behind it
        e.Handled = true;
    }

    public override void OnMouseScroll(MouseScrollEvent e) => e.Handled = true;

    private UILabel AddPlaceholderLabel(string name, int y)
    {
        var label = new UILabel
        {
            Name = name,
            X = 0,
            Y = y,
            Width = Placeholder.Width,
            Height = TextRenderer.CHAR_HEIGHT,
            HorizontalAlignment = HorizontalAlignment.Center,
            ShrinkToFit = false,
            IsHitTestVisible = false
        };

        Placeholder.AddChild(label);

        return label;
    }
}
