#region
using Brigid.Rendering;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The texture cache behind <see cref="ThemeArt" />. The background draws at a pass scale of 1 and the UI at the
///     window scale in the same frame, so both sizes must stay cached. An evicted texture must not be disposed in the
///     frame that evicted it, because the deferred sprite batch can still draw it.
/// </summary>
public class ThemeTextureCacheTests
{
    private long Now;
    private readonly List<string> Created = [];
    private readonly List<string> Disposed = [];

    private ThemeTextureCache<string, string> NewCache(int capacity) => new(capacity, () => Now, Disposed.Add);

    private string? Get(ThemeTextureCache<string, string> cache, string key)
        => cache.GetOrCreate(
            key,
            () =>
            {
                Created.Add(key);

                return key;
            });

    [Fact]
    public void KeepsTwoScalesOfTheSamePart()
    {
        var cache = NewCache(16);

        //one frame draws the background at scale 1 and the buttons at scale 2, again and again
        for (var frame = 0; frame < 5; frame++)
        {
            Get(cache, "background@1");
            Get(cache, "button@2");
            Now += 16;
        }

        Assert.Equal(["background@1", "button@2"], Created);
        Assert.Empty(Disposed);
    }

    [Fact]
    public void CachesAMissingPart()
    {
        var cache = NewCache(16);
        var calls = 0;

        for (var i = 0; i < 3; i++)
            Assert.Null(
                cache.GetOrCreate(
                    "missing",
                    () =>
                    {
                        calls++;

                        return null;
                    }));

        Assert.Equal(1, calls);
    }

    [Fact]
    public void EvictsTheLeastRecentlyUsedWhenFull()
    {
        var cache = NewCache(4);

        foreach (var key in new[] { "a", "b", "c", "d" })
        {
            Get(cache, key);
            Now++;
        }

        //use a again, so b is now the oldest
        Get(cache, "a");
        Now++;
        Get(cache, "e");

        //above capacity, the cache drops to three quarters of it: b and c go
        Assert.Equal(3, cache.Count);

        Get(cache, "a");
        Get(cache, "d");
        Get(cache, "e");
        Assert.Equal(["a", "b", "c", "d", "e"], Created);
    }

    [Fact]
    public void DisposesAnEvictedValueOnlyAfterTheDelay()
    {
        var cache = NewCache(1);

        Get(cache, "a");
        Get(cache, "b");

        //a is out of the cache, but a draw queued in this frame can still use it
        Assert.Empty(Disposed);

        Now += ThemeTextureCache<string, string>.DisposeDelayMs - 1;
        Get(cache, "b");
        Assert.Empty(Disposed);

        Now += 1;
        Get(cache, "b");
        Assert.Equal(["a"], Disposed);
    }

    [Fact]
    public void ClearDefersTheDisposeToo()
    {
        var cache = NewCache(8);

        Get(cache, "a");
        Get(cache, "b");
        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Empty(Disposed);

        Now += ThemeTextureCache<string, string>.DisposeDelayMs;
        Get(cache, "c");
        Assert.Equal(["a", "b"], Disposed.Order());
    }
}
