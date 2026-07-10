using Brigid.Systems;
using Xunit;

namespace Brigid.Tests;

public sealed class UpdateCheckerVersionTests
{
    [Theory]
    //newer remote core
    [InlineData("v0.2.0", "0.1.0", true)]
    [InlineData("v1.0.0", "0.9.9", true)]
    //numeric (not lexicographic) comparison
    [InlineData("v0.10.0", "0.9.0", true)]
    //same version
    [InlineData("v0.1.0", "0.1.0", false)]
    //older remote
    [InlineData("v0.1.0", "0.2.0", false)]
    //stable release supersedes a local prerelease of the same core
    [InlineData("v1.0.0", "1.0.0-alpha1", true)]
    //local stable is not superseded by a prerelease of the same core
    [InlineData("v1.0.0-alpha1", "1.0.0", false)]
    //prerelease-to-prerelease of the same core: deliberately treated as equal
    [InlineData("v1.0.0-alpha2", "1.0.0-alpha1", false)]
    //newer core still wins regardless of prerelease suffixes
    [InlineData("v1.0.1-alpha1", "1.0.0", true)]
    //uppercase tag prefix
    [InlineData("V0.2.0", "0.1.0", true)]
    public void IsNewer_ComparesVersions(string remoteTag, string localVersion, bool expected)
        => Assert.Equal(expected, UpdateChecker.IsNewer(remoteTag, localVersion));

    [Theory]
    //unparseable falls back to inequality
    [InlineData("vnightly", "0.1.0", true)]
    [InlineData("vnightly", "nightly", false)]
    public void IsNewer_UnparseableFallsBackToInequality(string remoteTag, string localVersion, bool expected)
        => Assert.Equal(expected, UpdateChecker.IsNewer(remoteTag, localVersion));
}
