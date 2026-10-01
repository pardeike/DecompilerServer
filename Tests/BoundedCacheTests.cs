using DecompilerServer.Services;

namespace Tests;

public class BoundedCacheTests
{
    [Fact]
    public void Eviction_RespectsBytesAndRecencyWhileOversizedValuesRemainUsable()
    {
        var cache = new BoundedCache<string, string>(2, 200, value => value.Length * 2);
        cache.GetOrAdd("a", _ => "a");
        cache.GetOrAdd("b", _ => "b");
        Assert.True(cache.TryGetValue("a", out _));
        cache.GetOrAdd("c", _ => "c");
        Assert.False(cache.TryGetValue("b", out _));
        Assert.True(cache.TryGetValue("a", out _));
        var large = new string('x', 100);
        Assert.Equal(large, cache.GetOrAdd("large", _ => large));
        Assert.False(cache.TryGetValue("large", out _));
        Assert.InRange(cache.Bytes, 0, 200);
        cache.Clear();
        Assert.Equal(0, cache.Bytes);
    }

    [Fact]
    public async Task ConcurrentMisses_ComputeOnceAndClearDoesNotRetainAnOldResult()
    {
        var cache = new BoundedCache<string, string>(2, 128, _ => 64);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        string Factory(string key)
        {
            Interlocked.Increment(ref calls);
            started.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return key;
        }
        var first = Task.Run(() => cache.GetOrAdd("same", Factory));
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        var second = Task.Run(() => cache.GetOrAdd("same", Factory));
        release.Set();
        await Task.WhenAll(first, second);
        Assert.Equal(1, calls);

        cache.Clear(); started.Reset(); release.Reset();
        var stale = Task.Run(() => cache.GetOrAdd("old", Factory));
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        cache.Clear();
        release.Set();
        Assert.Equal("old", await stale);
        Assert.False(cache.TryGetValue("old", out _));
    }
}
