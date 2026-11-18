using Xunit;

namespace Brigid.SmokeTests;

/// <summary>
///     Specs 2 to 5 of BRIG-184, against the local server. Each spec makes its own character, which starts at map 137
///     (Mileth Inn Room 1), tile 4, 3. On that map, tiles 4 to 6 of row 3 are open, and 6, 4 is a warp to map 200 (Mileth
///     Inn Corridor 1).
/// </summary>
[Collection("Smoke")]
public sealed class ServerSpecs(SmokeServer server)
{
    private const int CORRIDOR_MAP_ID = 200;

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

                await app.PressAsync("Q");
                await app.ClickAsync("pausemenu/exit_menu");
                await app.ClickAsync("ExitConfirmPopup/ok");

                await app.WaitForScreenAsync("LobbyLoginScreen", TimeSpan.FromSeconds(30));
                await app.WaitForShownAsync("login/account/name");
                await app.LogInAsync(character, SmokeServer.START_MAP_ID);
            });
}
