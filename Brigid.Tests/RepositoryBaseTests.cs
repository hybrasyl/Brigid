using Brigid.Data.Abstractions;
using Xunit;

namespace Brigid.Tests;

// A lookup for an asset that is not in the archive returns null, so the caller's fallback runs. GetOrCreate refuses a null
// load, and neither method caches a null.
public class RepositoryBaseTests
{
    private sealed class Repository : RepositoryBase
    {
        public int Loads { get; private set; }

        public string Require(string key, string? value) => GetOrCreate(key, () => value!);

        public string? Get(string key, string? value)
            => GetOrCreateOrNull(
                key,
                () =>
                {
                    Loads++;

                    return value;
                });
    }

    [Fact]
    public void GetOrCreateOrNull_ReturnsNullForAnAbsentAssetEveryTime()
    {
        var repository = new Repository();

        Assert.Null(repository.Get("missing.spf", null));
        Assert.Null(repository.Get("missing.spf", null));
    }

    [Fact]
    public void GetOrCreateOrNull_DoesNotCacheAnAbsentAsset()
    {
        var repository = new Repository();

        Assert.Null(repository.Get("late.spf", null));
        Assert.Equal("found", repository.Get("late.spf", "found"));
        Assert.Equal(2, repository.Loads);
    }

    [Fact]
    public void GetOrCreateOrNull_CachesAPresentAsset()
    {
        var repository = new Repository();

        Assert.Equal("a", repository.Get("frame.spf", "a"));
        Assert.Equal("a", repository.Get("frame.spf", "b"));
        Assert.Equal(1, repository.Loads);
    }

    [Fact]
    public void GetOrCreate_ThrowsOnANullLoadEveryTimeAndCachesNothing()
    {
        var repository = new Repository();

        Assert.Throws<NullReferenceException>(() => repository.Require("missing.spf", null));
        Assert.Throws<NullReferenceException>(() => repository.Require("missing.spf", null));
        Assert.Equal("found", repository.Require("missing.spf", "found"));
    }
}
