#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     Tells the player the game is running without sound because SDL2_mixer, or a codec library it links, could
///     not be loaded (<see cref="Systems.SoundSystem.MixerLibraryMissing" />). Informational: Close, Escape and Enter
///     all dismiss it.
/// </summary>
/// <remarks>
///     Only the Linux build depends on system libraries, so only there is the fix something the player installs.
///     On Windows and macOS the mixer ships whole, and a failure means a damaged install.
/// </remarks>
public sealed class AudioMissingNoticeControl : CenteredModalPanel
{
    private const int PANEL_W = 396;
    //the headline gets a second line because the wrap count is per-face (see the sibling prompts); the command
    //rows never wrap, so a face that cannot fit them in the command column would clip rather than overlap
    private const int LINUX_PANEL_H = 172;
    private const int OTHER_PANEL_H = 110;
    private const int ROW_H = 14;
    private const int DISTRO_COL_W = 104;

    public const string HEADLINE = "Brigid could not load its audio libraries.";

    public const string LINUX_GUIDANCE = "Install your distribution's SDL2_mixer package, then restart Brigid:";

    public const string OTHER_GUIDANCE = "Reinstall Brigid to restore sound.";

    /// <summary>The install command for each distribution family, in display order.</summary>
    public static IReadOnlyList<(string Distro, string Command)> InstallCommands { get; } =
    [
        ("Debian, Ubuntu", "sudo apt install libsdl2-mixer-2.0-0"),
        ("Fedora", "sudo dnf install SDL2_mixer"),
        ("Arch", "sudo pacman -S sdl2_mixer")
    ];

    /// <param name="linux">
    ///     Whether to show the Linux install commands. The caller makes the OS check, as
    ///     <see cref="Systems.SoundSystem" /> knows nothing of the notice.
    /// </param>
    public AudioMissingNoticeControl(bool linux)
        : base("No Sound", PANEL_W, linux ? LINUX_PANEL_H : OTHER_PANEL_H)
    {
        var y = ContentTop + 4;

        AddRow(ref y, HEADLINE, 2, DialogPalette.RequirementUnmet);
        y += 4;

        if (!linux)
        {
            AddRow(ref y, OTHER_GUIDANCE, 1, DialogPalette.Title);

            return;
        }

        AddRow(ref y, LINUX_GUIDANCE, 2, DialogPalette.Title);
        y += 4;

        foreach (var (distro, command) in InstallCommands)
        {
            AddLabel(PADDING, y, DISTRO_COL_W, distro, DialogPalette.Title);
            AddLabel(PADDING + DISTRO_COL_W, y, Width - 2 * PADDING - DISTRO_COL_W, command, DialogPalette.SelectedText);
            y += ROW_H;
        }
    }

    //Enter dismisses too, as the OK popup this replaced did; Escape is handled by the base
    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Enter)
        {
            Hide();
            e.Handled = true;

            return;
        }

        base.OnKeyDown(e);
    }

    private void AddRow(ref int y, string text, int lines, Color color)
    {
        //wrap settings go before Text: the label lays its lines out when the text is assigned
        var label = AddLabel(PADDING, y, Width - 2 * PADDING, string.Empty, color);
        label.WordWrap = lines > 1;
        label.Height = ROW_H * lines;
        label.Text = text;
        y += ROW_H * lines;
    }

    private UILabel AddLabel(int x, int y, int width, string text, Color color)
    {
        var label = new UILabel
        {
            X = x,
            Y = y,
            Width = width,
            Height = ROW_H,
            ForegroundColor = color,
            IsHitTestVisible = false,
            ShrinkToFit = false,
            Text = text
        };

        AddChild(label);

        return label;
    }
}
