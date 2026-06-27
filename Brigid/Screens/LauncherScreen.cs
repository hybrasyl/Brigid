#region
using Brigid.Controls.Components;
using Brigid.Data;
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

    private readonly Dictionary<string, Texture2D> TextCache = new();
    private readonly Dictionary<string, int> WidthCache = new();
    private readonly string[] AddText = new string[3];
    private readonly Rectangle[] AddFieldBounds = new Rectangle[3];
    private readonly List<(Rectangle Row, Rectangle Remove, ServerEntry Entry)> DropdownRows = [];
    private readonly List<(Rectangle Row, int Multiplier)> ResolutionRows = [];

    private ChaosGame Game = null!;
    private SpriteBatch ActiveBatch = null!;
    private Texture2D Pixel = null!;

    //virtual→native draw scale for the current frame; text is rasterized at native size and drawn back down to virtual
    private float NativeScaleX = 1f;
    private float NativeScaleY = 1f;
    private float LastNativeScaleY = 1f;

    private Mode CurrentMode = Mode.Main;
    private AddField AddFocused = AddField.Host;
    private string AssetPath = "";
    private bool AssetPathValid;
    private bool PreviousLeftHeld;
    private bool Completed;
    private double CaretTimer;

    //recomputed each frame from current state
    private Rectangle DropdownButton;
    private Rectangle ResolutionButton;
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

    //the world/render-target pass is unused: the launcher has no world layer and draws entirely in the native pass so its
    //SystemFontText labels render crisp at the window resolution instead of sharing the 640×480 render target's upscale.
    public void Draw(SpriteBatch spriteBatch, GameTime gameTime) { }

    public void DrawNative(SpriteBatch spriteBatch, float scaleX, float scaleY)
    {
        //text textures are rasterized at native pixel size (see GetText/DrawText); flush the cache when the scale changes
        //(e.g. via the resolution selector) so stale-size glyphs are re-rendered crisp at the new size.
        if (scaleY != LastNativeScaleY)
        {
            foreach (var texture in TextCache.Values)
                texture.Dispose();

            TextCache.Clear();
            LastNativeScaleY = scaleY;
        }

        NativeScaleX = scaleX;
        NativeScaleY = scaleY;
        ActiveBatch = spriteBatch;

        //virtual 640×480 layout drawn under a Scale(sx,sy) transform: solid rects point-scale crisply, and text cancels
        //the transform per draw (rasterized at native size) so it stays sharp. Mirrors the main UI's native pass.
        spriteBatch.Begin(samplerState: GlobalSettings.Sampler, transformMatrix: Matrix.CreateScale(scaleX, scaleY, 1f));

        FillRect(new Rectangle(0, 0, ChaosGame.VIRTUAL_WIDTH, ChaosGame.VIRTUAL_HEIGHT), Backdrop);
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
        ResolutionButton = new Rectangle(Panel.X + 90, Panel.Y + 186, Panel.Right - 20 - (Panel.X + 90), ROW_HEIGHT);
        ConnectButton = new Rectangle(Panel.Right - 120, Panel.Bottom - 40, 100, 26);

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

            case Mode.ResolutionDropdown:
                HandleResolutionDropdownClick(cursor);

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

        if (ResolutionButton.Contains(cursor))
        {
            CurrentMode = Mode.ResolutionDropdown;

            return;
        }

        if (ConnectButton.Contains(cursor) && CanConnect())
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
        DrawText("Brigid", new Vector2(Panel.X + 20, Panel.Y + 16), TitleColor, TITLE_SIZE);
        DrawText("Server", new Vector2(Panel.X + 20, DropdownButton.Y + 5), LabelColor, TEXT_SIZE);

        var selected = LauncherConfig.GetSelectedServer();
        FillRect(DropdownButton, FieldFill);
        BorderRect(DropdownButton, FieldBorder);

        var labelBox = new Rectangle(DropdownButton.X, DropdownButton.Y, DropdownButton.Width - 18, DropdownButton.Height);
        DrawClippedEnd(selected?.Display ?? "(no servers - add one)", labelBox, selected is null ? BadColor : Color.White, TEXT_SIZE);
        DrawDownTriangle(DropdownButton.Right - 14, DropdownButton.Y + 10, LabelColor);

        DrawAssetRow();
        DrawResolutionRow();
        DrawButton(ConnectButton, "Connect", CanConnect(), new Color(60, 110, 70), new Color(110, 180, 120));

        if (CurrentMode == Mode.Dropdown)
            DrawDropdownList();

        if (CurrentMode == Mode.ResolutionDropdown)
            DrawResolutionDropdownList();
    }

    private void DrawResolutionRow()
    {
        DrawText("Resolution", new Vector2(Panel.X + 20, ResolutionButton.Y + 5), LabelColor, TEXT_SIZE);

        FillRect(ResolutionButton, FieldFill);
        BorderRect(ResolutionButton, FieldBorder);

        var labelBox = new Rectangle(ResolutionButton.X, ResolutionButton.Y, ResolutionButton.Width - 18, ResolutionButton.Height);
        DrawClippedEnd(ResolutionLabel(Game.CurrentWindowMultiplier), labelBox, Color.White, TEXT_SIZE);
        DrawDownTriangle(ResolutionButton.Right - 14, ResolutionButton.Y + 10, LabelColor);
    }

    private void DrawResolutionDropdownList()
    {
        foreach (var (row, multiplier) in ResolutionRows)
        {
            var isSelected = multiplier == Game.CurrentWindowMultiplier;
            FillRect(row, isSelected ? FieldFocusFill : FieldFill);
            BorderRect(row, FieldBorder);
            DrawClippedEnd(ResolutionLabel(multiplier), row, Color.White, TEXT_SIZE);
        }
    }

    private static string ResolutionLabel(int multiplier) => $"{ChaosGame.VIRTUAL_WIDTH * multiplier}×{ChaosGame.VIRTUAL_HEIGHT * multiplier}";

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

        //the texture is rasterized at native pixel size; cancel the pass transform per draw (scale by its inverse) so it
        //lands at the virtual size but with crisp native detail — the FontEngine native-pass trick, applied to SystemFontText.
        ActiveBatch.Draw(
            GetText(text, color, size),
            position,
            null,
            Color.White,
            0f,
            Vector2.Zero,
            new Vector2(1f / NativeScaleX, 1f / NativeScaleY),
            SpriteEffects.None,
            0f);
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

        //rasterize at native pixel size so the glyph is sharp at the window resolution; DrawText scales it back down to the
        //virtual size. Keyed by virtual size — valid because DrawNative flushes the cache whenever the native scale changes.
        var texture = SystemFontText.Render(text, size * NativeScaleY, color);
        TextCache[key] = texture;

        return texture;
    }

    #endregion
}
