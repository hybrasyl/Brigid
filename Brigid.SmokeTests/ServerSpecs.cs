using Xunit;

namespace Brigid.SmokeTests;

/// <summary>
///     Specs 2 to 8 of BRIG-184, against the local server. Each spec makes its own character, which starts at map 137
///     (Mileth Inn Room 1), tile 4, 3. On that map, tiles 4 to 6 of row 3 are open, and 6, 4 is a warp to map 200 (Mileth
///     Inn Corridor 1).
/// </summary>
[Collection("Smoke")]
public sealed class ServerSpecs(SmokeServer server)
{
    private const int CORRIDOR_MAP_ID = 200;
    private const int WARP_X = 6;
    private const int WARP_Y = 4;
    private const string MACRO_BOX = "Macro0";
    private const string MACROS_NOT_SAVED = "Brigid did not save your macros: Macro.cfg could not be read.";

    /// <summary>Spec 2: create a character with a new name, then log in. The world shows the start map.</summary>
    [ServerSmokeFact]
    public Task CreateACharacterThenLogIn()
        => BrigidApp.RunAsync(
            server,
            async app =>
            {
                await app.EnterWorldAsNewCharacterAsync();

                var state = await app.StateAsync();
                Assert.Equal((SmokeServer.START_X, SmokeServer.START_Y), (state.PlayerX, state.PlayerY));
            });

    /// <summary>Spec 3: an arrow key moves the player one tile.</summary>
    [ServerSmokeFact]
    public Task AnArrowKeyWalksOneTile()
        => BrigidApp.RunAsync(
            server,
            async app =>
            {
                await app.EnterWorldAsNewCharacterAsync();

                var state = await app.StepAsync("Right", 1, 0);
                Assert.Equal((SmokeServer.START_X + 1, SmokeServer.START_Y), (state.PlayerX, state.PlayerY));
            });

    /// <summary>Spec 4: a warp tile changes the map, and the client stays in the world (BRIG-180).</summary>
    [ServerSmokeFact]
    public Task AWarpChangesTheMap()
        => BrigidApp.RunAsync(
            server,
            async app =>
            {
                await app.EnterWorldAsNewCharacterAsync();
                await app.StepAsync("Right", 1, 0);
                await app.StepAsync("Right", 1, 0);

                //the warp at 6, 4: the step ends on the new map, so the spec waits for the map, not for the tile
                await app.PressUntilAsync("Down", state => state.MapId == CORRIDOR_MAP_ID, $"map {CORRIDOR_MAP_ID}");

                var corridor = await app.WaitForMapAsync(CORRIDOR_MAP_ID);
                Assert.Equal("World", corridor.Connection);
            });

    /// <summary>Spec 5: Exit to Menu shows the login form, and the same character logs in again.</summary>
    [ServerSmokeFact]
    public Task ExitToMenuThenLogInAgain()
        => BrigidApp.RunAsync(
            server,
            async app =>
            {
                var character = await app.EnterWorldAsNewCharacterAsync();

                await app.ExitToMenuAsync();
                await app.LogInAsync(character, SmokeServer.START_MAP_ID);
            });

    /// <summary>
    ///     Spec 6 (BRIG-175): a focused text box keeps the keys across a map change. The focused box takes the arrow keys,
    ///     so the map change is a right-click on the warp tile. Options covers that tile in the start room, so the spec
    ///     clicks first, then focuses a macro box and types while the player walks the three tiles to the warp.
    /// </summary>
    [ServerSmokeFact]
    public Task AFocusedBoxKeepsTheKeysAcrossAMapChange()
        => BrigidApp.RunAsync(
            server,
            async app =>
            {
                await app.EnterWorldAsNewCharacterAsync();

                await app.ClickTileAsync(WARP_X, WARP_Y, "right");
                await app.OpenMacrosAsync();
                await app.FillAsync(MACRO_BOX, "abc");

                if ((await app.StateAsync()).MapId != SmokeServer.START_MAP_ID)
                    throw new InvalidOperationException("the map changed before the macro box had the focus, so this run tests nothing; the walk to the warp is too short for this machine");

                await app.WaitForMapAsync(CORRIDOR_MAP_ID);
                await app.WaitForShownAsync(MACRO_BOX);

                await app.TypeAsync("def");
                await app.WaitForAsync(() => app.FindAsync(MACRO_BOX), box => box?.Text == "abcdef", "the macro box to hold \"abcdef\"");
            });

    /// <summary>
    ///     Spec 7 (BRIG-177): a <c>Macro.cfg</c> that another program holds at world entry. The client enters the world,
    ///     refuses the save of the macros, and shows one notice. The file keeps its text, and the macros come back at the
    ///     next login after the file is free.
    /// </summary>
    [ServerSmokeFact]
    public Task ALockedMacroFileAtWorldEntry()
        => BrigidApp.RunAsync(
            server,
            async app =>
            {
                var character = await app.EnterWorldAsNewCharacterAsync();
                await app.OpenMacrosAsync();
                await app.FillAsync(MACRO_BOX, "kept");
                await app.CloseOptionsAsync();

                var macroFile = await app.MacroFileAsync() ?? throw new InvalidOperationException("the client has no character folder in the world");
                await app.WaitForAsync(() => Task.FromResult(ReadOrEmpty(macroFile)), text => text.Contains("kept"), $"{macroFile} to hold the macro");
                await app.ExitToMenuAsync();

                using (File.Open(macroFile, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    await app.LogInAsync(character, SmokeServer.START_MAP_ID);
                    await app.OpenMacrosAsync();
                    Assert.Equal(string.Empty, (await app.FindAsync(MACRO_BOX))?.Text);

                    await app.FillAsync(MACRO_BOX, "lost");
                    await app.CloseOptionsAsync();
                    await app.WaitForAsync(app.OrangeBarAsync, lines => lines.Any(IsMacrosNotSaved), "the notice that the macros were not saved");

                    //a second refused save shows no second notice
                    await app.OpenMacrosAsync();
                    await app.FillAsync(MACRO_BOX, "again");
                    await app.CloseOptionsAsync();
                    Assert.Null(await app.TryWaitForAsync(app.OrangeBarAsync, lines => lines.Count(IsMacrosNotSaved) > 1, TimeSpan.FromSeconds(3)));
                }

                var text = File.ReadAllText(macroFile);
                Assert.Contains("kept", text);
                Assert.DoesNotContain("lost", text);

                await app.ExitToMenuAsync();
                await app.LogInAsync(character, SmokeServer.START_MAP_ID);
                await app.OpenMacrosAsync();
                await app.WaitForAsync(() => app.FindAsync(MACRO_BOX), box => box?.Text == "kept", "the macro from the file after the file is free");
            });

    /// <summary>
    ///     Spec 8 (BRIG-176): the server goes away. The world screen shows "Connection lost." and stays; its OK goes to
    ///     the lobby, which is offline while the server is down. The next spec starts the server again.
    /// </summary>
    [ServerSmokeFact]
    public Task TheServerGoesAway()
        => BrigidApp.RunAsync(
            server,
            async app =>
            {
                await app.EnterWorldAsNewCharacterAsync();
                await server.StopServiceAsync();

                var message = await app.WaitForShownAsync("DisconnectPopup/message", TimeSpan.FromSeconds(30));
                Assert.Equal("Connection lost.", message.Text);

                //the client does not leave the world by itself
                Assert.Null(await app.TryWaitForAsync(app.StateAsync, state => state.Screen != "WorldScreen", TimeSpan.FromSeconds(3)));

                await app.ClickAsync("DisconnectPopup/ok");
                await app.WaitForScreenAsync("LobbyLoginScreen");
                await app.WaitForAsync(app.AlertsAsync, titles => titles.Contains("Offline"), "the Offline alert at the main menu", TimeSpan.FromSeconds(30));
            });

    private static bool IsMacrosNotSaved(string line) => line.StartsWith(MACROS_NOT_SAVED, StringComparison.Ordinal);

    //the client can hold the file for a moment while it writes it
    private static string ReadOrEmpty(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        } catch (IOException)
        {
            return string.Empty;
        }
    }
}
