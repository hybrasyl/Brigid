#region
using Brigid.Controls.Generic;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The notice shown when SDL2_mixer or a codec library it links cannot load. Only Linux gets install
///     commands, because only the Linux build depends on system libraries.
/// </summary>
public class AudioMissingNoticeTests
{
    [Fact]
    public void InstallCommands_CoverTheThreeDistroFamilies()
        => Assert.Equal(
            ["apt", "dnf", "pacman"],
            AudioMissingNoticeControl.InstallCommands.Select(c => c.Command.Split(' ')[1]));

    [Fact]
    public void InstallCommands_Debian_NamesTheMixerPackage()
        => Assert.Contains(
            AudioMissingNoticeControl.InstallCommands,
            c => c.Command == "sudo apt install libsdl2-mixer-2.0-0");

    [Fact]
    public void OtherGuidance_AsksForAReinstall()
        => Assert.Contains("Reinstall Brigid", AudioMissingNoticeControl.OTHER_GUIDANCE);
}
