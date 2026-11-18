#region
using Brigid.Screens;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using DALib.Networking.Packets.Server;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-145 phase 1: the one chat log. Each message is one entry with its channel; the legacy chat panel and the
///     orange bar select their entries by channel, so the classic and large HUDs show what they showed before.
/// </summary>
public sealed class ChatLogTests
{
    [Fact]
    public void AnEntryKeepsItsTextColourAndChannelInOrder()
    {
        var chat = new Chat();
        var added = new List<ChatEntry>();
        chat.EntryAdded += added.Add;

        chat.Add(ChatChannel.Say, "a", Color.White);
        chat.Add(ChatChannel.Whisper, "b", Color.Purple);

        Assert.Equal([new ChatEntry("a", Color.White, ChatChannel.Say), new ChatEntry("b", Color.Purple, ChatChannel.Whisper)], chat.Entries);
        Assert.Equal(chat.Entries, added);
    }

    [Fact]
    public void TheLogKeepsTheNewest1000Entries()
    {
        var chat = new Chat();

        for (var i = 0; i < 1003; i++)
            chat.Add(ChatChannel.Say, $"m{i}", Color.White);

        Assert.Equal(1000, chat.Entries.Count);
        Assert.Equal("m3", chat.Entries[0].Text);
        Assert.Equal("m1002", chat.Entries[^1].Text);
    }

    [Fact]
    public void AnEntryKeepsItsSequenceNumberWhileTheCapRemovesOlderEntries()
    {
        var chat = new Chat();

        for (var i = 0; i < 1003; i++)
            chat.Add(ChatChannel.Say, $"m{i}", Color.White);

        Assert.Equal(3, chat.FirstSequence);
        Assert.Equal("m3", chat.Entries[0].Text);

        chat.Clear();
        chat.Add(ChatChannel.Say, "after", Color.White);

        Assert.Equal(1003, chat.FirstSequence);
    }

    [Fact]
    public void TheOrangeBarHistoryHoldsOnlyItsChannels()
    {
        var chat = new Chat();

        foreach (var channel in Enum.GetValues<ChatChannel>())
            chat.Add(channel, channel.ToString(), Color.White);

        Assert.Equal(["Whisper", "Group", "Guild", "System", "Combat"], chat.GetOrangeBarHistory().Select(e => e.Text));
        Assert.Equal(Enum.GetValues<ChatChannel>().Length, chat.Entries.Count);
    }

    [Theory]
    [InlineData(ChatChannel.Say, true, false)]
    [InlineData(ChatChannel.Shout, true, false)]
    [InlineData(ChatChannel.Npc, true, false)]
    [InlineData(ChatChannel.Whisper, true, true)]
    [InlineData(ChatChannel.Group, true, true)]
    [InlineData(ChatChannel.Guild, true, true)]
    [InlineData(ChatChannel.Combat, true, true)]
    [InlineData(ChatChannel.System, false, true)]
    public void EachChannelShowsWhereTheLegacyHudShowedIt(ChatChannel channel, bool chatPanel, bool orangeBar)
    {
        Assert.Equal(chatPanel, Chat.ShowsInChatPanel(channel));
        Assert.Equal(orangeBar, Chat.ShowsInOrangeBar(channel));
    }

    [Fact]
    public void ClearEmptiesTheLogAndTheHistory()
    {
        var chat = new Chat();
        var cleared = 0;
        chat.Cleared += () => cleared++;
        chat.Add(ChatChannel.System, "x", Color.Orange);

        chat.Clear();

        Assert.Empty(chat.Entries);
        Assert.Empty(chat.GetOrangeBarHistory());
        Assert.Equal(1, cleared);
    }

    //the forms of CombatEvents.cs in hybrasyl/server; a loot event ends with an empty line
    [Theory]
    [InlineData("[combat] Damage: Sabrael: Assail on Rat (None, Physical)")]
    [InlineData("[combat] CRIT Damage: Sabrael: Assail on Rat (None, Physical)")]
    [InlineData("  [Applied] 12 dmg EResist 3")]
    [InlineData("  [Immune] 0 dmg")]
    [InlineData("[combat] Heal: Ard Ioc on Sabrael: 300 (modified 0)")]
    [InlineData("[combat] [Loot] XP 120 Gold 15")]
    [InlineData("[combat] [Status] Applied: Poison on Rat")]
    [InlineData("[LOOT DENIED]: Rat: not in the group")]
    [InlineData("")]
    public void ACombatLogLineIsCombat(string text)
    {
        Assert.True(CombatLog.IsCombatLine(text));
        Assert.Equal(ChatChannel.Combat, WorldScreen.ServerChannelOf(ServerMessageType.GroupChat, text));
    }

    [Theory]
    [InlineData("[!Sabrael] [combat] Damage: fake")]
    [InlineData("[!Sabrael] hello")]
    [InlineData("Damage: Sabrael: Assail on Rat")]
    [InlineData("[combat]Damage")]
    [InlineData("[COMBAT] Damage")]
    [InlineData(" [Applied] 12 dmg")]
    [InlineData("[Notice] Sabrael joined the group")]
    public void OtherGroupMessagesAreGroup(string text)
    {
        Assert.False(CombatLog.IsCombatLine(text));
        Assert.Equal(ChatChannel.Group, WorldScreen.ServerChannelOf(ServerMessageType.GroupChat, text));
    }

    [Theory]
    [InlineData(PublicMessageType.Normal, false, ChatChannel.Say)]
    [InlineData(PublicMessageType.Normal, true, ChatChannel.Npc)]
    [InlineData(PublicMessageType.Shout, false, ChatChannel.Shout)]
    [InlineData(PublicMessageType.Shout, true, ChatChannel.Shout)]
    public void APublicMessageHasTheChannelOfItsTypeAndSource(PublicMessageType type, bool isNpc, ChatChannel expected)
        => Assert.Equal(expected, WorldScreen.PublicChannelOf(type, isNpc));

    [Theory]
    [InlineData(ServerMessageType.Whisper, ChatChannel.Whisper)]
    [InlineData(ServerMessageType.GuildChat, ChatChannel.Guild)]
    [InlineData(ServerMessageType.ActiveMessage, ChatChannel.System)]
    [InlineData(ServerMessageType.OrangeBar1, ChatChannel.System)]
    [InlineData(ServerMessageType.OrangeBar2, ChatChannel.System)]
    [InlineData(ServerMessageType.OrangeBar3, ChatChannel.System)]
    [InlineData(ServerMessageType.AdminMessage, ChatChannel.System)]
    [InlineData(ServerMessageType.OrangeBar5, ChatChannel.System)]
    [InlineData((ServerMessageType)250, ChatChannel.System)]
    [InlineData(ServerMessageType.PersistentMessage, null)]
    [InlineData(ServerMessageType.ScrollWindow, null)]
    [InlineData(ServerMessageType.NonScrollWindow, null)]
    [InlineData(ServerMessageType.WoodenBoard, null)]
    [InlineData(ServerMessageType.UserOptions, null)]
    [InlineData(ServerMessageType.ClosePopup, null)]
    public void AServerMessageHasTheChannelOfItsType(ServerMessageType type, ChatChannel? expected)
        => Assert.Equal(expected, WorldScreen.ServerChannelOf(type, "text"));

    [Theory]
    [InlineData(ServerMessageType.Whisper, true)]
    [InlineData(ServerMessageType.GroupChat, true)]
    [InlineData(ServerMessageType.GuildChat, true)]
    [InlineData(ServerMessageType.ActiveMessage, true)]
    [InlineData(ServerMessageType.OrangeBar1, false)]
    [InlineData(ServerMessageType.AdminMessage, false)]
    [InlineData(ServerMessageType.PersistentMessage, false)]
    public void ThePaneShowsTheTypesItShowedBefore(ServerMessageType type, bool shows)
        => Assert.Equal(shows, WorldScreen.ShowsInSystemPane(type));
}
