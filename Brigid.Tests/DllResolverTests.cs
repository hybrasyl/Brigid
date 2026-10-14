using System.Runtime.InteropServices;
using Xunit;

namespace Brigid.Tests;

// On Linux Mint 22, .NET reports the host RID ubuntu24.04-x64, but MonoGame ships SDL2 under the portable
// runtimes/linux-x64/native. The resolver must probe the portable folder too, or startup fails before the window opens.
public class DllResolverTests
{
    private const string BASE = "/opt/brigid";
    private const string SDL = "libSDL2-2.0.so.0";

    [Theory]
    [InlineData("linux", Architecture.X64, "linux-x64")]
    [InlineData("linux", Architecture.Arm64, "linux-arm64")]
    [InlineData("linux", Architecture.Arm, "linux-arm")]
    [InlineData("osx", Architecture.Arm64, "osx-arm64")]
    [InlineData("win", Architecture.X86, "win-x86")]
    public void GetPortableRuntimeIdentifier_MapsTheProcessArchitecture(string os, Architecture architecture, string expected)
        => Assert.Equal(expected, DllResolver.GetPortableRuntimeIdentifier(os, architecture));

    [Fact]
    public void GetUnixProbePaths_ADistroRidAlsoProbesThePortableFolder()
        => Assert.Equal(
            [
                Path.Combine(BASE, SDL),
                Path.Combine(BASE, "runtimes", "ubuntu24.04-x64", "native", SDL),
                Path.Combine(BASE, "runtimes", "linux-x64", "native", SDL),
                Path.Combine(BASE, "runtimes", "linux", "native", SDL)
            ],
            DllResolver.GetUnixProbePaths(BASE, SDL, "ubuntu24.04-x64", "linux-x64"));

    [Fact]
    public void GetUnixProbePaths_APortableHostRidProbesItsFolderOnce()
        => Assert.Equal(
            [
                Path.Combine(BASE, SDL),
                Path.Combine(BASE, "runtimes", "linux-x64", "native", SDL),
                Path.Combine(BASE, "runtimes", "linux", "native", SDL)
            ],
            DllResolver.GetUnixProbePaths(BASE, SDL, "linux-x64", "linux-x64"));

    //Ubuntu 22.04 and Fedora: the bundled mixer cannot load its codecs, and the distribution package installs only the
    //versioned name. The bare names let the system loader find that file after every bundled path fails.
    [Fact]
    public void GetLoadCandidates_TriesEveryBundledPathThenEachBareName()
        => Assert.Equal(
            [
                Path.Combine(BASE, "libSDL2_mixer-2.0.so.0"),
                Path.Combine(BASE, "libSDL2_mixer.so"),
                "libSDL2_mixer-2.0.so.0",
                "libSDL2_mixer.so"
            ],
            DllResolver.GetLoadCandidates(
                ["libSDL2_mixer-2.0.so.0", "libSDL2_mixer.so"],
                name => [Path.Combine(BASE, name)]));
}
