#region
using Brigid.Data;
#endregion

namespace Brigid.Controls.LobbyLogin;

/// <summary>One button on a menu alert. A click runs <paramref name="Run" />, and the alert then goes away.</summary>
public sealed record MenuAlertAction(string Text, Action? Run = null);

/// <summary>
///     A local alert in the main menu's <see cref="AlertsPanel" />: a tab title, a markdown body and its actions. An alert
///     with the same <paramref name="Id" /> as a shown alert replaces it.
/// </summary>
public sealed record MenuAlert(string Id, string Title, string Body, IReadOnlyList<MenuAlertAction> Actions);

/// <summary>The local alerts that the lobby can show. Each one replaces a popup that blocked the old menu (BRIG-87).</summary>
public static class MenuAlerts
{
    public const string NO_SOUND_ID = "nosound";
    public const string LEGACY_IMPORT_ID = "legacyimport";
    public const string OFFLINE_ID = "offline";
    public const string GREETING_UNREADABLE_ID = "greetingunreadable";
    public const string ASSETS_UNLISTABLE_ID = "assetsunlistable";
    public const string NOT_SAVED_ID_PREFIX = "notsaved:";

    /// <summary>The install command for each distribution family, in display order.</summary>
    public static IReadOnlyList<(string Distro, string Command)> InstallCommands { get; } =
    [
        ("Debian, Ubuntu", "sudo apt install libsdl2-mixer-2.0-0"),
        ("Fedora", "sudo dnf install SDL2_mixer"),
        ("Arch", "sudo pacman -S sdl2_mixer")
    ];

    /// <summary>
    ///     Tells the player that the game runs without sound, because SDL2_mixer or a codec library that it links did not
    ///     load. Only the Linux build uses system libraries, so only on Linux is the fix a package to install. On Windows
    ///     and macOS the mixer ships with the client, and a failure means a damaged install.
    /// </summary>
    /// <param name="linux">Whether to show the Linux install commands. The caller makes the OS check.</param>
    public static MenuAlert NoSound(bool linux)
    {
        var body = linux
            ? "**Brigid could not load its audio libraries.**\n\nInstall the SDL2_mixer package of your distribution, then start Brigid again:\n\n"
              + string.Join('\n', InstallCommands.Select(entry => $"- {entry.Distro}: `{entry.Command}`"))
            : "**Brigid could not load its audio libraries.**\n\nInstall Brigid again to get sound back.";

        return new MenuAlert(NO_SOUND_ID, "No Sound", body, [new MenuAlertAction("Dismiss")]);
    }

    /// <summary>
    ///     Tells the player that the lobby has no connection to the server: the connection was lost, or it did not start.
    ///     Reconnect starts a new lobby connection. The alert goes away when a new connection starts.
    /// </summary>
    public static MenuAlert Offline(Action reconnect)
        => new(
            OFFLINE_ID,
            "Offline",
            "**Brigid has no connection to the server.**\n\nClick Reconnect to connect to the lobby again.",
            [new MenuAlertAction("Reconnect", reconnect)]);

    /// <summary>
    ///     Tells the player that Brigid could not list its assets folder, so it runs with no asset packs: no theme and no
    ///     pack art (BRIG-129). The fix is on the player's computer, so the alert names the folder.
    /// </summary>
    public static MenuAlert AssetsUnlistable(string folder)
        => new(
            ASSETS_UNLISTABLE_ID,
            "No Packs",
            $"**Brigid could not read its assets folder,** so it runs with no asset packs: no theme and no pack art.\n\nCheck that your user account can open `{folder}`, then start Brigid again.",
            [new MenuAlertAction("Dismiss")]);

    /// <summary>
    ///     Tells the player that the server sent a greeting (the lobby notice) that does not decompress, so it does not
    ///     show (BRIG-132). The menu stays usable. The alert goes away when a new lobby connection starts.
    /// </summary>
    public static MenuAlert GreetingUnreadable()
        => new(
            GREETING_UNREADABLE_ID,
            "Notice",
            "**The server notice could not be read.**\n\nThe server sent a notice, but Brigid could not read it. You can still log in.",
            [new MenuAlertAction("OK")]);

    /// <summary>
    ///     Tells the player that Brigid did not save a settings file: the last read of it failed, so the save was refused
    ///     (<see cref="SaveResult.Unreadable" />), or the write failed (<see cref="SaveResult.Failed" />). Another program
    ///     holds the file, or it is denied. Each file path has its own alert.
    /// </summary>
    public static MenuAlert NotSaved(GuardedFile file, SaveResult result)
        => new(
            NOT_SAVED_ID_PREFIX + file.Path,
            "Not Saved",
            $"**Brigid did not save your {file.Description}:** `{file.FileName}` {ProblemOf(result)}.\n\n{NextStepFor(file, result)}",
            [new MenuAlertAction("Dismiss")]);

    /// <summary>The one-line text of a save that did not go through, for the orange bar in the world.</summary>
    public static string NotSavedLine(GuardedFile file, SaveResult result)
        => $"Brigid did not save your {file.Description}: {file.FileName} {ProblemOf(result)}. {NextStepFor(file, result)}";

    private static string ProblemOf(SaveResult result) => result == SaveResult.Unreadable ? "could not be read" : "could not be written";

    //a failed write is tried again at the next change. After a failed read, the client reads a character file again at
    //the next login, and a settings file at the next start
    private static string NextStepFor(GuardedFile file, SaveResult result)
        => result != SaveResult.Unreadable ? "Close the program that holds it, then change it again."
            : file.ReadAgainAtLogin ? "Close the program that holds it, then log in again."
            : "Close the program that holds it, then start Brigid again.";

    /// <summary>
    ///     Offers to import the character settings of the legacy Dark Ages client. The alert does not record a decision when
    ///     the player does not answer, so it shows again at the next start.
    /// </summary>
    public static MenuAlert LegacyImport(Action import, Action startFresh)
        => new(
            LEGACY_IMPORT_ID,
            "Import",
            "**Brigid found character settings from the Dark Ages client:** macros, friends, family and spells.\n\n"
            + "Import them into Brigid, or start fresh. Until you choose, this alert shows each time Brigid starts.",
            [new MenuAlertAction("Import", import), new MenuAlertAction("Start Fresh", startFresh)]);
}
