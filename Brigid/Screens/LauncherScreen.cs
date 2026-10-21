#region
using Brigid.Controls.Components;
using Brigid.Data;
using Brigid.Definitions;
using Brigid.Rendering;
using Brigid.Systems;
using Brigid.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Screens;

/// <summary>
///     Launcher / connect screen shown on every normal startup (skipped only when env vars fully specify a valid setup;
///     see <see cref="GlobalSettings.ShowLauncher" />). Renders entirely without Dark Ages assets, through
///     <see cref="SystemTextPainter" /> (OS-typeface text and solid rectangles), so it can run before any <c>.dat</c> is
///     loaded. Lets the user pick a saved server (or add one), choose the asset folder via the native
///     <see cref="FolderPicker" />, and connect. On connect it persists <see cref="LauncherConfig" /> and calls
///     <see cref="ChaosGame.FinishAssetInitialization" />, which loads the assets and continues to the lobby.
/// </summary>
public sealed class LauncherScreen : IScreen
{
    private enum Mode
    {
        Main,
        Dropdown,
        ResolutionDropdown,
        AddServer
    }

    private enum AddField
    {
        Name = 0,
        Host = 1,
        Port = 2
    }

    private const float TEXT_SIZE = 13f;
    private const float HINT_SIZE = 11f;
    private const float TITLE_SIZE = 18f;
    private const int ROW_HEIGHT = 24;
    private const int FIELD_GAP = 14;
    private const int HINT_GAP = 4;
    private const int MAX_PORT_DIGITS = 5;

    private static readonly Rectangle Panel = new(110, 90, 420, 290);
    private static readonly Color Backdrop = new(10, 12, 20);
    private static readonly Color PanelFill = new(28, 32, 44);
    private static readonly Color PanelBorder = new(80, 90, 110);
    private static readonly Color FieldFill = new(38, 42, 56);
    private static readonly Color FieldFocusFill = new(48, 54, 70);
    private static readonly Color FieldBorder = new(70, 78, 96);
    private static readonly Color FocusBorder = new(120, 160, 220);
    private static readonly Color LabelColor = new(180, 188, 200);
    private static readonly Color TitleColor = new(235, 240, 250);
    private static readonly Color OkColor = new(120, 210, 130);
    private static readonly Color BadColor = new(225, 130, 120);

    private readonly SystemTextPainter Painter = new();
    private readonly string[] AddText = new string[3];
    private readonly Rectangle[] AddFieldBounds = new Rectangle[3];
    private readonly List<(Rectangle Row, Rectangle Remove, ServerEntry Entry)> DropdownRows = [];
    private readonly List<(Rectangle Row, int Multiplier)> ResolutionRows = [];

    private ChaosGame Game = null!;

    private Mode CurrentMode = Mode.Main;
    private AddField AddFocused = AddField.Host;
    private string AssetPath = "";
    private bool AssetPathValid;
    private bool Completed;

    //Browse is armed on press and fires on release; see HandleMouse
    private bool AssetButtonArmed;
    private double CaretTimer;

    //true when summoned from the in-game main menu (vs the cold-startup launcher). Surfaces a Return-to-Main-Menu
    //button and locks the data-folder selector — the data folder is loaded exactly once at startup and cannot change
    //in-session, so it must stay fixed here.
    private readonly bool OpenedFromMainMenu;

    //recomputed each frame from current state
    private Rectangle DropdownButton;
    private Rectangle ResolutionButton;
    private Rectangle AssetButton;
    private Rectangle ConnectButton;
    private Rectangle SkipLauncherRow;
    private Rectangle ReturnButton;
    private Rectangle AddRow;
    private Rectangle SaveButton;
    private Rectangle CancelButton;

    //cold startup uses the parameterless form (openedFromMainMenu defaults false); the main-menu Config button passes true.
    public LauncherScreen(bool openedFromMainMenu = false) => OpenedFromMainMenu = openedFromMainMenu;

    public UIPanel? Root => null;

    public void Initialize(ChaosGame game)
    {
        Game = game;

        AssetPath = string.IsNullOrWhiteSpace(GlobalSettings.DataPath)
            ? GlobalSettings.DefaultAssetPathGuess
            : GlobalSettings.DataPath;

        RevalidateAssetPath();
    }

    public void LoadContent(GraphicsDevice graphicsDevice) => Painter.LoadContent(graphicsDevice);

    public void Update(GameTime gameTime)
    {
        if (Completed)
            return;

        CaretTimer += gameTime.ElapsedGameTime.TotalSeconds;

        Layout();
        HandleMouse();

        if (Completed)
            return;

        if (CurrentMode == Mode.AddServer)
        {
            HandleTyping();
            HandleAddKeys();
        }
    }

    //the world/render-target pass is unused: the launcher has no world layer and draws entirely in the native pass so its
    //SystemFontText labels render crisp at the window resolution instead of sharing the 640×480 render target's upscale.
    public void Draw(SpriteBatch spriteBatch, GameTime gameTime) { }

    public void DrawNative(SpriteBatch spriteBatch, float scaleX, float scaleY)
    {
        Painter.Begin(spriteBatch, scaleX, scaleY);

        //virtual 640×480 layout drawn under a Scale(sx,sy) transform: solid rects point-scale crisply, and text cancels
        //the transform per draw (rasterized at native size) so it stays sharp. Mirrors the main UI's native pass.
        spriteBatch.Begin(samplerState: GlobalSettings.Sampler, transformMatrix: Matrix.CreateScale(scaleX, scaleY, 1f));

        Painter.FillRect(new Rectangle(0, 0, ChaosGame.VIRTUAL_WIDTH, ChaosGame.VIRTUAL_HEIGHT), Backdrop);
        Painter.FillRect(Panel, PanelFill);
        Painter.BorderRect(Panel, PanelBorder);

        if (CurrentMode == Mode.AddServer)
            DrawAddServer();
        else
            DrawMain();

        spriteBatch.End();
    }

    public void UnloadContent() => Painter.Dispose();

    public void Dispose() => UnloadContent();

    #region Layout

    private void Layout()
    {
        DropdownButton = new Rectangle(Panel.X + 90, Panel.Y + 52, Panel.Right - 20 - (Panel.X + 90), ROW_HEIGHT);
        AssetButton = new Rectangle(Panel.Right - 100, Panel.Y + 150, 80, ROW_HEIGHT);
        ResolutionButton = new Rectangle(Panel.X + 90, Panel.Y + 186, Panel.Right - 20 - (Panel.X + 90), ROW_HEIGHT);
        ConnectButton = new Rectangle(Panel.Right - 120, Panel.Bottom - 40, 100, 26);

        //clickable box + label at the bottom-left; toggles LauncherConfig.SuppressLauncher
        SkipLauncherRow = new Rectangle(Panel.X + 20, Panel.Bottom - 34, 210, 16);

        //return-to-main-menu button, top-right — only present when summoned from the main menu (see OpenedFromMainMenu)
        if (OpenedFromMainMenu)
            ReturnButton = new Rectangle(Panel.Right - 172, Panel.Y + 10, 160, 22);

        DropdownRows.Clear();
        ResolutionRows.Clear();

        if (CurrentMode == Mode.Dropdown)
        {
            var y = DropdownButton.Bottom;

            foreach (var entry in LauncherConfig.Servers)
            {
                var row = new Rectangle(DropdownButton.X, y, DropdownButton.Width, ROW_HEIGHT);
                var remove = new Rectangle(row.Right - ROW_HEIGHT, row.Y, ROW_HEIGHT, ROW_HEIGHT);
                DropdownRows.Add((row, remove, entry));
                y += ROW_HEIGHT;
            }

            AddRow = new Rectangle(DropdownButton.X, y, DropdownButton.Width, ROW_HEIGHT);
        }

        if (CurrentMode == Mode.ResolutionDropdown)
        {
            var max = Game.MaxWindowMultiplierForDisplay();
            var y = ResolutionButton.Bottom;

            for (var m = 1; m <= max; m++)
            {
                ResolutionRows.Add((new Rectangle(ResolutionButton.X, y, ResolutionButton.Width, ROW_HEIGHT), m));
                y += ROW_HEIGHT;
            }
        }

        if (CurrentMode == Mode.AddServer)
        {
            var x = Panel.X + 80;
            var width = Panel.Right - 20 - x;
            var y = Panel.Y + 52;

            for (var i = 0; i < 3; i++)
            {
                AddFieldBounds[i] = new Rectangle(x, y, width, ROW_HEIGHT);
                y += ROW_HEIGHT + FIELD_GAP;

                //the "Name is optional" hint sits under the Name field, so the next field clears its line box
                if (i == 0)
                    y += HINT_GAP + SystemFontText.LineHeight(HINT_SIZE);
            }

            SaveButton = new Rectangle(Panel.Right - 90, Panel.Bottom - 40, 70, 26);
            CancelButton = new Rectangle(Panel.Right - 170, Panel.Bottom - 40, 70, 26);
        }
    }

    #endregion

    #region Input

    private void HandleMouse()
    {
        //consume the discrete per-event stream (same contract as InputDispatcher) rather than edge-detecting the
        //polled IsLeftButtonHeld bool — polling drops a click whose down+up are drained in the same frame, which is
        //timing-dependent and diverged by platform (dead on Windows, fine on macOS).
        foreach (var evt in InputBuffer.Events)
        {
            if ((evt.Kind != BufferedInputKind.MouseButton) || (evt.Button != MouseButton.Left))
                continue;

            var cursor = new Point(evt.X, evt.Y);

            //open the folder picker on the release. The picker stops the game loop until its dialog closes. If it opens on
            //the press, SDL does not see the release, and on X11 SDL keeps the pointer capture of the held button. Then no
            //window, the picker included, gets a click until the process stops.
            if (!evt.IsPress)
            {
                if (AssetButtonArmed && (CurrentMode == Mode.Main) && AssetButton.Contains(cursor))
                    BrowseForAssetFolder();

                AssetButtonArmed = false;

                continue;
            }

            //each press clears an old arm, because a focus change can lose a release
            AssetButtonArmed = false;

            switch (CurrentMode)
            {
                case Mode.Main:
                    HandleMainClick(cursor);

                    break;

                case Mode.Dropdown:
                    HandleDropdownClick(cursor);

                    break;

                case Mode.ResolutionDropdown:
                    HandleResolutionDropdownClick(cursor);

                    break;

                case Mode.AddServer:
                    HandleAddClick(cursor);

                    break;
            }

            //Connect() replaces this screen mid-loop — stop touching state once that happens.
            if (Completed)
                return;
        }
    }

    private void HandleMainClick(Point cursor)
    {
        //return to the main menu without connecting — reuses the already-loaded assets (no FinishAssetInitialization).
        //set Completed so the post-click guards short-circuit the rest of the frame, since Switch disposes this screen.
        if (OpenedFromMainMenu && ReturnButton.Contains(cursor))
        {
            //persist any in-session server pick (resolution and skip-launcher already save on change; Connect, which
            //normally persists the selection, is hidden in this mode). Data folder is locked, so it is unchanged.
            LauncherConfig.Save();

            Completed = true;
            Game.Screens.Switch(new LobbyLoginScreen());

            return;
        }

        if (DropdownButton.Contains(cursor))
        {
            CurrentMode = Mode.Dropdown;

            return;
        }

        //data folder is locked when summoned from the main menu — it can only change at startup
        if (!OpenedFromMainMenu && AssetButton.Contains(cursor))
        {
            AssetButtonArmed = true;

            return;
        }

        if (ResolutionButton.Contains(cursor))
        {
            CurrentMode = Mode.ResolutionDropdown;

            return;
        }

        if (SkipLauncherRow.Contains(cursor))
        {
            LauncherConfig.SuppressLauncher = !LauncherConfig.SuppressLauncher;
            LauncherConfig.Save();

            return;
        }

        //Connect is hidden when summoned from the main menu — connecting re-runs the once-only FinishAssetInitialization
        //(re-news every repository, leaking the prior instances). Return to Main Menu is the only exit in that mode.
        if (!OpenedFromMainMenu && ConnectButton.Contains(cursor) && CanConnect())
            Connect();
    }

    private void HandleResolutionDropdownClick(Point cursor)
    {
        //clicking the button again closes the open dropdown
        if (ResolutionButton.Contains(cursor))
        {
            CurrentMode = Mode.Main;

            return;
        }

        foreach (var (row, multiplier) in ResolutionRows)
            if (row.Contains(cursor))
            {
                //apply live so the window resizes immediately, then persist the (display-clamped) result
                var applied = Game.SetWindowMultiplier(multiplier);
                LauncherConfig.WindowMultiplier = applied;
                LauncherConfig.Save();
                CurrentMode = Mode.Main;

                return;
            }

        //click anywhere else closes the dropdown
        CurrentMode = Mode.Main;
    }

    private void HandleDropdownClick(Point cursor)
    {
        //clicking the button again closes the open dropdown
        if (DropdownButton.Contains(cursor))
        {
            CurrentMode = Mode.Main;

            return;
        }

        foreach (var (row, remove, entry) in DropdownRows)
        {
            if (remove.Contains(cursor))
            {
                LauncherConfig.RemoveServer(entry);
                LauncherConfig.Save();

                return;
            }

            if (row.Contains(cursor))
            {
                LauncherConfig.SelectedServer = entry.Key;
                CurrentMode = Mode.Main;

                return;
            }
        }

        if (AddRow.Contains(cursor))
        {
            AddText[(int)AddField.Name] = "";
            AddText[(int)AddField.Host] = "";
            AddText[(int)AddField.Port] = LauncherConfig.DEFAULT_PORT.ToString();
            AddFocused = AddField.Host;
            CurrentMode = Mode.AddServer;

            return;
        }

        //click anywhere else closes the dropdown
        CurrentMode = Mode.Main;
    }

    private void HandleAddClick(Point cursor)
    {
        for (var i = 0; i < 3; i++)
            if (AddFieldBounds[i].Contains(cursor))
            {
                AddFocused = (AddField)i;

                return;
            }

        if (SaveButton.Contains(cursor))
        {
            SaveNewServer();

            return;
        }

        if (CancelButton.Contains(cursor))
            CurrentMode = Mode.Main;
    }

    private void HandleTyping()
    {
        var typed = InputBuffer.TextInput;

        if (typed.IsEmpty)
            return;

        var index = (int)AddFocused;
        var current = AddText[index];

        foreach (var ch in typed)
        {
            if (ch < ' ' || ch == (char)127)
                continue;

            if (AddFocused == AddField.Port)
            {
                if (!char.IsDigit(ch) || current.Length >= MAX_PORT_DIGITS)
                    continue;
            }

            current += ch;
        }

        AddText[index] = current;
    }

    private void HandleAddKeys()
    {
        if (InputBuffer.WasKeyPressed(Keys.Back))
        {
            var index = (int)AddFocused;

            if (AddText[index].Length > 0)
                AddText[index] = AddText[index][..^1];
        }

        if (InputBuffer.WasKeyPressed(Keys.Tab))
            AddFocused = (AddField)(((int)AddFocused + 1) % 3);

        if (InputBuffer.WasKeyPressed(Keys.Enter))
            SaveNewServer();

        if (InputBuffer.WasKeyPressed(Keys.Escape))
            CurrentMode = Mode.Main;
    }

    #endregion

    #region Actions

    private void BrowseForAssetFolder()
    {
        var picked = FolderPicker.Pick("Select your Dark Ages data folder", AssetPath);

        //the modal ran its own message loop while our game loop was stalled; drop anything the SDL watcher buffered
        //during that time (e.g. a dialog-dismiss click delivered via SDL_HINT_MOUSE_FOCUS_CLICKTHROUGH) so it isn't
        //replayed as a launcher click next frame.
        InputBuffer.DiscardPending();

        //the picker is another process on macOS (osascript) and on Linux (zenity or kdialog), and it can keep the
        //activation when it closes. On macOS the cursor hide at Connect then has no effect, and the OS cursor draws over
        //the game cursor. Raising the window when it is already in front does nothing, so raise it on every platform
        Game.RaiseWindow();

        if (picked is null)
            return;

        AssetPath = picked;
        RevalidateAssetPath();
    }

    private void SaveNewServer()
    {
        var host = AddText[(int)AddField.Host].Trim();

        if (string.IsNullOrWhiteSpace(host))
            return;

        var port = int.TryParse(AddText[(int)AddField.Port], out var parsed) && parsed is >= 1 and <= 65535
            ? parsed
            : LauncherConfig.DEFAULT_PORT;

        LauncherConfig.AddOrSelectServer(host, port, AddText[(int)AddField.Name]);
        LauncherConfig.Save();
        CurrentMode = Mode.Main;
    }

    private void RevalidateAssetPath() => AssetPathValid = DataContext.IsValidDataDirectory(AssetPath?.Trim());

    private bool CanConnect() => LauncherConfig.GetSelectedServer() is not null && AssetPathValid;

    private void Connect()
    {
        var server = LauncherConfig.GetSelectedServer();

        if (server is null)
            return;

        LauncherConfig.AssetPath = AssetPath.Trim();
        LauncherConfig.SelectedServer = server.Key;
        LauncherConfig.Save();

        GlobalSettings.LobbyHost = server.Host;
        GlobalSettings.LobbyPort = server.Port;
        GlobalSettings.DataPath = AssetPath.Trim();

        Completed = true;

        //replaces this screen (UnloadContent + Dispose) with the lobby screen — must be the last thing we do
        Game.FinishAssetInitialization();
    }

    #endregion

    #region Drawing

    private void DrawMain()
    {
        Painter.DrawText("Brigid", new Vector2(Panel.X + 20, Panel.Y + 16), TitleColor, TITLE_SIZE);
        Painter.DrawText("Server", new Vector2(Panel.X + 20, Painter.TextTop(DropdownButton, TEXT_SIZE)), LabelColor, TEXT_SIZE);

        var selected = LauncherConfig.GetSelectedServer();
        Painter.FillRect(DropdownButton, FieldFill);
        Painter.BorderRect(DropdownButton, FieldBorder);

        var labelBox = new Rectangle(DropdownButton.X, DropdownButton.Y, DropdownButton.Width - 18, DropdownButton.Height);
        DrawClippedEnd(selected?.Display ?? "(no servers - add one)", labelBox, selected is null ? BadColor : Color.White, TEXT_SIZE);
        DrawDownTriangle(DropdownButton.Right - 14, DropdownButton.Y + 10, LabelColor);

        DrawAssetRow();
        DrawResolutionRow();
        DrawSkipLauncherCheckbox();

        //Connect is only meaningful at startup (it runs the once-only asset init + enters the lobby). When summoned
        //from the main menu the session is already loaded, so Connect is hidden and Return to Main Menu is the exit.
        if (OpenedFromMainMenu)
            DrawButton(ReturnButton, "Return to Main Menu", true, new Color(50, 54, 68), FieldBorder);
        else
            DrawButton(ConnectButton, "Connect", CanConnect(), new Color(60, 110, 70), new Color(110, 180, 120));

        if (CurrentMode == Mode.Dropdown)
            DrawDropdownList();

        if (CurrentMode == Mode.ResolutionDropdown)
            DrawResolutionDropdownList();
    }

    private void DrawResolutionRow()
    {
        Painter.DrawText("Resolution", new Vector2(Panel.X + 20, Painter.TextTop(ResolutionButton, TEXT_SIZE)), LabelColor, TEXT_SIZE);

        Painter.FillRect(ResolutionButton, FieldFill);
        Painter.BorderRect(ResolutionButton, FieldBorder);

        var labelBox = new Rectangle(ResolutionButton.X, ResolutionButton.Y, ResolutionButton.Width - 18, ResolutionButton.Height);
        DrawClippedEnd(ResolutionLabel(Game.CurrentWindowMultiplier), labelBox, Color.White, TEXT_SIZE);
        DrawDownTriangle(ResolutionButton.Right - 14, ResolutionButton.Y + 10, LabelColor);
    }

    private void DrawResolutionDropdownList()
    {
        foreach (var (row, multiplier) in ResolutionRows)
        {
            var isSelected = multiplier == Game.CurrentWindowMultiplier;
            Painter.FillRect(row, isSelected ? FieldFocusFill : FieldFill);
            Painter.BorderRect(row, FieldBorder);
            DrawClippedEnd(ResolutionLabel(multiplier), row, Color.White, TEXT_SIZE);
        }
    }

    private static string ResolutionLabel(int multiplier) => $"{ChaosGame.VIRTUAL_WIDTH * multiplier}×{ChaosGame.VIRTUAL_HEIGHT * multiplier}";

    private void DrawAssetRow()
    {
        var labelY = Panel.Y + 118;
        var pathBox = new Rectangle(Panel.X + 20, AssetButton.Y, AssetButton.X - 8 - (Panel.X + 20), AssetButton.Height);

        //locked when summoned from the main menu — the data folder loads once at startup and can't change in-session
        if (OpenedFromMainMenu)
        {
            Painter.DrawText("Data folder (startup only)", new Vector2(Panel.X + 20, labelY), LabelColor, TEXT_SIZE);
            DrawClippedFront(AssetPath, pathBox, LabelColor, HINT_SIZE);
            DrawButton(AssetButton, "Locked", false, FieldFill, FieldBorder);

            return;
        }

        Painter.DrawText("Data folder", new Vector2(Panel.X + 20, labelY), LabelColor, TEXT_SIZE);

        if (AssetPathValid)
        {
            DrawClippedFront(AssetPath, pathBox, OkColor, HINT_SIZE);
            DrawButton(AssetButton, "Change", true, new Color(45, 50, 62), FieldBorder);
        } else
        {
            DrawClippedFront("not a Dark Ages data folder (need khanpal.dat + legend.dat)", pathBox, BadColor, HINT_SIZE);
            DrawButton(AssetButton, "Browse", true, new Color(60, 70, 92), FocusBorder);
        }
    }

    private void DrawSkipLauncherCheckbox()
    {
        var box = new Rectangle(SkipLauncherRow.X, SkipLauncherRow.Y, 14, 14);
        Painter.FillRect(box, FieldFill);
        Painter.BorderRect(box, LauncherConfig.SuppressLauncher ? FocusBorder : FieldBorder);

        if (LauncherConfig.SuppressLauncher)
            Painter.FillRect(new Rectangle(box.X + 3, box.Y + 3, 8, 8), OkColor);

        Painter.DrawText("Skip this launcher next time", new Vector2(box.Right + 8, Painter.TextTop(box, HINT_SIZE)), LabelColor, HINT_SIZE);
    }

    private void DrawDropdownList()
    {
        foreach (var (row, remove, entry) in DropdownRows)
        {
            var isSelected = entry.Key == LauncherConfig.SelectedServer;
            Painter.FillRect(row, isSelected ? FieldFocusFill : FieldFill);
            Painter.BorderRect(row, FieldBorder);

            var textBox = new Rectangle(row.X, row.Y, row.Width - ROW_HEIGHT, row.Height);
            DrawClippedEnd(entry.Display, textBox, Color.White, TEXT_SIZE);
            Painter.DrawText("x", new Vector2(remove.X + (remove.Width - Painter.Measure("x", TEXT_SIZE)) / 2f, Painter.TextTop(remove, TEXT_SIZE)), BadColor, TEXT_SIZE);
        }

        Painter.FillRect(AddRow, new Color(44, 60, 50));
        Painter.BorderRect(AddRow, FieldBorder);
        DrawClippedEnd("+ Add server", AddRow, OkColor, TEXT_SIZE);
    }

    private void DrawAddServer()
    {
        Painter.DrawText("Add server", new Vector2(Panel.X + 20, Panel.Y + 16), TitleColor, TITLE_SIZE);

        string[] labels = ["Name", "Host", "Port"];

        for (var i = 0; i < 3; i++)
        {
            var bounds = AddFieldBounds[i];
            var focused = (AddField)i == AddFocused;

            Painter.DrawText(labels[i], new Vector2(Panel.X + 20, Painter.TextTop(bounds, TEXT_SIZE)), LabelColor, TEXT_SIZE);
            Painter.FillRect(bounds, focused ? FieldFocusFill : FieldFill);
            Painter.BorderRect(bounds, focused ? FocusBorder : FieldBorder);

            var textBox = new Rectangle(bounds.X + 6, bounds.Y, bounds.Width - 12, bounds.Height);
            var textEnd = DrawClippedFront(AddText[i], textBox, Color.White, TEXT_SIZE);

            //one pixel past where the text actually ended, spanning the same line box the text was centred in
            if (focused && (int)(CaretTimer * 2) % 2 == 0)
            {
                var caretX = MathF.Min(textEnd + 1, bounds.Right - 6);
                Painter.FillRectSnapped(caretX, Painter.TextTop(bounds, TEXT_SIZE), 1, Painter.TextHeight(TEXT_SIZE), Color.White);
            }
        }

        Painter.DrawText("Name is optional", new Vector2(AddFieldBounds[0].X, AddFieldBounds[0].Bottom + HINT_GAP), LabelColor, HINT_SIZE);

        var canSave = !string.IsNullOrWhiteSpace(AddText[(int)AddField.Host]);
        DrawButton(SaveButton, "Save", canSave, new Color(60, 110, 70), new Color(110, 180, 120));
        DrawButton(CancelButton, "Cancel", true, new Color(45, 50, 62), FieldBorder);
    }

    private void DrawButton(Rectangle rect, string label, bool enabled, Color fill, Color border)
    {
        Painter.FillRect(rect, enabled ? fill : new Color(45, 50, 62));
        Painter.BorderRect(rect, enabled ? border : FieldBorder);

        var width = Painter.Measure(label, TEXT_SIZE);
        var pos = new Vector2(rect.X + (rect.Width - width) / 2f, Painter.TextTop(rect, TEXT_SIZE));
        Painter.DrawText(label, pos, enabled ? Color.White : new Color(120, 128, 140), TEXT_SIZE);
    }

    private void DrawDownTriangle(int centerX, int top, Color color)
    {
        //axis-aligned approximation of a downward triangle: stacked centered rows of decreasing width
        for (var row = 0; row < 4; row++)
        {
            var halfWidth = 4 - row;
            Painter.FillRect(new Rectangle(centerX - halfWidth, top + row, halfWidth * 2 + 1, 1), color);
        }
    }

    //left-aligned within box, trimming the FRONT so the tail (e.g. the end of a path) stays visible. Returns the x where
    //the drawn text ends, so a caret can follow it.
    private float DrawClippedFront(string text, Rectangle box, Color color, float size)
    {
        var x = box.X + 2;

        if (string.IsNullOrEmpty(text))
            return x;

        var maxWidth = box.Width - 4;
        var drawn = text;

        //once trimming starts the ellipsis is part of the drawn width, so measure it with the tail
        if (Painter.Measure(text, size) > maxWidth)
        {
            var shown = text;

            do
                shown = shown[1..];
            while (shown.Length > 1 && Painter.Measure("..." + shown, size) > maxWidth);

            drawn = "..." + shown;
        }

        Painter.DrawText(drawn, new Vector2(x, Painter.TextTop(box, size)), color, size);

        return x + Painter.Measure(drawn, size);
    }

    //left-aligned within box, trimming the END to fit
    private void DrawClippedEnd(string text, Rectangle box, Color color, float size)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var maxWidth = box.Width - 12;
        var shown = text;

        while (shown.Length > 1 && Painter.Measure(shown + "...", size) > maxWidth)
            shown = shown[..^1];

        if (shown.Length < text.Length)
            shown += "...";

        Painter.DrawText(shown, new Vector2(box.X + 6, Painter.TextTop(box, size)), color, size);
    }

    #endregion
}
