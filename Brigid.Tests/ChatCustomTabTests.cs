#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Hud.Main;
using Brigid.Controls.World.Popups;
using Brigid.Definitions;
using Brigid.Rendering;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-145 phase 3: the custom tabs of the chat box. The player adds up to three, each with a unique name and a set of
///     channels, and edits or removes one; <c>brigid.cfg</c> keeps them as one line each; the editor's OK needs a valid,
///     free name and a channel.
/// </summary>
[Collection("FontEngine")]
public sealed class ChatCustomTabTests : IDisposable
{
    private readonly ChatBoxControl Box;
    private readonly Chat Log = new();

    public ChatCustomTabTests()
    {
        FontEngine.Initialize(0);
        Box = new ChatBoxControl(Log);
    }

    public void Dispose() => Box.Dispose();

    private static ChatTab Tab(string name, params ChatChannel[] channels) => new(name, channels.ToHashSet());

    private static void Type(UITextBox box, string text)
    {
        foreach (var c in text)
            box.OnTextInput(new TextInputEvent { Character = c });
    }

    #region The setting line
    [Fact]
    public void ATabComesBackFromItsSettingLine()
    {
        var tab = Tab("Party 2", ChatChannel.Group, ChatChannel.Whisper);

        Assert.Equal("Party 2|Whisper,Group", tab.ToSetting());
        Assert.True(ChatTab.TryParseSetting(tab.ToSetting(), out var back));
        Assert.Equal("Party 2", back.Name);
        Assert.Equal(tab.Channels, back.Channels);
    }

    [Theory]
    [InlineData("Party")]
    [InlineData("Party|")]
    [InlineData("Party|Loot")]
    [InlineData("Party|3")]
    [InlineData("Party|Say,")]
    [InlineData("Too long a|Say")]
    [InlineData(" Party|Say")]
    [InlineData("Par-ty|Say")]
    [InlineData("|Say")]
    [InlineData("a|b|Say")]
    [InlineData("Party|Say|Say")]
    public void ABadSettingLineIsRefused(string value) => Assert.False(ChatTab.TryParseSetting(value, out _));

    [Theory]
    [InlineData("A", true)]
    [InlineData("Party 12", true)]
    [InlineData("Party 123", false)]
    [InlineData("Party ", false)]
    [InlineData("", false)]
    [InlineData("P|ty", false)]
    public void ANameIsOneToEightLettersDigitsAndSpaces(string name, bool valid) => Assert.Equal(valid, ChatTab.IsValidName(name));
    #endregion

    #region The box
    [Fact]
    public void AnAddedTabIsSelectedNamedAndShowsTheEarlierMessages()
    {
        Log.Add(ChatChannel.Whisper, "psst", Color.Blue);
        var changes = 0;
        Box.StateChanged += () => changes++;

        Box.AddCustomTab(Tab("Private", ChatChannel.Whisper));

        Assert.Equal("Private", Box.SelectedTabName);
        Assert.Equal(["Private"], Box.CustomTabs.Select(tab => tab.Name));
        Assert.Equal(1, changes);
        Assert.Contains(Box.TabControls, control => control.Name == "custom_tab_0");
        Box.View.Refresh();
        Assert.Equal("psst", Box.View.ShownText.Last());
        UiAssert.NamedTree(Box);
    }

    [Fact]
    public void TheRowHoldsThreeCustomTabsAndThenHidesThePlusButton()
    {
        Box.AddCustomTab(Tab("Party 12", ChatChannel.Group));
        Box.AddCustomTab(Tab("Guild 12", ChatChannel.Guild));
        Assert.True(Box.AddTabButton.Visible);
        Box.AddCustomTab(Tab("Whisp 12", ChatChannel.Whisper));

        Assert.False(Box.CanAddTab);
        Assert.False(Box.AddTabButton.Visible);

        Box.AddCustomTab(Tab("Four", ChatChannel.Say));
        Assert.Equal(3, Box.CustomTabs.Count);

        //six tabs fit the row, down to the minimum width, clear of the hide button
        var tabs = Box.TabControls.ToList();
        Assert.All(tabs, tab => Assert.True(tab.Width >= MainHudLayout.CHAT_TAB_MIN, tab.Name));
        Assert.True(tabs[^1].X + tabs[^1].Width <= MainHudLayout.ChatHideButton.Left);
        UiAssert.NamedTree(Box);
    }

    [Theory]
    [InlineData("chat")]
    [InlineData("SYSTEM")]
    [InlineData("party")]
    public void ANameThatATabHasIsNotFree(string name)
    {
        Box.AddCustomTab(Tab("Party", ChatChannel.Group));

        Assert.False(Box.IsNameFree(name));

        Box.AddCustomTab(Tab(name, ChatChannel.Say));
        Assert.Single(Box.CustomTabs);
    }

    [Fact]
    public void AnEditKeepsThePlaceAndTheSelectionOfTheTab()
    {
        var party = Tab("Party", ChatChannel.Group);
        Box.AddCustomTab(party);
        Box.AddCustomTab(Tab("Guild", ChatChannel.Guild));
        Box.Select("Party");
        Log.Add(ChatChannel.Whisper, "psst", Color.Blue);

        Assert.True(Box.IsNameFree("Party", party));
        Box.ReplaceCustomTab(party, Tab("Friends", ChatChannel.Whisper));

        Assert.Equal(["Friends", "Guild"], Box.CustomTabs.Select(tab => tab.Name));
        Assert.Equal("Friends", Box.SelectedTabName);
        Box.View.Refresh();
        Assert.Equal("psst", Box.View.ShownText.Last());
    }

    [Fact]
    public void AnEditToTheNameOfAnotherTabIsRefused()
    {
        var party = Tab("Party", ChatChannel.Group);
        Box.AddCustomTab(party);
        Box.AddCustomTab(Tab("Guild", ChatChannel.Guild));

        Box.ReplaceCustomTab(party, Tab("Guild", ChatChannel.Say));

        Assert.Equal(["Party", "Guild"], Box.CustomTabs.Select(tab => tab.Name));
    }

    [Fact]
    public void RemovingTheSelectedTabSelectsTheChatTabAndFreesAPlace()
    {
        var party = Tab("Party", ChatChannel.Group);
        Box.AddCustomTab(party);
        Box.AddCustomTab(Tab("Guild", ChatChannel.Guild));
        Box.AddCustomTab(Tab("Whisper", ChatChannel.Whisper));
        Box.Select("Party");

        Box.RemoveCustomTab(party);

        Assert.Equal("Chat", Box.SelectedTabName);
        Assert.Equal(["Guild", "Whisper"], Box.CustomTabs.Select(tab => tab.Name));
        Assert.True(Box.AddTabButton.Visible);

        //a new tab takes a name that no tab of the box had
        Box.AddCustomTab(Tab("Again", ChatChannel.Say));
        Assert.Contains(Box.TabControls, control => control.Name == "custom_tab_3");
        UiAssert.NamedTree(Box);
    }

    [Fact]
    public void ApplyAddsANewTabAndChangesAnEditedOne()
    {
        var party = Tab("Party", ChatChannel.Group);
        Box.Apply(null, party);
        Assert.Same(party, Box.CustomTabs.Single());

        var friends = Tab("Friends", ChatChannel.Whisper);
        Box.Apply(party, friends);
        Assert.Same(friends, Box.CustomTabs.Single());
    }

    [Fact]
    public void AnInvalidTabIsNotAddedOrPutInThePlaceOfOne()
    {
        var party = Tab("Party", ChatChannel.Group);
        Box.AddCustomTab(Tab("Empty"));
        Box.AddCustomTab(Tab("Too long a", ChatChannel.Say));
        Box.AddCustomTab(party);

        Box.ReplaceCustomTab(party, Tab("Party"));

        Assert.Same(party, Box.CustomTabs.Single());
    }

    [Fact]
    public void AChangeOrARemoveOfAnotherTabKeepsTheSelection()
    {
        var party = Tab("Party", ChatChannel.Group);
        var guild = Tab("Guild", ChatChannel.Guild);
        Box.AddCustomTab(party);
        Box.AddCustomTab(guild);
        Box.Select("Guild");

        Box.ReplaceCustomTab(party, Tab("Friends", ChatChannel.Whisper));
        Assert.Equal("Guild", Box.SelectedTabName);

        Box.RemoveCustomTab(Box.CustomTabs[0]);
        Assert.Equal("Guild", Box.SelectedTabName);
        Assert.Same(guild, Box.View.Window.Tab);
    }

    [Fact]
    public void AnEditClearsTheMarkOfTheTab()
    {
        var party = Tab("Party", ChatChannel.Group);
        Box.AddCustomTab(party);
        Box.Select("Chat");
        Log.Add(ChatChannel.Group, "hi", Color.Olive);
        Assert.True(Box.IsMarked("Party"));

        Box.ReplaceCustomTab(party, Tab("Party", ChatChannel.Whisper));

        Assert.False(Box.IsMarked("Party"));
    }

    //the + button follows the tabs and clears the hide button, while the row has space and while the box is hidden
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePlusButtonFollowsTheTabs(bool collapsed)
    {
        Box.AddCustomTab(Tab("Party 12", ChatChannel.Group));
        Box.AddCustomTab(Tab("Guild 12", ChatChannel.Guild));
        Box.SetCollapsed(collapsed);

        var last = Box.TabControls.Last();
        var plus = Box.AddTabButton;

        Assert.True(plus.Visible);
        Assert.Equal(last.X + last.Width + MainHudLayout.CHAT_TAB_GAP, plus.X);
        Assert.Equal(last.Y, plus.Y);
        Assert.True(plus.X + plus.Width <= MainHudLayout.ChatHideButton.Left);
    }

    //a tab with no menu lets a right click go on to its parent, as before
    [Fact]
    public void ARightClickOnATabWithNoMenuIsNotTaken()
    {
        var tab = new SelectableTab("Plain", 40, 13);
        var click = new ClickEvent { Button = MouseButton.Right };

        tab.OnClick(click);

        Assert.False(click.Handled);
    }

    [Fact]
    public void TheFixedTabsCannotBeRemovedOrChanged()
    {
        Box.RemoveCustomTab(ChatTab.Chat);
        Box.ReplaceCustomTab(ChatTab.System, Tab("Mine", ChatChannel.Say));

        Assert.Equal(["System", "Combat", "Chat"], Box.TabList.Select(tab => tab.Name));
    }

    [Fact]
    public void ARightClickOnACustomTabAsksForItsMenu()
    {
        var party = Tab("Party", ChatChannel.Group);
        Box.AddCustomTab(party);
        Box.AddCustomTab(Tab("Guild", ChatChannel.Guild));
        ChatTab? asked = null;
        Box.TabMenuRequested += (tab, _, _) => asked = tab;

        RightClick("tab_chat");
        Assert.Null(asked);

        RightClick("custom_tab_0");
        Assert.Same(party, asked);
    }

    [Fact]
    public void ThePlusButtonAsksForTheEditor()
    {
        var asked = 0;
        Box.AddTabRequested += () => asked++;

        Box.AddTabButton.OnClick(new ClickEvent { Button = MouseButton.Left });

        Assert.Equal(1, asked);
    }

    [Fact]
    public void TheSavedTabsComeBackWithoutAnEventAndOnlyTheValidOnes()
    {
        var changes = 0;
        Box.StateChanged += () => changes++;

        Box.SetCustomTabs(
        [
            Tab("Party", ChatChannel.Group), Tab("party", ChatChannel.Say), Tab("Chat", ChatChannel.Say), Tab("Empty"),
            Tab("Guild", ChatChannel.Guild), Tab("Whisper", ChatChannel.Whisper), Tab("Fourth", ChatChannel.Say)
        ]);

        Assert.Equal(["Party", "Guild", "Whisper"], Box.CustomTabs.Select(tab => tab.Name));
        Assert.Equal(0, changes);
    }
    #endregion

    #region The editor
    [Fact]
    public void TheEditorsChildrenHaveUniqueNames() => UiAssert.NamedTree(new ChatTabEditorControl((_, _) => true));

    [Fact]
    public void OkNeedsAValidFreeNameAndAChannel()
    {
        var editor = new ChatTabEditorControl((name, _) => !string.Equals(name, "Chat", StringComparison.OrdinalIgnoreCase));
        editor.Show();

        Assert.False(editor.CanConfirm);
        Assert.Equal("New chat tab", editor.Title);

        Type(editor.NameBox, "Party");
        Assert.False(editor.CanConfirm);

        editor.BoxOf(ChatChannel.Group).Checked = true;
        Assert.True(editor.CanConfirm);
        Assert.False(editor.OkButton.Enabled);
        editor.Update(new GameTime());
        Assert.True(editor.OkButton.Enabled);

        editor.NameBox.Text = "chat";
        Assert.False(editor.CanConfirm);

        editor.NameBox.Text = "Par-ty";
        Assert.False(editor.CanConfirm);
    }

    [Fact]
    public void EnterConfirmsAndGivesTheEditedTabAndTheNewOne()
    {
        var party = Tab("Party", ChatChannel.Group);
        var editor = new ChatTabEditorControl((_, _) => true);
        (ChatTab? Edited, ChatTab Tab)? confirmed = null;
        editor.Confirmed += (edited, tab) => confirmed = (edited, tab);

        editor.Show(party);
        Assert.Equal("Edit chat tab", editor.Title);
        Assert.Equal("Party", editor.NameBox.Text);
        Assert.True(editor.BoxOf(ChatChannel.Group).Checked);
        Assert.False(editor.BoxOf(ChatChannel.Say).Checked);

        editor.BoxOf(ChatChannel.Say).Checked = true;
        editor.OnKeyDown(new KeyDownEvent { Key = Keys.Enter });

        Assert.False(editor.Visible);
        Assert.Same(party, confirmed!.Value.Edited);
        Assert.Equal("Party", confirmed.Value.Tab.Name);
        Assert.Equal(new HashSet<ChatChannel> { ChatChannel.Group, ChatChannel.Say }, confirmed.Value.Tab.Channels);
    }

    [Fact]
    public void EnterDoesNothingWhileOkIsDisabledAndEscapeCancels()
    {
        var editor = new ChatTabEditorControl((_, _) => true);
        var confirmed = 0;
        editor.Confirmed += (_, _) => confirmed++;
        editor.Show();

        editor.OnKeyDown(new KeyDownEvent { Key = Keys.Enter });
        Assert.True(editor.Visible);

        editor.OnKeyDown(new KeyDownEvent { Key = Keys.Escape });
        Assert.False(editor.Visible);
        Assert.Equal(0, confirmed);

        //Cancel hides it too, and a plain Show opens it for a new tab
        editor.Show(Tab("Party", ChatChannel.Group));
        editor.CancelButton.OnClick(new ClickEvent { Button = MouseButton.Left });
        Assert.False(editor.Visible);

        editor.Show();
        Assert.Equal(string.Empty, editor.NameBox.Text);
        Assert.Equal(0, confirmed);
    }
    #endregion

    private void RightClick(string name)
        => Box.TabControls.Single(control => control.Name == name).OnClick(new ClickEvent { Button = MouseButton.Right });
}
