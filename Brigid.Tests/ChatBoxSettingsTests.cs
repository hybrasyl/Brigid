#region
using Brigid.Systems;
using Brigid.ViewModel;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>BRIG-145: the chat box opens as the player left it, from <c>brigid.cfg</c>.</summary>
[Collection("ClientSettings")]
public sealed class ChatBoxSettingsTests : IDisposable
{
    private readonly string Root = Path.Combine(Path.GetTempPath(), "brigid-chat-cfg-" + Guid.NewGuid().ToString("N"));
    private readonly string SavedTab = ClientSettings.SelectedChatTab;
    private readonly bool SavedHidden = ClientSettings.ChatBoxHidden;
    private readonly IReadOnlyList<ChatTab> SavedTabs = ClientSettings.ChatTabs;

    public ChatBoxSettingsTests() => Directory.CreateDirectory(Root);

    public void Dispose()
    {
        ClientSettings.SelectedChatTab = SavedTab;
        ClientSettings.ChatBoxHidden = SavedHidden;
        ClientSettings.ChatTabs = SavedTabs;
        Directory.Delete(Root, true);
    }

    [Theory]
    [InlineData("System", true)]
    [InlineData("Chat", false)]
    public void TheTabAndTheHiddenStateComeBackFromTheFile(string tab, bool hidden)
    {
        var path = Path.Combine(Root, "brigid.cfg");
        ClientSettings.SelectedChatTab = tab;
        ClientSettings.ChatBoxHidden = hidden;
        ClientSettings.Save(path);

        ClientSettings.SelectedChatTab = "other";
        ClientSettings.ChatBoxHidden = !hidden;
        ClientSettings.Read(path);

        Assert.Equal(tab, ClientSettings.SelectedChatTab);
        Assert.Equal(hidden, ClientSettings.ChatBoxHidden);
    }

    [Fact]
    public void TheCustomTabsComeBackInTheirOrderAndABadLineIsSkipped()
    {
        var path = Path.Combine(Root, "brigid.cfg");
        ClientSettings.ChatTabs =
        [
            new ChatTab("Party", new HashSet<ChatChannel> { ChatChannel.Group }),
            new ChatTab("Mixed", new HashSet<ChatChannel> { ChatChannel.System, ChatChannel.Whisper })
        ];
        ClientSettings.Save(path);
        File.AppendAllLines(path, ["ChatTab: Bad|Loot"]);

        ClientSettings.ChatTabs = [new ChatTab("Other", new HashSet<ChatChannel> { ChatChannel.Say })];
        ClientSettings.Read(path);

        Assert.Equal(["Party", "Mixed"], ClientSettings.ChatTabs.Select(tab => tab.Name));
        Assert.Equal(new HashSet<ChatChannel> { ChatChannel.System, ChatChannel.Whisper }, ClientSettings.ChatTabs[1].Channels);
    }
}
