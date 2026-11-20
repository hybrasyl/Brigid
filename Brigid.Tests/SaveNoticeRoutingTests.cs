using Brigid.Controls.Components;
using Brigid.Controls.World.Hud;
using Brigid.Data;
using Brigid.Data.Models;
using Brigid.Screens;
using Brigid.Systems;
using Xunit;

namespace Brigid.Tests;

// ChaosGame gave the saves that did not go through only to the world and the lobby, so a save that failed while the
// launcher showed was lost when the player quit from it. Each screen that shows them is an ISaveNoticeHost now, and the
// launcher draws them below its panel. The routing ran in ChaosGame, which no test reached.
[Collection("GuardedFileNotices")]
public sealed class SaveNoticeRoutingTests : IDisposable
{
    private readonly string Folder = Path.Combine(Path.GetTempPath(), "brigid-notices-" + Guid.NewGuid().ToString("N"));

    public SaveNoticeRoutingTests()
    {
        Directory.CreateDirectory(Folder);
        SaveNotices.Start();

        while (SaveNotices.TryTake(out _, out _)) { }
    }

    public void Dispose() => Directory.Delete(Folder, true);

    private sealed class Host : ISaveNoticeHost
    {
        public readonly List<(string FileName, SaveResult Result)> Shown = [];

        public void ShowSaveNotice(GuardedFile file, SaveResult result) => Shown.Add((file.FileName, result));
    }

    //a save whose write fails: a folder in the place of the file
    private GuardedFile FailedSave(string name)
    {
        var file = new GuardedFile(Path.Combine(Folder, name), "settings");
        Directory.CreateDirectory(file.Path);
        file.Save(path => File.WriteAllText(path, "new"));

        return file;
    }

    [Fact]
    public void AScreenThatShowsNoticesGetsEachWaitingNotice()
    {
        FailedSave("brigid.cfg");
        FailedSave("launcher.json");
        var host = new Host();

        SaveNotices.ShowOn(host);

        Assert.Equal([("brigid.cfg", SaveResult.Failed), ("launcher.json", SaveResult.Failed)], host.Shown);
        Assert.False(SaveNotices.TryTake(out _, out _));
    }

    [Fact]
    public void ANoticeWaitsOnAScreenThatDoesNotShowNotices()
    {
        FailedSave("brigid.cfg");

        SaveNotices.ShowOn(new object());
        SaveNotices.ShowOn(null);
        var host = new Host();
        SaveNotices.ShowOn(host);

        Assert.Equal([("brigid.cfg", SaveResult.Failed)], host.Shown);
    }

    [Fact]
    public void TheLauncherAndTheTwoGameScreensShowNotices()
    {
        Assert.True(typeof(ISaveNoticeHost).IsAssignableFrom(typeof(LauncherScreen)));
        Assert.True(typeof(ISaveNoticeHost).IsAssignableFrom(typeof(LobbyLoginScreen)));
        Assert.True(typeof(ISaveNoticeHost).IsAssignableFrom(typeof(WorldScreen)));
    }

    [Fact]
    public void TheLauncherKeepsALineForEachNotice()
    {
        using var launcher = new LauncherScreen();
        var file = FailedSave("launcher.json");

        SaveNotices.ShowOn(launcher);

        Assert.Single(launcher.NoticesForTests);
        Assert.Contains(file.FileName, launcher.NoticesForTests[0]);
    }

    //one character is one unit wide
    [Theory]
    [InlineData("one two three", 7, new[] { "one two", "three" })]
    [InlineData("one two three", 100, new[] { "one two three" })]
    [InlineData("averylongword ab", 4, new[] { "averylongword", "ab" })]
    [InlineData("", 10, new string[0])]
    public void TheLauncherBreaksANoticeAtSpaces(string text, float width, string[] expected)
        => Assert.Equal(expected, LauncherScreen.WrapWords(text, width, line => line.Length));
}

// The chat box registers with the dispatcher, which lives for the run (the UITextBoxFocus collection holds the
// dispatcher instance).
[Collection("UITextBoxFocus")]
public sealed class ChatInputDisposeTests
{
    //the box goes away with its world screen; it kept its registration, and the lobby after a logout had a chat box that
    //no screen showed
    [Fact]
    public void AChatBoxGivesUpItsRegistrationAtItsDispose()
    {
        using var dispatcher = new InputDispatcher(registerAsInstance: true);
        var chat = new ChatInputControl(new ControlPrefabSet("_test"));
        Assert.NotNull(dispatcher.ChatInputTextBox);

        chat.Dispose();

        Assert.Null(dispatcher.ChatInputTextBox);
    }
}
