#region
using Brigid.Controls.Components;
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
///     see <see cref="GlobalSettings.ShowLauncher" />). Renders entirely without Dark Ages assets — text via
///     <see cref="SystemFontText" /> (OS default typeface), panels via a 1×1 white texture — so it can run before any
///     <c>.dat</c> is loaded. Lets the user pick a saved server (or add one), choose the asset folder via the native
///     <see cref="FolderPicker" />, and connect. On connect it persists <see cref="LauncherConfig" /> and calls
///     <see cref="ChaosGame.FinishAssetInitialization" />, which loads the assets and continues to the lobby.
/// </summary>
public sealed class LauncherScreen : IScreen
{
    private enum Mode
    {
        Main,
        Dropdown,
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
    private const int MAX_PORT_DIGITS = 5;

    private static readonly Rectangle Panel = new(110, 110, 420, 250);
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

    private readonly Dictionary<string, Texture2D> TextCache = new();
    private readonly Dictionary<string, int> WidthCache = new();
    private readonly string[] AddText = new string[3];
    private readonly Rectangle[] AddFieldBounds = new Rectangle[3];
    private readonly List<(Rectangle Row, Rectangle Remove, ServerEntry Entry)> DropdownRows = [];

    private ChaosGame Game = null!;
    private SpriteBatch ActiveBatch = null!;
    private Texture2D Pixel = null!;

    private Mode CurrentMode = Mode.Main;
    private AddField AddFocused = AddField.Host;
    private string AssetPath = "";
    private bool AssetPathValid;
    private bool PreviousLeftHeld;
    private bool Completed;
    private double CaretTimer;

    //recomputed each frame from current state
    private Rectangle DropdownButton;
    private Rectangle AssetButton;
    private Rectangle ConnectButton;
    private Rectangle AddRow;
    private Rectangle SaveButton;
    private Rectangle CancelButton;

    public UIPanel? Root => null;

    public void Initialize(ChaosGame game)
    {
        Game = game;

        AssetPath = string.IsNullOrWhiteSpace(GlobalSettings.DataPath)
            ? GlobalSettings.DefaultAssetPathGuess
            : GlobalSettings.DataPath;

        RevalidateAssetPath();
    }

    public void LoadContent(GraphicsDevice graphicsDevice)
    {
        Pixel = new Texture2D(graphicsDevice, 1, 1);
        Pixel.SetData(new[] { Color.White });
    }

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

    public void Draw(SpriteBatch spriteBatch, GameTime gameTime)
    {
        ActiveBatch = spriteBatch;
        spriteBatch.Begin(samplerState: GlobalSettings.Sampler);

        FillRect(new Rectangle(0, 0, 640, 480), Backdrop);
        FillRect(Panel, PanelFill);
        BorderRect(Panel, PanelBorder);

        if (CurrentMode == Mode.AddServer)
            DrawAddServer();
        else
            DrawMain();

        spriteBatch.End();
    }

    public void UnloadContent()
    {
        Pixel?.Dispose();
        Pixel = null!;

        foreach (var texture in TextCache.Values)
            texture.Dispose();

        TextCache.Clear();
    }

    public void Dispose() => UnloadContent();

    #region Layout

    private void Layout()
    {
        DropdownButton = new Rectangle(Panel.X + 90, Panel.Y + 52, Panel.Right - 20 - (Panel.X + 90), ROW_HEIGHT);
        AssetButton = new Rectangle(Panel.Right - 100, Panel.Y + 150, 80, ROW_HEIGHT);
        ConnectButton = new Rectangle(Panel.Right - 120, Panel.Bottom - 40, 100, 26);

        DropdownRows.Clear();

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

        if (CurrentMode == Mode.AddServer)
        {
            var x = Panel.X + 80;
            var width = Panel.Right - 20 - x;
            var y = Panel.Y + 52;

            for (var i = 0; i < 3; i++)
            {
                AddFieldBounds[i] = new Rectangle(x, y, width, ROW_HEIGHT);
                y += 38;
            }

            SaveButton = new Rectangle(Panel.Right - 90, Panel.Bottom - 40, 70, 26);
            CancelButton = new Rectangle(Panel.Right - 170, Panel.Bottom - 40, 70, 26);
        }
    }

    #endregion

    #region Input

    private void HandleMouse()
    {
        var leftHeld = InputBuffer.IsLeftButtonHeld;
        var pressed = leftHeld && !PreviousLeftHeld;
        PreviousLeftHeld = leftHeld;

        if (!pressed)
            return;

        var cursor = new Point(InputBuffer.MouseX, InputBuffer.MouseY);

        switch (CurrentMode)
        {
            case Mode.Main:
                HandleMainClick(cursor);

                break;

            case Mode.Dropdown:
                HandleDropdownClick(cursor);

                break;

            case Mode.AddServer:
                HandleAddClick(cursor);

                break;
        }
    }

    private void HandleMainClick(Point cursor)
    {
        if (DropdownButton.Contains(cursor))
        {
            CurrentMode = Mode.Dropdown;

            return;
        }

        if (AssetButton.Contains(cursor))
        {
            BrowseForAssetFolder();

            return;
        }

        if (ConnectButton.Contains(cursor) && CanConnect())
            Connect();
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

        //the modal dialog blocked the game loop; resync the edge tracker so the return click isn't double-counted
        PreviousLeftHeld = InputBuffer.IsLeftButtonHeld;

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

    private void RevalidateAssetPath() => AssetPathValid = LauncherConfig.IsValidAssetPath(AssetPath?.Trim());

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
        DrawText("Brigid", new Vector2(Panel.X + 20, Panel.Y + 16), TitleColor, TITLE_SIZE);
        DrawText("Server", new Vector2(Panel.X + 20, DropdownButton.Y + 5), LabelColor, TEXT_SIZE);

        var selected = LauncherConfig.GetSelectedServer();
        FillRect(DropdownButton, FieldFill);
        BorderRect(DropdownButton, FieldBorder);

        var labelBox = new Rectangle(DropdownButton.X, DropdownButton.Y, DropdownButton.Width - 18, DropdownButton.Height);
        DrawClippedEnd(selected?.Display ?? "(no servers - add one)", labelBox, selected is null ? BadColor : Color.White, TEXT_SIZE);
        DrawDownTriangle(DropdownButton.Right - 14, DropdownButton.Y + 10, LabelColor);

        DrawAssetRow();
        DrawButton(ConnectButton, "Connect", CanConnect(), new Color(60, 110, 70), new Color(110, 180, 120));

        if (CurrentMode == Mode.Dropdown)
            DrawDropdownList();
    }

    private void DrawAssetRow()
    {
        var labelY = Panel.Y + 118;
        DrawText("Data folder", new Vector2(Panel.X + 20, labelY), LabelColor, TEXT_SIZE);

        var pathBox = new Rectangle(Panel.X + 20, labelY + 20, AssetButton.X - 8 - (Panel.X + 20), ROW_HEIGHT - 2);

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

    private void DrawDropdownList()
    {
        foreach (var (row, remove, entry) in DropdownRows)
        {
            var isSelected = entry.Key == LauncherConfig.SelectedServer;
            FillRect(row, isSelected ? FieldFocusFill : FieldFill);
            BorderRect(row, FieldBorder);

            var textBox = new Rectangle(row.X, row.Y, row.Width - ROW_HEIGHT, row.Height);
            DrawClippedEnd(entry.Display, textBox, Color.White, TEXT_SIZE);
            DrawText("x", new Vector2(remove.X + 9, remove.Y + 5), BadColor, TEXT_SIZE);
        }

        FillRect(AddRow, new Color(44, 60, 50));
        BorderRect(AddRow, FieldBorder);
        DrawClippedEnd("+ Add server", AddRow, OkColor, TEXT_SIZE);
    }

    private void DrawAddServer()
    {
        DrawText("Add server", new Vector2(Panel.X + 20, Panel.Y + 16), TitleColor, TITLE_SIZE);

        string[] labels = ["Name", "Host", "Port"];

        for (var i = 0; i < 3; i++)
        {
            var bounds = AddFieldBounds[i];
            var focused = (AddField)i == AddFocused;

            DrawText(labels[i], new Vector2(Panel.X + 20, bounds.Y + 5), LabelColor, TEXT_SIZE);
            FillRect(bounds, focused ? FieldFocusFill : FieldFill);
            BorderRect(bounds, focused ? FocusBorder : FieldBorder);

            var textBox = new Rectangle(bounds.X + 6, bounds.Y, bounds.Width - 12, bounds.Height);
            DrawClippedFront(AddText[i], textBox, Color.White, TEXT_SIZE);

            if (focused && (int)(CaretTimer * 2) % 2 == 0)
            {
                var caretX = Math.Min(bounds.X + 6 + Measure(AddText[i], TEXT_SIZE), bounds.Right - 6);
                FillRect(new Rectangle(caretX, bounds.Y + 5, 1, 14), Color.White);
            }
        }

        DrawText("Name is optional", new Vector2(AddFieldBounds[0].X, AddFieldBounds[0].Bottom + 4), LabelColor, HINT_SIZE);

        var canSave = !string.IsNullOrWhiteSpace(AddText[(int)AddField.Host]);
        DrawButton(SaveButton, "Save", canSave, new Color(60, 110, 70), new Color(110, 180, 120));
        DrawButton(CancelButton, "Cancel", true, new Color(45, 50, 62), FieldBorder);
    }

    private void DrawButton(Rectangle rect, string label, bool enabled, Color fill, Color border)
    {
        FillRect(rect, enabled ? fill : new Color(45, 50, 62));
        BorderRect(rect, enabled ? border : FieldBorder);

        var width = Measure(label, TEXT_SIZE);
        var pos = new Vector2(rect.X + (rect.Width - width) / 2f, rect.Y + (rect.Height - 16) / 2f);
        DrawText(label, pos, enabled ? Color.White : new Color(120, 128, 140), TEXT_SIZE);
    }

    private void DrawDownTriangle(int centerX, int top, Color color)
    {
        //axis-aligned approximation of a downward triangle: stacked centered rows of decreasing width
        for (var row = 0; row < 4; row++)
        {
            var halfWidth = 4 - row;
            ActiveBatch.Draw(Pixel, new Rectangle(centerX - halfWidth, top + row, halfWidth * 2 + 1, 1), color);
        }
    }

    private void FillRect(Rectangle rect, Color color) => ActiveBatch.Draw(Pixel, rect, color);

    private void BorderRect(Rectangle rect, Color color)
    {
        ActiveBatch.Draw(Pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
        ActiveBatch.Draw(Pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), color);
        ActiveBatch.Draw(Pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
        ActiveBatch.Draw(Pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), color);
    }

    private void DrawText(string text, Vector2 position, Color color, float size)
    {
        if (string.IsNullOrEmpty(text))
            return;

        ActiveBatch.Draw(GetText(text, color, size), position, Color.White);
    }

    //left-aligned within box, trimming the FRONT so the tail (e.g. the end of a path) stays visible
    private void DrawClippedFront(string text, Rectangle box, Color color, float size)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var maxWidth = box.Width - 4;
        var shown = text;

        while (shown.Length > 1 && Measure(shown, size) > maxWidth)
            shown = shown[1..];

        var prefix = shown.Length < text.Length ? "..." : "";
        DrawText(prefix + shown, new Vector2(box.X + 2, box.Y + (box.Height - 16) / 2f + 2), color, size);
    }

    //left-aligned within box, trimming the END to fit
    private void DrawClippedEnd(string text, Rectangle box, Color color, float size)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var maxWidth = box.Width - 12;
        var shown = text;

        while (shown.Length > 1 && Measure(shown + "...", size) > maxWidth)
            shown = shown[..^1];

        if (shown.Length < text.Length)
            shown += "...";

        DrawText(shown, new Vector2(box.X + 6, box.Y + (box.Height - 16) / 2f + 2), color, size);
    }

    private int Measure(string text, float size)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var key = $"{size:0.#}|{text}";

        if (WidthCache.TryGetValue(key, out var width))
            return width;

        width = SystemFontText.Measure(text, size);
        WidthCache[key] = width;

        return width;
    }

    private Texture2D GetText(string text, Color color, float size)
    {
        var key = $"{size:0.#}|{color.PackedValue:X8}|{text}";

        if (TextCache.TryGetValue(key, out var cached))
            return cached;

        var texture = SystemFontText.Render(text, size, color);
        TextCache[key] = texture;

        return texture;
    }

    #endregion
}
