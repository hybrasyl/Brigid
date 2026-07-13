#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Data;
using Brigid.Definitions;
using Brigid.Extensions;
using Brigid.Rendering;
using Brigid.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Options;

/// <summary>
///     Center-screen menu opened with Escape. Contains sound/music sliders plus Friends / Macros / Settings /
///     Exit Game / Close buttons laid out as a 2×2 action grid. Background is a DlgBack2.spf tile composited
///     with the dlgframe.epf 8-piece border; action buttons are rendered as colored rects with text overlays.
///     Clicking Friends / Macros / Settings closes the menu so the sub-panel can take over.
/// </summary>
public sealed class PauseMenuControl : UIPanel
{
    private const int PANEL_WIDTH = 260;
    private const int PANEL_HEIGHT = 226;

    //vertical positions inside the panel
    private const int TITLE_Y = 18;
    private const int SOUND_LABEL_Y = 48;
    private const int SOUND_TRACK_Y = 52;
    private const int MUSIC_LABEL_Y = 72;
    private const int MUSIC_TRACK_Y = 76;
    private const int AMBIENCE_LABEL_Y = 96;
    private const int AMBIENCE_TRACK_Y = 100;
    private const int BUTTON_ROW_1_Y = 124;
    private const int BUTTON_ROW_2_Y = 150;
    private const int CLOSE_Y = 186;

    //button dimensions
    private const int BUTTON_WIDTH = 100;
    private const int BUTTON_HEIGHT = 22;
    private const int BUTTON_GAP = 12;
    private const int CLOSE_WIDTH = 84;

    //volume slider geometry
    private const int TRACK_X = 88;
    private const int TRACK_WIDTH = 138;
    private const int TRACK_HEIGHT = 12;

    private static readonly Color ButtonNormalBg = new(36, 36, 40, 220);
    private static readonly Color ButtonHoverBg = new(70, 70, 80, 235);
    private static readonly Color ButtonBorder = new(170, 170, 180, 255);
    private static readonly Color TitleColor = new(230, 220, 180, 255);

    private readonly SliderControl MusicSlider;
    private readonly SliderControl SoundSlider;
    private readonly SliderControl AmbienceSlider;

    //viewport the panel centers within; defaults to full screen until SetViewportBounds runs
    private Rectangle Viewport = new(0, 0, 640, 480);

    public UIButton CloseButton { get; }
    public UIButton ExitButton { get; }
    public UIButton FriendsButton { get; }
    public UIButton MacroButton { get; }
    public UIButton SettingsButton { get; }

    public PauseMenuControl()
    {
        Name = "PauseMenu";
        Visible = false;
        UsesControlStack = true;
        Width = PANEL_WIDTH;
        Height = PANEL_HEIGHT;
        this.CenterOnScreen();

        //composite tiled background with 8-piece border (same pattern as OkPopupMessageControl)
        using var bgTile = DataContext.UserControls.GetSpfImage("DlgBack2.spf");

        if (bgTile is not null)
        {
            using var composite = DialogFrame.Composite(bgTile, PANEL_WIDTH, PANEL_HEIGHT);

            if (composite is not null)
                Background = TextureConverter.ToTexture2D(composite);
        }

        //title
        AddChild(
            new UILabel
            {
                X = 0,
                Y = TITLE_Y,
                Width = PANEL_WIDTH,
                Height = 16,
                Text = "Menu",
                ForegroundColor = TitleColor,
                HorizontalAlignment = HorizontalAlignment.Center,
                IsHitTestVisible = false
            });

        //volume sliders — thumb comes from option04.epf frame 0 (same as the legacy _noptdlg prefab)
        var thumbTexture = UiRenderer.Instance?.GetEpfTexture("option04.epf", 0);

        AddChild(
            new UILabel
            {
                X = 24,
                Y = SOUND_LABEL_Y,
                Width = 56,
                Height = 16,
                Text = "Sound",
                ForegroundColor = Color.White,
                IsHitTestVisible = false
            });

        SoundSlider = new SliderControl(new Rectangle(TRACK_X, SOUND_TRACK_Y, TRACK_WIDTH, TRACK_HEIGHT), thumbTexture);
        SoundSlider.ValueChanged += v => OnSoundVolumeChanged?.Invoke(v);
        AddChild(SoundSlider);

        AddChild(
            new UILabel
            {
                X = 24,
                Y = MUSIC_LABEL_Y,
                Width = 56,
                Height = 16,
                Text = "Music",
                ForegroundColor = Color.White,
                IsHitTestVisible = false
            });

        MusicSlider = new SliderControl(new Rectangle(TRACK_X, MUSIC_TRACK_Y, TRACK_WIDTH, TRACK_HEIGHT), thumbTexture);
        MusicSlider.ValueChanged += v => OnMusicVolumeChanged?.Invoke(v);
        AddChild(MusicSlider);

        AddChild(
            new UILabel
            {
                X = 24,
                Y = AMBIENCE_LABEL_Y,
                Width = 56,
                Height = 16,
                Text = "Ambience",
                ForegroundColor = Color.White,
                IsHitTestVisible = false
            });

        AmbienceSlider = new SliderControl(new Rectangle(TRACK_X, AMBIENCE_TRACK_Y, TRACK_WIDTH, TRACK_HEIGHT), thumbTexture);
        AmbienceSlider.ValueChanged += v => OnAmbienceVolumeChanged?.Invoke(v);
        AddChild(AmbienceSlider);

        //2×2 action grid — two buttons per row, centered horizontally
        var gridTotalWidth = BUTTON_WIDTH * 2 + BUTTON_GAP;
        var leftX = (PANEL_WIDTH - gridTotalWidth) / 2;
        var rightX = leftX + BUTTON_WIDTH + BUTTON_GAP;

        FriendsButton = CreateTextButton("Friends", leftX, BUTTON_ROW_1_Y, BUTTON_WIDTH);
        FriendsButton.Clicked += () =>
        {
            Hide();
            OnFriends?.Invoke();
        };

        MacroButton = CreateTextButton("Macros", rightX, BUTTON_ROW_1_Y, BUTTON_WIDTH);
        MacroButton.Clicked += () =>
        {
            Hide();
            OnMacro?.Invoke();
        };

        SettingsButton = CreateTextButton("Settings", leftX, BUTTON_ROW_2_Y, BUTTON_WIDTH);
        SettingsButton.Clicked += () =>
        {
            Hide();
            OnSettings?.Invoke();
        };

        ExitButton = CreateTextButton("Exit Game", rightX, BUTTON_ROW_2_Y, BUTTON_WIDTH);
        ExitButton.Clicked += () =>
        {
            Hide();
            OnExit?.Invoke();
        };

        //close button — centered below the grid
        CloseButton = CreateTextButton("Close", (PANEL_WIDTH - CLOSE_WIDTH) / 2, CLOSE_Y, CLOSE_WIDTH);
        CloseButton.Clicked += Hide;
    }

    /// <summary>
    ///     Creates a colored-rect button with hover feedback and adds it (plus its text label) to the panel's
    ///     children. Label is hit-test-transparent so clicks pass to the button.
    /// </summary>
    private UIButton CreateTextButton(string text, int x, int y, int width)
    {
        var button = new UIButton
        {
            X = x,
            Y = y,
            Width = width,
            Height = BUTTON_HEIGHT,
            BackgroundColor = ButtonNormalBg,
            BorderColor = ButtonBorder
        };
        button.Hovered += _ => button.BackgroundColor = ButtonHoverBg;
        button.Unhovered += _ => button.BackgroundColor = ButtonNormalBg;
        AddChild(button);

        AddChild(
            new UILabel
            {
                X = x,
                Y = y + 4,
                Width = width,
                Height = BUTTON_HEIGHT - 4,
                Text = text,
                ForegroundColor = Color.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                IsHitTestVisible = false
            });

        return button;
    }

    /// <summary>
    ///     Sets the playable viewport rect (typically the HUD's MAP area). The panel re-centers within it so it
    ///     doesn't render under the HUD chrome.
    /// </summary>
    public void SetViewportBounds(Rectangle viewport)
    {
        Viewport = viewport;
        CenterInViewport();
    }

    private void CenterInViewport()
    {
        X = Viewport.X + (Viewport.Width - Width) / 2;
        Y = Viewport.Y + (Viewport.Height - Height) / 2;
    }

    public int GetMusicVolume() => MusicSlider.Value;

    public int GetSoundVolume() => SoundSlider.Value;

    public int GetAmbienceVolume() => AmbienceSlider.Value;

    public void SetMusicVolume(int volume) => MusicSlider.SetValue(volume);

    public void SetSoundVolume(int volume) => SoundSlider.SetValue(volume);

    public void SetAmbienceVolume(int volume) => AmbienceSlider.SetValue(volume);

    public void Show()
    {
        if (Visible)
            return;

        CenterInViewport();
        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
    }

    public void Hide()
    {
        if (!Visible)
            return;

        InputDispatcher.Instance?.RemoveControl(this);
        Visible = false;
        OnClose?.Invoke();
    }

    public event AmbienceVolumeChangedHandler? OnAmbienceVolumeChanged;
    public event CloseHandler? OnClose;
    public event ExitHandler? OnExit;
    public event FriendsHandler? OnFriends;
    public event MacroHandler? OnMacro;
    public event MusicVolumeChangedHandler? OnMusicVolumeChanged;
    public event SettingsHandler? OnSettings;
    public event SoundVolumeChangedHandler? OnSoundVolumeChanged;

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            Hide();
            e.Handled = true;
        } else if (e.Key == Keys.X)
        {
            Hide();
            OnExit?.Invoke();
            e.Handled = true;
        }
    }
}
