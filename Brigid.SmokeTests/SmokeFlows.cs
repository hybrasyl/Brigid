namespace Brigid.SmokeTests;

/// <summary>A character that a spec made: its name and password.</summary>
public sealed record SmokeCharacter(string Name, string Password);

/// <summary>
///     The player flows that the specs share (BRIG-184): the main menu, character creation, login, a map, a step. Each
///     flow clicks and types as a player does, and waits for a state, never for a fixed time. Control paths are full
///     paths: some short names find another control first (<c>create</c> is the menu button, not the creator's).
/// </summary>
public static class SmokeFlows
{
    private static readonly TimeSpan ServerWait = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     A new character for this run: a name of 4 to 12 letters and a password of 8 letters and digits, the server's
    ///     rules. A smoke server starts with no characters, so a random name does not collide.
    /// </summary>
    public static SmokeCharacter NewCharacter()
    {
        const string LETTERS = "abcdefghijklmnopqrstuvwxyz";
        const string DIGITS = "0123456789";
        var name = "Smk" + new string(Random.Shared.GetItems(LETTERS.AsSpan(), 8));
        var password = new string(Random.Shared.GetItems(LETTERS.AsSpan(), 4)) + new string(Random.Shared.GetItems(DIGITS.AsSpan(), 4));

        return new SmokeCharacter(char.ToUpperInvariant(name[0]) + name[1..], password);
    }

    /// <summary>
    ///     Waits until the main menu can create a character or log in. A new client first shows the server greeting, which
    ///     this answers with OK; its Cancel would close the client.
    /// </summary>
    public static async Task WaitForMenuAsync(this BrigidApp app)
    {
        var (greeting, _) = await app.WaitForAsync(
            async () => (await app.FindAsync("servergreeting"), await app.FindAsync("mainmenu/buttons/create")),
            found => found.Item1 is { Shown: true } || found.Item2 is { Shown: true, Enabled: true },
            "the server greeting or a ready main menu",
            ServerWait);

        if (greeting is { Shown: true })
            await app.ClickAsync("servergreeting/ok");

        await app.WaitForAsync(() => app.FindAsync("mainmenu/buttons/create"), create => create is { Shown: true, Enabled: true }, "a ready main menu", ServerWait);
    }

    /// <summary>Creates <paramref name="character" /> from the main menu and comes back to it.</summary>
    public static async Task CreateCharacterAsync(this BrigidApp app, SmokeCharacter character)
    {
        await app.ClickAsync("mainmenu/buttons/create");
        await app.FillAsync("charcreate/account/name", character.Name);
        await app.FillAsync("charcreate/account/password", character.Password);
        await app.FillAsync("charcreate/account/confirm", character.Password);
        await app.ClickAsync("charcreate/create");

        //the result is the popup, or an error in the creator's status line
        var (popup, status) = await app.WaitForAsync(
            async () => (await app.FindAsync("LobbyLoginPopupMessage/message"), await app.FindAsync("charcreate/status")),
            found => found.Item1 is { Shown: true },
            "the character to be created",
            ServerWait);

        if (popup?.Text?.Contains("created", StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidOperationException($"the character was not created: \"{popup?.Text}\" (creator status: \"{status?.Text}\")");

        await app.ClickAsync("LobbyLoginPopupMessage/ok");
    }

    /// <summary>Opens the login form from the main menu, logs in, and waits for the world.</summary>
    public static async Task<ClientState> LogInFromMenuAsync(this BrigidApp app, SmokeCharacter character, int mapId)
    {
        await app.ClickAsync("mainmenu/buttons/continue");

        return await app.LogInAsync(character, mapId);
    }

    /// <summary>Fills the open login form, logs in, and waits until the world shows map <paramref name="mapId" />.</summary>
    public static async Task<ClientState> LogInAsync(this BrigidApp app, SmokeCharacter character, int mapId)
    {
        await app.FillAsync("login/account/name", character.Name);
        await app.FillAsync("login/account/password", character.Password);
        await app.ClickAsync("login/log_in");

        return await app.WaitForMapAsync(mapId);
    }

    /// <summary>Makes a new character and takes it into the world at the start room.</summary>
    public static async Task<SmokeCharacter> EnterWorldAsNewCharacterAsync(this BrigidApp app)
    {
        var character = NewCharacter();
        await app.WaitForMenuAsync();
        await app.CreateCharacterAsync(character);
        await app.LogInFromMenuAsync(character, SmokeServer.START_MAP_ID);

        return character;
    }

    /// <summary>
    ///     Waits until the world screen shows map <paramref name="mapId" /> with its tiles: the map id, a player, and the
    ///     map loading bar hidden. A map that is not cached streams in after its id arrives, and the loading bar hides when
    ///     the last row is in.
    /// </summary>
    public static async Task<ClientState> WaitForMapAsync(this BrigidApp app, int mapId)
    {
        //the loading bar must be found and hidden: a renamed bar must fail the wait, not pass it
        var (state, _) = await app.WaitForAsync(
            async () => (await app.StateAsync(), await app.FindAsync("MapLoading")),
            found => found.Item1 is { Screen: "WorldScreen", Connection: "World", MapLoaded: true, PlayerX: not null } world
                     && (world.MapId == mapId)
                     && found.Item2 is { Shown: false },
            $"map {mapId} to load in the world",
            ServerWait);

        return state;
    }

    /// <summary>
    ///     Walks one tile with <paramref name="key" /> and returns the new state. A first press can only turn the
    ///     character, so the key is pressed again until the tile changes.
    /// </summary>
    public static async Task<ClientState> StepAsync(this BrigidApp app, string key, int dx, int dy)
    {
        var start = await app.StateAsync();
        var (x, y) = (start.PlayerX!.Value + dx, start.PlayerY!.Value + dy);

        return await app.PressUntilAsync(key, state => (state.PlayerX == x) && (state.PlayerY == y), $"the player at {x}, {y}");
    }

    /// <summary>
    ///     Presses <paramref name="key" /> until <paramref name="condition" /> is true, at most three times, and returns the
    ///     state. A first arrow key can only turn the character, so a walk can take a second press.
    /// </summary>
    public static async Task<ClientState> PressUntilAsync(this BrigidApp app, string key, Func<ClientState, bool> condition, string what)
    {
        for (var press = 0; press < 3; press++)
        {
            await app.PressAsync(key);

            if (await app.TryWaitForAsync(app.StateAsync, condition, TimeSpan.FromSeconds(2)) is { } state)
                return state;
        }

        throw new TimeoutException($"three presses of {key} did not give {what}; the last value was {await app.StateAsync()}");
    }

    /// <summary>Clicks a text box and types <paramref name="text" /> into it.</summary>
    public static async Task FillAsync(this BrigidApp app, string path, string text)
    {
        await app.ClickAsync(path);
        await app.TypeAsync(text);
    }
}
