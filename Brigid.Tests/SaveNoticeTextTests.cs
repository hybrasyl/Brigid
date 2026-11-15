using Brigid.Controls.LobbyLogin;
using Brigid.Data;
using Xunit;

namespace Brigid.Tests;

// A save that did not go through tells the player which file was not saved and what to do. After a failed read, a
// character file is read again at the next login and a settings file at the next start; a failed write is tried again at
// the next change. Each file path has its own main-menu alert.
public sealed class SaveNoticeTextTests
{
    private readonly string Folder = Path.Combine(Path.GetTempPath(), "brigid-notice-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void AnUnreadableCharacterFileSaysToLogInAgain()
        => Assert.Equal(
            "Brigid did not save your macros: Macro.cfg could not be read. Close the program that holds it, then log in again.",
            MenuAlerts.NotSavedLine(new GuardedFile(Path.Combine(Folder, "Macro.cfg"), "macros"), SaveResult.Unreadable));

    [Fact]
    public void AnUnreadableSettingsFileSaysToStartBrigidAgain()
        => Assert.Equal(
            "Brigid did not save your key bindings: keybinds.json could not be read. Close the program that holds it, then start Brigid again.",
            MenuAlerts.NotSavedLine(GuardedFile.ForSession(Path.Combine(Folder, "keybinds.json"), "key bindings"), SaveResult.Unreadable));

    [Fact]
    public void AFailedWriteSaysToChangeItAgain()
        => Assert.Equal(
            "Brigid did not save your spell chants: SpellBook.cfg could not be written. Close the program that holds it, then change it again.",
            MenuAlerts.NotSavedLine(new GuardedFile(Path.Combine(Folder, "SpellBook.cfg"), "spell chants"), SaveResult.Failed));

    //two characters have a Macro.cfg each
    [Fact]
    public void EachFilePathHasItsOwnAlert()
    {
        var first = MenuAlerts.NotSaved(new GuardedFile(Path.Combine(Folder, "Angelique", "Macro.cfg"), "macros"), SaveResult.Unreadable);
        var other = MenuAlerts.NotSaved(new GuardedFile(Path.Combine(Folder, "Sabrael", "Macro.cfg"), "macros"), SaveResult.Unreadable);
        var settings = MenuAlerts.NotSaved(GuardedFile.ForSession(Path.Combine(Folder, "brigid.cfg"), "settings"), SaveResult.Unreadable);

        Assert.NotEqual(first.Id, other.Id);
        Assert.Contains("`brigid.cfg`", settings.Body);
        Assert.Contains("start Brigid again", settings.Body);
    }
}
