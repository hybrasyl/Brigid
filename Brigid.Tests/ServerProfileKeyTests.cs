using Brigid.Data;
using Xunit;

namespace Brigid.Tests;

// ServerProfileKey scopes per-character config by server (profiles/{serverKey}/{char}) so the same character name on
// different servers no longer shares one folder. Retail (*.kru.com) -> "kru"; Hybrasyl -> "hybrasyl"; else a
// filesystem-safe slug of the host.
public class ServerProfileKeyTests
{
    [Theory]
    [InlineData("da0.kru.com", true)]
    [InlineData("da1.kru.com", true)]
    [InlineData("login.KRU.COM", true)]     //case-insensitive
    [InlineData("kru.com", true)]
    [InlineData("notkru.com", false)]        //must not false-match the dotted suffix
    [InlineData("qa.hybrasyl.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsRetail_MatchesOnlyKruDotComHosts(string? host, bool expected)
        => Assert.Equal(expected, ServerProfileKey.IsRetail(host));

    [Theory]
    [InlineData("da0.kru.com", "Dark Ages", "kru")]
    [InlineData("qa.hybrasyl.com", "Hybrasyl", "hybrasyl")]
    [InlineData("somehost.net", "Hybrasyl Test", "hybrasyl")]   //hybrasyl detected via server name
    [InlineData("play.example.org", null, "play_example_org")]  //fallback: non-alnum (dots) -> '_'
    public void Resolve_MapsKnownAndFallsBack(string host, string? serverName, string expected)
        => Assert.Equal(expected, ServerProfileKey.Resolve(host, serverName));

    [Fact]
    public void Resolve_SanitizesArbitraryHostToFilesystemSafeSlug()
    {
        var key = ServerProfileKey.Resolve("Play.Some-Server:9999", null);

        Assert.Equal("play_some-server_9999", key);   //lowercased; hyphen kept; dot/colon -> '_'
        Assert.DoesNotContain(':', key);
    }

    [Fact]
    public void Resolve_EmptyInputs_FallsBackToUnknown()
        => Assert.Equal("unknown", ServerProfileKey.Resolve(null, null));

    [Fact]
    public void Resolve_RetailBeatsHybrasylAndSlug()
        => Assert.Equal("kru", ServerProfileKey.Resolve("da0.kru.com", "Hybrasyl"));
}
