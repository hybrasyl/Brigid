#region
using System.Text.Json.Nodes;
using Brigid.Controls.Components;
using Brigid.Collections;
using Brigid.Controls.LobbyLogin;
using Brigid.Data;
using Brigid.DevModules;
using Brigid.Screens;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.TestDriver;

/// <summary>An error that the driver sends back to the test as the reply to a command.</summary>
internal sealed class DriverException(string message) : Exception(message);

/// <summary>
///     The commands of the test driver (BRIG-184). Each command runs on the game thread as a sequence of steps: a step
///     that yields null waits for the next update, and the step that yields a <see cref="JsonObject" /> ends the command
///     with that result. Input commands spread over updates as a player's input does: a click hovers in one update,
///     presses in the next and releases in the one after, so hover, capture and click run as for a person.
/// </summary>
internal static class DriverCommands
{
    //a screenshot that does not appear in this many updates fails
    private const int SCREENSHOT_UPDATES = 600;

    internal static IEnumerator<JsonObject?> Start(JsonObject request, DriverContext context)
    {
        var command = request["cmd"]?.GetValue<string>() ?? throw new DriverException("the request has no \"cmd\"");

        return command switch
        {
            "ping"       => Result(new JsonObject()),
            "state"      => Result(State(context)),
            "paths"      => Result(Paths()),
            "find"       => Result(Find(context, RequiredString(request, "path"))),
            "alerts"     => Result(Alerts(context)),
            "click"      => Click(request, context),
            "key"        => Key(request, context),
            "text"       => Text(RequiredString(request, "text"), context),
            "wheel"      => Wheel(request, context),
            "screenshot" => Screenshot(context),
            "quit"       => Quit(context),
            _            => throw new DriverException($"unknown command \"{command}\"")
        };
    }

    private static IEnumerator<JsonObject?> Result(JsonObject result)
    {
        yield return result;
    }

    //the screen and the connection; in the world, also the map and the player's tile
    private static JsonObject State(DriverContext context)
    {
        var screen = context.Host.Screens.ActiveScreen;
        var state = new JsonObject
        {
            ["screen"] = screen?.GetType().Name,
            ["connection"] = context.Host.Connection.State.ToString()
        };

        if (screen is WorldScreen world)
        {
            var (id, loaded) = world.MapState;
            state["mapId"] = id;
            state["mapLoaded"] = loaded;

            if (WorldState.GetPlayerEntity() is { } player)
            {
                state["playerX"] = player.TileX;
                state["playerY"] = player.TileY;
            }
        }

        return state;
    }

    //the folders of this client, so that a test does not copy the client's path rules
    private static JsonObject Paths() => new() { ["appRoot"] = AppPaths.AppRoot };

    private static JsonObject Find(DriverContext context, string path)
    {
        if (UiLocator.Find(context.Host.Screens.ActiveScreen?.Root, path) is not { } element)
            return new JsonObject { ["found"] = false };

        return new JsonObject
        {
            ["found"] = true,
            ["path"] = UiLocator.PathOf(element),
            ["type"] = element.GetType().Name,
            ["shown"] = UiLocator.IsShown(element),
            ["enabled"] = element.Enabled,
            ["x"] = element.ScreenX,
            ["y"] = element.ScreenY,
            ["width"] = element.Width,
            ["height"] = element.Height,
            ["text"] = element switch
            {
                UILabel label     => label.Text,
                UITextBox textBox => textBox.Text,
                _                 => null
            }
        };
    }

    private static JsonObject Alerts(DriverContext context)
    {
        var titles = new JsonArray();

        if (context.Host.Screens.ActiveScreen?.Root?.FindChild<MainMenuControl>("mainmenu") is { } menu)
            foreach (var alert in menu.Alerts.Alerts)
                titles.Add(alert.Title);

        return new JsonObject { ["titles"] = titles };
    }

    private static IEnumerator<JsonObject?> Click(JsonObject request, DriverContext context)
    {
        var (x, y, path) = Target(request, context);
        var button = (request["button"]?.GetValue<string>() ?? "left") switch
        {
            "left"  => (byte)1,
            "right" => (byte)3,
            var other => throw new DriverException($"unknown button \"{other}\"")
        };
        var (mods, _) = Modifiers(request);

        //hover first, as a player's mouse arrives before it presses
        InputBuffer.SetPointerOverride(x, y);
        var (windowX, windowY) = InputBuffer.ToWindowPoint(x, y);
        SdlInput.PushMotion(context.WindowId, windowX, windowY);

        yield return null;

        var before = SdlInput.SetModifiers(mods);

        try
        {
            (windowX, windowY) = InputBuffer.ToWindowPoint(x, y);
            SdlInput.PushButton(context.WindowId, button, true, windowX, windowY);

            yield return null;

            SdlInput.PushButton(context.WindowId, button, false, windowX, windowY);
        } finally
        {
            SdlInput.RestoreModifiers(before);
        }

        yield return null;

        yield return new JsonObject { ["x"] = x, ["y"] = y, ["path"] = path };
    }

    private static IEnumerator<JsonObject?> Key(JsonObject request, DriverContext context)
    {
        var name = RequiredString(request, "key");

        if (!Enum.TryParse<Keys>(name, true, out var key) || !SdlInput.TryGetScancode(key, out var scancode))
            throw new DriverException($"unknown key \"{name}\"");

        var (mods, modifierKeys) = Modifiers(request);
        var before = SdlInput.SetModifiers(mods);

        try
        {
            //the modifiers go down first and stay down for one update, so IsKeyHeld sees them when the key arrives
            foreach (var modifier in modifierKeys)
                SdlInput.PushKey(context.WindowId, Scancode(modifier), true, mods);

            SdlInput.PushKey(context.WindowId, scancode, true, mods);

            yield return null;

            SdlInput.PushKey(context.WindowId, scancode, false, mods);

            foreach (var modifier in modifierKeys.AsEnumerable().Reverse())
                SdlInput.PushKey(context.WindowId, Scancode(modifier), false, 0);
        } finally
        {
            SdlInput.RestoreModifiers(before);
        }

        yield return null;

        yield return new JsonObject();
    }

    private static IEnumerator<JsonObject?> Text(string text, DriverContext context)
    {
        SdlInput.PushText(context.WindowId, text);

        yield return null;

        yield return new JsonObject();
    }

    private static IEnumerator<JsonObject?> Wheel(JsonObject request, DriverContext context)
    {
        var notches = request["notches"]?.GetValue<int>() ?? throw new DriverException("the request has no \"notches\"");
        var (x, y, _) = Target(request, context);

        //a wheel event takes the cursor position, so the pointer moves there first
        InputBuffer.SetPointerOverride(x, y);

        yield return null;

        SdlInput.PushWheel(context.WindowId, notches);

        yield return null;

        yield return new JsonObject();
    }

    //the whole window, UI included, through the client's frame capture; the F12 screenshot has no UI
    private static IEnumerator<JsonObject?> Screenshot(DriverContext context)
    {
        var path = Path.Combine(context.Host.OutputDirectory, "test-driver", $"frame-{DateTime.Now:HHmmss-fff}.png");
        bool? saved = null;
        context.Host.Game.RequestFrameCapture(path, result => saved = result);

        //the capture runs in the next draw; a minimized window can skip draws, so the wait has a limit
        for (var i = 0; (i < SCREENSHOT_UPDATES) && saved is null; i++)
            yield return null;

        if (saved != true)
            throw new DriverException(saved is null ? "the client drew no frame for the capture" : $"the frame capture could not be written to {path}");

        yield return new JsonObject { ["path"] = path };
    }

    private static IEnumerator<JsonObject?> Quit(DriverContext context)
    {
        context.ExitRequested = true;

        yield return new JsonObject();
    }

    //the centre of the control at "path", or the virtual point "x", "y"
    private static (int X, int Y, string? Path) Target(JsonObject request, DriverContext context)
    {
        if (request["path"]?.GetValue<string>() is { } path)
        {
            var element = UiLocator.Find(context.Host.Screens.ActiveScreen?.Root, path) ?? throw new DriverException($"no control \"{path}\"");

            if (!UiLocator.IsShown(element))
                throw new DriverException($"the control \"{path}\" is not shown");

            return (element.ScreenX + element.Width / 2, element.ScreenY + element.Height / 2, UiLocator.PathOf(element));
        }

        if (request["x"]?.GetValue<int>() is { } x && request["y"]?.GetValue<int>() is { } y)
            return (x, y, null);

        throw new DriverException("the request has no \"path\" and no \"x\" and \"y\"");
    }

    //"mods": ["shift", "ctrl", "alt", "meta"]; meta is the primary modifier of the OS (Cmd on macOS, else Ctrl)
    private static (ushort Mods, Keys[] Keys) Modifiers(JsonObject request)
    {
        if (request["mods"] is not JsonArray names)
            return (0, []);

        ushort mods = 0;
        var keys = new List<Keys>();

        foreach (var name in names.Select(node => node?.GetValue<string>()))
        {
            var (bit, key) = name switch
            {
                "shift"                              => (SdlInput.KMOD_LSHIFT, Keys.LeftShift),
                "ctrl"                               => (SdlInput.KMOD_LCTRL, Keys.LeftControl),
                "alt"                                => (SdlInput.KMOD_LALT, Keys.LeftAlt),
                "meta" when OperatingSystem.IsMacOS() => (SdlInput.KMOD_LGUI, Keys.LeftWindows),
                "meta"                               => (SdlInput.KMOD_LCTRL, Keys.LeftControl),
                _                                    => throw new DriverException($"unknown modifier \"{name}\"")
            };

            mods |= bit;

            if (!keys.Contains(key))
                keys.Add(key);
        }

        return (mods, keys.ToArray());
    }

    private static int Scancode(Keys key)
        => SdlInput.TryGetScancode(key, out var scancode) ? scancode : throw new DriverException($"no scancode for {key}");

    private static string RequiredString(JsonObject request, string name)
        => request[name]?.GetValue<string>() ?? throw new DriverException($"the request has no \"{name}\"");
}

/// <summary>What the commands need from the module.</summary>
internal sealed class DriverContext(IDevHost host, uint windowId)
{
    internal IDevHost Host { get; } = host;

    internal uint WindowId { get; } = windowId;

    internal bool ExitRequested { get; set; }
}
