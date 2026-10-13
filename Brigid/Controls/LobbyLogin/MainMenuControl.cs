#region
using System.Reflection;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Brigid.Networking;
using Brigid.Rendering;
using Brigid.Systems;
using Brigid.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.LobbyLogin;

/// <summary>
///     The lobby main menu, made from asset-free primitives (BRIG-107). It fills the 640×480 layout: a full-window
///     background, a centred title, a column of buttons on the right and a status line at the bottom left. It sets
///     <see cref="UIElement.UseTheme" />, so every part of it draws the <c>ui_theme</c> pack's art where the pack has it
///     and keeps the flat <see cref="DialogPalette" /> look where it does not.
///     <para>
///         The control raises no events of its own. The screen wires each button's <c>Clicked</c>, as it does for the
///         legacy <see cref="LobbyLoginControl" />. About opens its own <see cref="MarkdownView" />.
///     </para>
/// </summary>
public sealed class MainMenuControl : UIPanel
{
    private const string TITLE_TEXT = "Hybrasyl";
    private const int TITLE_SIZE = 56;

    private const int BUTTON_WIDTH = 132;
    private const int BUTTON_HEIGHT = 24;
    private const int BUTTON_GAP = 6;
    private const int BUTTON_COUNT = 8;
    private const int BUTTON_COLUMN_RIGHT_MARGIN = 36;
    private const int BUTTON_COLUMN_TOP = 150;

    private const int STATUS_MARGIN = 10;
    private const int STATUS_GAP = 10;

    //emoji ink is square and fills its em, so the padlock overruns a row built on the 12px text cell; the server label
    //is this much taller, centred on the row, as the legacy connection line is
    private const int PADLOCK_HEADROOM = 4;

    private static readonly Rectangle TitleBounds = new(100, 40, 440, 90);

    private readonly UIPanel Status;
    private readonly UILabel VersionLabel;
    private readonly UILabel ServerLabel;
    private readonly LinkLabel UpdateNotice;

    //the buttons that need a lobby connection. Config, About and Exit work without one; Story waits for StoryView.
    private readonly TextButton[] ConnectionButtons;

    //made on the first click, so the menu needs no game data until About opens
    private MarkdownView? AboutView;

    //the FontEngine generation the status line was measured at; a resize or a face change measures it again
    private int StatusLayoutGeneration = -1;

    public TextButton CreateButton { get; }
    public TextButton ContinueButton { get; }
    public TextButton PasswordButton { get; }
    public TextButton AboutButton { get; }
    public TextButton HomepageButton { get; }
    public TextButton StoryButton { get; }
    public TextButton ConfigButton { get; }
    public TextButton ExitButton { get; }

    public MainMenuControl()
    {
        Name = "mainmenu";
        X = 0;
        Y = 0;
        Width = ChaosGame.VIRTUAL_WIDTH;
        Height = ChaosGame.VIRTUAL_HEIGHT;
        UseTheme = true;

        AddChild(
            new DisplayTitle
            {
                Name = "title",
                X = TitleBounds.X,
                Y = TitleBounds.Y,
                Width = TitleBounds.Width,
                Height = TitleBounds.Height,
                Text = TITLE_TEXT,
                Size = TITLE_SIZE,
                ArtRole = ThemeRoles.MainMenuTitle,
                IsHitTestVisible = false
            });

        var buttons = new UIPanel
        {
            Name = "buttons",
            X = Width - BUTTON_COLUMN_RIGHT_MARGIN - BUTTON_WIDTH,
            Y = BUTTON_COLUMN_TOP,
            Width = BUTTON_WIDTH,
            Height = BUTTON_COUNT * BUTTON_HEIGHT + (BUTTON_COUNT - 1) * BUTTON_GAP,
            IsPassThrough = true
        };

        AddChild(buttons);

        CreateButton = AddMenuButton(buttons, "create", "Create", 0);
        ContinueButton = AddMenuButton(buttons, "continue", "Continue", 1);
        PasswordButton = AddMenuButton(buttons, "password", "Password", 2);
        AboutButton = AddMenuButton(buttons, "about", "About", 3);
        HomepageButton = AddMenuButton(buttons, "homepage", "Homepage", 4);
        StoryButton = AddMenuButton(buttons, "story", "Story", 5);
        ConfigButton = AddMenuButton(buttons, "config", "Config", 6);
        ExitButton = AddMenuButton(buttons, "exit", "Exit", 7);

        ConnectionButtons = [CreateButton, ContinueButton, PasswordButton, HomepageButton];

        //Story opens StoryView, which BRIG-107 phase 3 adds
        StoryButton.SetEnabled(false);

        //one row at the bottom left; LayoutStatusLine places the three labels inside it
        Status = new UIPanel
        {
            Name = "status",
            X = STATUS_MARGIN,
            Y = Height - STATUS_MARGIN - TextRenderer.CHAR_HEIGHT - PADLOCK_HEADROOM / 2,
            Width = Width - 2 * STATUS_MARGIN,
            Height = TextRenderer.CHAR_HEIGHT + PADLOCK_HEADROOM,
            IsPassThrough = true
        };

        VersionLabel = new UILabel
        {
            Name = "version",
            Y = PADLOCK_HEADROOM / 2,
            Height = TextRenderer.CHAR_HEIGHT,
            Text = $"Brigid v{VersionInfo.Display}",
            ShrinkToFit = false,
            IsHitTestVisible = false
        };

        ServerLabel = new UILabel
        {
            Name = "server",
            Y = 0,
            Height = TextRenderer.CHAR_HEIGHT + PADLOCK_HEADROOM,
            VerticalAlignment = VerticalAlignment.Center,
            ShrinkToFit = false,
            IsHitTestVisible = false
        };

        UpdateNotice = new LinkLabel
        {
            Name = "update",
            Y = PADLOCK_HEADROOM / 2,
            Height = TextRenderer.CHAR_HEIGHT,
            ForegroundColor = DialogPalette.UpdateLink,
            HoverColor = DialogPalette.UpdateLinkHover,
            TextStyle = FontStyle.Bold,
            Visible = false
        };

        UpdateNotice.Clicked += () => Browser.Open(UpdateChecker.Available?.Url ?? string.Empty);

        Status.AddChild(VersionLabel);
        Status.AddChild(ServerLabel);
        Status.AddChild(UpdateNotice);
        AddChild(Status);

        AboutButton.Clicked += ShowAbout;

        SetButtonsEnabled(false);
    }

    /// <summary>Enables the buttons that need a lobby connection, after the server greeting.</summary>
    public void EnableButtons() => SetButtonsEnabled(true);

    /// <summary>
    ///     Enables or disables the buttons that need a lobby connection: Create, Continue, Password and Homepage. About,
    ///     Config and Exit stay enabled. Story stays disabled until StoryView exists.
    /// </summary>
    public void SetButtonsEnabled(bool enabled)
    {
        foreach (var button in ConnectionButtons)
            button.SetEnabled(enabled);
    }

    /// <summary>
    ///     Sets the server on the status line: the server name when the lobby knows it, else the host, after a padlock
    ///     while the connection is encrypted. A call with the same text changes nothing, so the screen can call it on
    ///     every update.
    /// </summary>
    public void SetConnection(string host, bool secure, string? serverName = null)
    {
        var shown = string.IsNullOrWhiteSpace(serverName) ? host : serverName;
        var text = secure ? $"{Glyphs.PADLOCK}{shown}" : shown;

        if (text == ServerLabel.Text)
            return;

        ServerLabel.Text = text;
        StatusLayoutGeneration = -1;
    }

    public override void Update(GameTime gameTime)
    {
        //the theme title font is applied here, in the update, not in the middle of a draw
        ThemeArt.EnsureThemeFonts();

        base.Update(gameTime);

        //show the update notice when the background release check finds a newer release
        if (!UpdateNotice.Visible && UpdateChecker.Available is { } update)
        {
            UpdateNotice.Text = $"{update.Tag} available!";
            UpdateNotice.Visible = true;
            StatusLayoutGeneration = -1;
        }

        if (StatusLayoutGeneration != FontEngine.Instance.Generation)
            LayoutStatusLine();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        //the status colours come from the theme, which can change after construction
        VersionLabel.ForegroundColor = ThemeColor(ThemeRoles.Text, DialogPalette.StatusText);
        ServerLabel.ForegroundColor = ThemeColor(ThemeRoles.Text, DialogPalette.StatusText);

        base.Draw(spriteBatch);
    }

    //the full-window background. The menu covers the whole 640×480 layout, so its native rectangle is the whole back
    //buffer, and a cover fit crops the art to the window's real aspect ratio, also in a maximized window that is not 4:3
    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if (TryDrawThemePart(spriteBatch, ThemeRoles.MainMenuBackground, bounds))
            return;

        DrawRectClipped(spriteBatch, bounds, DialogPalette.Backdrop);
    }

    private void ShowAbout()
    {
        if (AboutView is null)
        {
            AboutView = new MarkdownView(ChaosGame.Device, new Rectangle(0, 0, Width, Height)) { Name = "aboutview", ZIndex = 1 };
            AddChild(AboutView);
        }

        AboutView.Show(ReadText("about.md"));
    }

    private static TextButton AddMenuButton(UIPanel column, string name, string text, int index)
    {
        var button = new TextButton(text, BUTTON_WIDTH, BUTTON_HEIGHT)
        {
            Name = name,
            X = 0,
            Y = index * (BUTTON_HEIGHT + BUTTON_GAP)
        };

        column.AddChild(button);

        return button;
    }

    //version, server and update notice sit in one row, each after the measured width of the one before it. The widths
    //depend on the window scale and the face, so this runs again when FontEngine.Generation changes.
    private void LayoutStatusLine()
    {
        var x = 0;

        foreach (var label in (UILabel[])[VersionLabel, ServerLabel, UpdateNotice])
        {
            if (!label.Visible || string.IsNullOrEmpty(label.Text))
                continue;

            var width = label is LinkLabel link ? link.MeasureTextWidth() + 2 : TextRenderer.MeasureWidth(label.Text) + 2;

            label.X = x;
            label.Width = width;
            x += width + STATUS_GAP;
        }

        StatusLayoutGeneration = FontEngine.Instance.Generation;
    }

    /// <summary>Reads a text file that ships in the client under <c>Resources/MainMenu</c>. Empty, and logged, when it is absent.</summary>
    internal static string ReadText(string fileName)
    {
        var name = $"Brigid.MainMenu.{fileName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

        if (stream is null)
        {
            NoticeDebugLog.Write($"[menu] embedded text '{name}' is missing");

            return string.Empty;
        }

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
