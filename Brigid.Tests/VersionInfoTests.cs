using Brigid.Utilities;
using Xunit;

namespace Brigid.Tests;

//guards the tool-agnostic version parse so a future Nerdbank→MinVer swap can't silently garble the window title /
//login label — both tools emit a SemVer2 informational version; only the +buildmetadata differs.
public sealed class VersionInfoTests
{
    [Theory]
    [InlineData("1.2.3+a1b2c3d4", "1.2.3")]              // Nerdbank.GitVersioning: version + git-hash metadata
    [InlineData("1.2.3-alpha+a1b2c3d4", "1.2.3-alpha")]  // Nerdbank prerelease (feature branch)
    [InlineData("1.2.3", "1.2.3")]                        // MinVer release (tagged, no metadata)
    [InlineData("1.2.4-alpha.0.5+abc1234", "1.2.4-alpha.0.5")] // MinVer prerelease with height + sha
    [InlineData("0.2.0", "0.2.0")]                        // bare version, no metadata
    [InlineData("0.2.0.42+abc", "0.2.0.42")]              // Nerdbank height as a 4th component — kept verbatim
    public void Resolve_KeepsCoreVersion_DropsBuildMetadata(string informational, string expected)
        => Assert.Equal(expected, VersionInfo.Resolve(informational, new Version(9, 9, 9)));

    [Fact]
    public void Resolve_NullInformational_FallsBackToAssemblyVersion()
        => Assert.Equal("4.5.6", VersionInfo.Resolve(null, new Version(4, 5, 6)));

    [Fact]
    public void Resolve_EmptyInformational_FallsBackToAssemblyVersion()
        => Assert.Equal("4.5.6", VersionInfo.Resolve("", new Version(4, 5, 6)));

    [Fact]
    public void Resolve_MetadataOnly_FallsBackToAssemblyVersion()
        => Assert.Equal("4.5.6", VersionInfo.Resolve("+justmetadata", new Version(4, 5, 6)));

    [Fact]
    public void Resolve_NoInformationalNoAssemblyVersion_ReturnsZero()
        => Assert.Equal("0.0.0", VersionInfo.Resolve(null, null));
}
