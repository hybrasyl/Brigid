#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Hud;
using Brigid.Controls.World.Hud.Main;
using Brigid.Controls.World.ViewPort;
using Brigid.Rendering;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-145: the chat box of the new HUD. Its tabs are views of the one chat log; a tab that is not selected shows a
///     mark for news; the hide button collapses it to its tab row; the input draws its text as the lines do.
/// </summary>
[Collection("FontEngine")]
public sealed class ChatBoxControlTests : IDisposable
{
    private readonly ChatBoxControl Box;
    private readonly Chat Log = new();

    public ChatBoxControlTests()
    {
        FontEngine.Initialize(0);
        Box = new ChatBoxControl(Log);
    }

    public void Dispose() => Box.Dispose();

    [Fact]
    public void TheBoxIsInItsPlaceWithTheThreeTabsAndTheChatTabSelected()
    {
        Assert.Equal(MainHudLayout.ChatBox, new Rectangle(Box.X, Box.Y, Box.Width, Box.Height));
        Assert.Equal(["System", "Combat", "Chat"], Box.TabList.Select(tab => tab.Name));
        Assert.Equal("Chat", Box.SelectedTabName);
        Assert.Same(ChatTab.Chat, Box.View.Window.Tab);
        UiAssert.NamedTree(Box);
    }

    //each face measures the names in its own width
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TheTabsFitTheRowInOrderAndClearTheHideButton(int face)
    {
        Assert.InRange(face, 0, FontEngine.Instance.FontCount - 1);
        FontEngine.Instance.SetActiveFont(face);
        Box.Update(new GameTime());

        var tabs = Box.TabControls.ToList();

        Assert.Equal(3, tabs.Count);
        Assert.All(tabs, tab => Assert.True(tab.Width >= MainHudLayout.CHAT_TAB_MIN, tab.Name));

        for (var i = 1; i < tabs.Count; i++)
            Assert.Equal(tabs[i - 1].X + tabs[i - 1].Width + MainHudLayout.CHAT_TAB_GAP, tabs[i].X);

        Assert.True(tabs[^1].X + tabs[^1].Width <= MainHudLayout.ChatHideButton.Left);
    }

    [Fact]
    public void FitTabsShrinksTheWidestTabsToTheMinimum()
    {
        Assert.Equal([40, 30], MainHudLayout.FitTabs([40, 30], 100, 2));
        Assert.Equal([30, 30, 20], MainHudLayout.FitTabs([50, 30, 20], 84, 2));
        Assert.Equal([MainHudLayout.CHAT_TAB_MIN, MainHudLayout.CHAT_TAB_MIN], MainHudLayout.FitTabs([60, 60], 10, 2));
        Assert.Equal([MainHudLayout.CHAT_TAB_MIN], MainHudLayout.FitTabs([5], 100, 2));
    }

    [Fact]
    public void EveryChatIconHasBuiltInArt() => Assert.All(ThemeRoles.ChatIcons, role => Assert.True(BuiltInArt.Has(role), role));

    [Fact]
    public void AnUnknownTabNameSelectsTheChatTab()
    {
        Box.Select("System");
        Box.Select("Nothing");

        Assert.Equal("Chat", Box.SelectedTabName);
    }

    [Fact]
    public void EachTabShowsItsChannels()
    {
        Log.Add(ChatChannel.Say, "hello", Color.White);
        Log.Add(ChatChannel.System, "notice", Color.Orange);
        Log.Add(ChatChannel.Combat, "[combat] Damage: x", Color.Olive);

        Assert.Equal("hello", Shown());

        Box.Select("System");
        Assert.Equal("notice", Shown());

        Box.Select("Combat");
        Assert.Equal("[combat] Damage: x", Shown());
    }

    [Fact]
    public void ATabThatIsNotSelectedShowsAMarkUntilItIsSelected()
    {
        Log.Add(ChatChannel.System, "notice", Color.Orange);
        Log.Add(ChatChannel.Say, "hello", Color.White);
        Log.Add(ChatChannel.Group, "", Color.Olive);

        Assert.True(Box.IsMarked("System"));
        Assert.False(Box.IsMarked("Chat"));
        Assert.False(Box.IsMarked("Combat"));

        Box.Select("System");
        Assert.False(Box.IsMarked("System"));
    }

    //the player's changes are saved; a select from the code, as at the restore, is not a change
    [Fact]
    public void ATabClickRaisesStateChangedAndASelectFromTheCodeDoesNot()
    {
        var changes = 0;
        Box.StateChanged += () => changes++;

        Box.Select("System");
        Assert.Equal(0, changes);

        Box.TabControls.First(tab => tab.Name == "tab_combat").OnClick(new ClickEvent { Button = MouseButton.Left });
        Assert.Equal(1, changes);
        Assert.Equal("Combat", Box.SelectedTabName);
    }

    [Fact]
    public void WhileHiddenTheSelectedTabShowsAMarkToo()
    {
        var chat = Box.TabControls.Single(tab => tab.Name == "tab_chat");
        Box.SetCollapsed(true);

        Assert.Equal(DialogPalette.SelectedText, chat.LabelColorForTests);

        Log.Add(ChatChannel.Say, "hello", Color.White);

        Assert.True(Box.IsMarked("Chat"));
        Assert.Equal(DialogPalette.Title, chat.LabelColorForTests);

        Box.SetCollapsed(false);
        Assert.False(Box.IsMarked("Chat"));
        Assert.Equal(DialogPalette.SelectedText, chat.LabelColorForTests);
    }

    //a click on the tab that is selected changed nothing the player could see, but went back to the newest line
    [Fact]
    public void SelectingTheSelectedTabKeepsTheScrollPosition()
    {
        for (var i = 0; i < 40; i++)
            Log.Add(ChatChannel.Say, $"line {i}", Color.White);

        Assert.True(Box.Scroll(10));
        Assert.False(Box.View.Window.IsAtNewest);

        Box.Select("Chat");

        Assert.False(Box.View.Window.IsAtNewest);

        Box.Select("System");

        Assert.True(Box.View.Window.IsAtNewest);
    }

    [Fact]
    public void DisposeLeavesNoListenerOnTheChatLog()
    {
        var log = new Chat();
        var box = new ChatBoxControl(log);

        Assert.True(log.ListenerCountForTests > 0);

        box.Dispose();

        Assert.Equal(0, log.ListenerCountForTests);
    }

    [Fact]
    public void HiddenTheTabRowIsAtTheBottomOfTheBoxAndTakesTheMouse()
    {
        Box.SetCollapsed(true);

        var row = MainHudLayout.ChatCollapsedRow;
        Assert.All(Box.TabControls, tab => Assert.Equal(row.Y + MainHudLayout.CHAT_ROW_PAD, tab.Y));
        Assert.Equal(row.Y + MainHudLayout.CHAT_ROW_PAD, HideButton().Y);

        //the row takes the mouse, also between the tabs; the rest of the box passes it to the world
        var rowPoint = (Box.ScreenX + Box.Width - 40, Box.ScreenY + row.Y + 2);
        var abovePoint = (Box.ScreenX + 40, Box.ScreenY + 20);
        Assert.True(Box.TakesMouseAt(rowPoint.Item1, rowPoint.Item2));
        Assert.False(Box.TakesMouseAt(abovePoint.Item1, abovePoint.Item2));
        Assert.True(Box.IsPassThrough);

        //the dispatcher's hit test reads the clip of a draw, so the test checks the backdrop that takes the row's clicks
        var backdrop = Box.Children.Single(child => child.Name == "tab_row");
        Assert.True(backdrop.IsHitTestVisible);
        Assert.False(backdrop.IsPassThrough);
        Assert.Equal(row, new Rectangle(backdrop.X, backdrop.Y, backdrop.Width, backdrop.Height));
    }

    [Fact]
    public void AClickDoesNotFocusTheInputAndTheLinesMoveUpWhileItShows()
    {
        var lines = Box.View.ShownLines.Count();
        Assert.False(Box.Input.IsHitTestVisible);
        Assert.Equal(MainHudLayout.ChatLog.Height / TextRenderer.CHAR_HEIGHT, lines);

        Box.Input.Focus("Sabrael: ", Color.White);

        Assert.True(Box.Input.IsHitTestVisible);
        Assert.True(Box.View.ShownLines.Count() < lines);

        //the last line touches the bottom of the line area, just above the input
        var bottom = Box.View.ShownLines.Last();
        Assert.Equal(Box.Input.Y - 2, Box.View.Y + bottom.Y + bottom.Height);

        Box.Input.Unfocus();

        Assert.False(Box.Input.IsHitTestVisible);
        Assert.Equal(lines, Box.View.ShownLines.Count());
    }

    [Fact]
    public void TheScrollBarIsTheChatBoxsOwnAndItsThumbIsAtTheBottomAtTheNewestEntry()
    {
        for (var i = 0; i < 30; i++)
            Log.Add(ChatChannel.Say, $"m{i}", Color.White);

        Shown();

        var bar = Box.View.ScrollBar;
        var (top, height) = bar.ThumbPlace;
        Assert.Equal(bar.Height, top + height);

        Box.Scroll(100);
        Shown();
        Assert.Equal(0, bar.ThumbPlace.Top);
    }

    //the sync of the bar after a scroll moves its offset; that does not move the window back to the start of the entry
    [Fact]
    public void ALineScrollInsideAWrappedEntryStaysAfterARefresh()
    {
        var text = string.Join(" ", Enumerable.Repeat("word", 120));
        Log.Add(ChatChannel.Say, text, Color.White);
        Log.Add(ChatChannel.Say, "last", Color.White);
        Shown();

        //one line for "last", then one line into the wrapped entry
        Assert.True(Box.Scroll(2));

        var wrapped = TextRenderer.WrapLines(text, MainHudLayout.ChatLog.Width - MainHudLayout.CHAT_SCROLL_WIDTH - 2, FontEngine.Instance.UiSize);
        Assert.Equal(wrapped[^2], Shown());
        Assert.Equal(1, Box.View.ScrollModel.Offset);
        Assert.Equal(wrapped[^2], Shown());
    }

    //the page of the bar is the entries that the lines show, so a tab of a few long entries can scroll
    [Fact]
    public void TheBarScrollsATabOfFewWrappedEntries()
    {
        var text = string.Join(" ", Enumerable.Repeat("word", 60));

        for (var i = 0; i < 3; i++)
            Log.Add(ChatChannel.Say, text, Color.White);

        Shown();

        Assert.True(Box.View.ScrollModel.Extent <= 3);
        Assert.True(Box.View.ScrollModel.CanScroll);
    }

    [Fact]
    public void AFaceChangeWrapsTheLinesAgain()
    {
        var text = string.Join(" ", Enumerable.Repeat("wide", 60));
        Log.Add(ChatChannel.Say, text, Color.White);
        Shown();

        FontEngine.Instance.SetActiveFont(FontEngine.Instance.FontCount - 1);
        var expected = TextRenderer.WrapLines(text, MainHudLayout.ChatLog.Width - MainHudLayout.CHAT_SCROLL_WIDTH - 2, FontEngine.Instance.UiSize);

        Assert.Equal(expected[^1], Shown());
    }

    [Fact]
    public void HideCollapsesTheBoxToItsTabRow()
    {
        var changes = 0;
        Box.StateChanged += () => changes++;

        Box.SetCollapsed(true);

        Assert.True(Box.IsCollapsed);
        Assert.False(Box.View.Visible);
        Assert.True(Box.IsPassThrough);
        Assert.Equal(ThemeRoles.IconChatShow, HideButton().Role);
        Assert.False(Box.Scroll(1));
        Assert.Equal(1, changes);

        Box.ShowSystem();

        Assert.False(Box.IsCollapsed);
        Assert.True(Box.View.Visible);
        Assert.False(Box.IsPassThrough);
        Assert.Equal(ThemeRoles.IconChatHide, HideButton().Role);
        Assert.Equal("System", Box.SelectedTabName);
    }

    [Fact]
    public void TheInputDrawsItsTextAsTheLinesDo()
    {
        Log.Add(ChatChannel.Say, "hello", Color.White);
        Shown();
        Box.Input.Update(new GameTime());

        var text = Box.Input.FindChild<UITextBox>("chat_text_box")!;
        var line = Box.View.ShownLines.Last();

        Assert.Same(Box.View, Box.Input.TextStyle);
        Assert.Equal(line.FontSize, text.FontSize);
        Assert.Equal(line.CharacterSpacing, text.CharacterSpacing);
        Assert.Equal(MainHudLayout.ChatInput, new Rectangle(Box.Input.X, Box.Input.Y, Box.Input.Width, Box.Input.Height));
    }

    [Fact]
    public void TheViewScrollsBackAndShowsTheBottomLineAgainWhenTheLogIsCleared()
    {
        for (var i = 0; i < 20; i++)
            Log.Add(ChatChannel.Say, $"m{i}", Color.White);

        Assert.Equal("m19", Shown());
        Assert.True(Box.Scroll(2));
        Assert.Equal("m17", Shown());

        Log.Clear();
        Log.Add(ChatChannel.Say, "after", Color.White);

        Assert.Equal("after", Shown());
    }

    //a clear while the player reads back: the view follows the new entries again, not only the first of them
    [Fact]
    public void AfterAClearTheViewFollowsTheNewestEntry()
    {
        for (var i = 0; i < 20; i++)
            Log.Add(ChatChannel.Say, $"m{i}", Color.White);

        Box.Scroll(5);
        Shown();
        Log.Clear();
        Log.Add(ChatChannel.Say, "x", Color.White);
        Shown();
        Log.Add(ChatChannel.Say, "y", Color.White);

        Assert.Equal("y", Shown());
    }

    //the mark is the title colour, also on a selected tab: a selected tab has a mark only while the box is hidden, and
    //the mark is then the one sign of news. With no mark, a selected tab shows its selected colour
    [Fact]
    public void AMarkedTabShowsItsLabelInTheTitleColour()
    {
        var tab = new SelectableTab("System", 40, 13);
        var label = tab.Children.OfType<UILabel>().Single();

        Assert.Equal(DialogPalette.ForRole(ThemeRoles.Text), label.ForegroundColor);

        tab.IsMarked = true;
        Assert.Equal(DialogPalette.ForRole(ThemeRoles.TextTitle), label.ForegroundColor);

        tab.IsSelected = true;
        Assert.Equal(DialogPalette.ForRole(ThemeRoles.TextTitle), label.ForegroundColor);

        tab.IsMarked = false;
        Assert.Equal(DialogPalette.ForRole(ThemeRoles.TextSelected), label.ForegroundColor);
    }

    //the ignore and drop prompts are wider than the new input; the text box keeps room to type
    [Theory]
    [InlineData('a')]
    [InlineData('d')]
    public void ALongPromptLeavesTheTextBoxRoomToType(char mode)
    {
        Box.Input.FocusIgnore();
        Box.Input.OnTextInput(new TextInputEvent { Character = mode });
        AssertRoomToType();

        Box.Input.Unfocus();
        Box.Input.ShowPrompt("Number of items to drop [ 0 - 100 ]: ", 3, _ => { });
        AssertRoomToType();
        Box.Input.Unfocus();
    }

    private void AssertRoomToType()
    {
        var prefix = Box.Input.FindChild<UILabel>("chat_prefix")!;
        var text = Box.Input.FindChild<UITextBox>("chat_text_box")!;

        Assert.True(text.Width >= ChatInputControl.MIN_TEXT_WIDTH, $"{text.Width}");
        Assert.Equal(prefix.X + prefix.Width, text.X);
        Assert.Equal(Box.Input.Width, text.X + text.Width);
    }

    //the new hud hides the pane; a pane that shows again starts empty
    [Fact]
    public void AClearedPaneShowsNoOldMessage()
    {
        var pane = new SystemMessagePaneControl(MainHudLayout.World);
        pane.AddMessage("old whisper");

        pane.Clear();
        pane.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(10)));

        Assert.All(pane.Children, line => Assert.False(line.Visible));
        pane.Dispose();
    }

    //a clear kept the count of the old messages, so the next message showed under their lines
    [Fact]
    public void AMessageAfterAClearShowsAlone()
    {
        var pane = new SystemMessagePaneControl(MainHudLayout.World);
        pane.AddMessage("old whisper");
        pane.AddMessage("old shout");

        pane.Clear();
        pane.AddMessage("new whisper");

        Assert.Equal(["new whisper"], pane.Children.OfType<UILabel>().Where(line => line.Visible).Select(line => line.Text));
        pane.Dispose();
    }

    //the bottom line of the scrollback
    private string Shown()
    {
        Box.View.Refresh();

        return Box.View.ShownText.Last();
    }

    private IconButton HideButton() => Box.Children.OfType<IconButton>().Single(button => button.Name == "hide_button");
}
