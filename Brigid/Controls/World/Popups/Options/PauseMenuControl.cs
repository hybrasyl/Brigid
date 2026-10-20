#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Options;

/// <summary>
///     Center-screen menu opened with Escape or Q (BRIG-55). Contains Sound / Music / Ambience sliders and the Settings and
///     Exit Game buttons; Close is the bottom bar's. Clicking Settings closes the menu and opens the Options modal
///     (Friends/Macros now live as tabs there). Escape closes the menu, and X closes it and exits.
///     <para>
///         A primitive <see cref="CenteredModalPanel" /> that takes the theme: the frame, the buttons, the sliders and the
///         label colours come from the theme, with the flat <see cref="DialogPalette" /> look as the fallback. Every child
///         has a lowercase name.
///     </para>
/// </summary>
public sealed class PauseMenuControl : CenteredModalPanel
{
    private const int PANEL_W = 260;
    private const int PANEL_H = 170;

    private const int ROW_STRIDE = 24;
    private const int ROW_H = 16;
    private const int LABEL_W = 64;
    private const int SLIDER_W = 146;
    private const int SLIDER_H = 14;

    private const int BUTTON_W = 100;
    private const int BUTTON_H = 22;
    private const int BUTTON_GAP = 12;
    private const int SECTION_GAP = 6;

    private readonly SliderControl SoundSlider;
    private readonly SliderControl MusicSlider;
    private readonly SliderControl AmbienceSlider;

    public PauseMenuControl()
        : base("Menu", PANEL_W, PANEL_H, useTheme: true)
    {
        Name = "pausemenu";

        var content = ContentBounds;
        var rowsX = content.X + (content.Width - LABEL_W - SLIDER_W) / 2;
        var y = content.Y + SECTION_GAP;

        //each control is created here with a literal name, so that the primitives report can read it
        SoundSlider = new SliderControl(SLIDER_W, SLIDER_H) { Name = "sound" };
        AddSliderRow(new UILabel { Name = "sound_label", Text = "Sound" }, SoundSlider, rowsX, ref y);
        SoundSlider.ValueChanged += v => OnSoundVolumeChanged?.Invoke(v);

        MusicSlider = new SliderControl(SLIDER_W, SLIDER_H) { Name = "music" };
        AddSliderRow(new UILabel { Name = "music_label", Text = "Music" }, MusicSlider, rowsX, ref y);
        MusicSlider.ValueChanged += v => OnMusicVolumeChanged?.Invoke(v);

        AmbienceSlider = new SliderControl(SLIDER_W, SLIDER_H) { Name = "ambience" };
        AddSliderRow(new UILabel { Name = "ambience_label", Text = "Ambience" }, AmbienceSlider, rowsX, ref y);
        AmbienceSlider.ValueChanged += v => OnAmbienceVolumeChanged?.Invoke(v);

        //Settings and Exit Game side by side, centred below the sliders
        var buttonsX = content.X + (content.Width - (2 * BUTTON_W + BUTTON_GAP)) / 2;
        var buttonsY = y + SECTION_GAP;

        var settings = new TextButton("Settings", BUTTON_W, BUTTON_H) { Name = "settings", X = buttonsX, Y = buttonsY };
        settings.Clicked += () =>
        {
            Hide();
            OnSettings?.Invoke();
        };
        AddChild(settings);

        var exit = new TextButton("Exit Game", BUTTON_W, BUTTON_H)
        {
            Name = "exit",
            X = buttonsX + BUTTON_W + BUTTON_GAP,
            Y = buttonsY
        };
        exit.Clicked += Exit;
        AddChild(exit);
    }

    //places a row: the label at the left and the slider beside it
    private void AddSliderRow(UILabel label, SliderControl slider, int x, ref int y)
    {
        label.X = x;
        label.Y = y + (SLIDER_H - ROW_H) / 2;
        label.Width = LABEL_W;
        label.Height = ROW_H;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.IsHitTestVisible = false;
        label.SetForegroundRole(ThemeRoles.Text);
        AddChild(label);

        slider.X = x + LABEL_W;
        slider.Y = y;
        AddChild(slider);
        y += ROW_STRIDE;
    }

    private void Exit()
    {
        Hide();
        OnExit?.Invoke();
    }

    public int GetMusicVolume() => MusicSlider.Value;

    public int GetSoundVolume() => SoundSlider.Value;

    public int GetAmbienceVolume() => AmbienceSlider.Value;

    public void SetMusicVolume(int volume) => MusicSlider.SetValue(volume);

    public void SetSoundVolume(int volume) => SoundSlider.SetValue(volume);

    public void SetAmbienceVolume(int volume) => AmbienceSlider.SetValue(volume);

    public event AmbienceVolumeChangedHandler? OnAmbienceVolumeChanged;
    public event ExitHandler? OnExit;
    public event MusicVolumeChangedHandler? OnMusicVolumeChanged;
    public event SettingsHandler? OnSettings;
    public event SoundVolumeChangedHandler? OnSoundVolumeChanged;

    //Escape closes the menu and X closes it and exits. Every other key goes on to the world screen, as before the
    //rebuild: Q closes the menu again, and a panel hotkey closes it and opens its panel. The base modal marks every key as
    //handled, so this override does not call it
    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case Keys.Escape:
                Hide();
                e.Handled = true;

                break;

            case Keys.X:
                Exit();
                e.Handled = true;

                break;
        }
    }
}
