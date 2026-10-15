using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Backend.Beta.Caching.Core;
using WoW.Two.Sdk.Backend.Beta.Caching.Hybrid;
using Xunit;

namespace WoW.Two.Sdk.Backend.Beta.Foundation.Tests.Caching;

/// <summary>A factory can hand back a value without caching it; kept values and the plain overload cache as before.</summary>
public sealed class TransientCacheResultTests
{
    [Fact]
    public async Task TransientValue_ShouldBeReturnedButNotCached()
    {
        var cache = Build();
        var calls = 0;

        var first = await cache.GetOrCreateUnlessTransientAsync("price", _ =>
        {
            calls++;
            return ValueTask.FromResult(CacheResult.Transient("fallback"));
        });
        var second = await cache.GetOrCreateUnlessTransientAsync("price", _ =>
        {
            calls++;
            return ValueTask.FromResult(CacheResult.Keep("live"));
        });
        var third = await cache.GetOrCreateAsync<string>("price", _ => throw new InvalidOperationException("the kept value is cached"));

        first.Should().Be("fallback");
        second.Should().Be("live");
        third.Should().Be("live");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task TransientValue_ShouldReachEveryCoalescedCaller()
    {
        var cache = Build();
        var release = new TaskCompletionSource();
        var calls = 0;

        async ValueTask<CacheResult<int>> SlowFactory(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            await release.Task.WaitAsync(ct);
            return CacheResult.Transient(-1);
        }

        var callers = Enumerable.Range(0, 4).Select(_ => cache.GetOrCreateUnlessTransientAsync<int>("stock", SlowFactory).AsTask()).ToArray();
        release.SetResult();
        var values = await Task.WhenAll(callers);

        values.Should().OnlyContain(value => value == -1);
        calls.Should().Be(1);
        (await cache.GetOrCreateUnlessTransientAsync("stock", _ => ValueTask.FromResult(CacheResult.Keep(5)))).Should().Be(5);
    }

    [Fact]
    public async Task PlainGetOrCreate_ShouldStillCacheItsValue()
    {
        var cache = Build();

        await cache.GetOrCreateAsync("plain", _ => ValueTask.FromResult(42));

        (await cache.GetOrCreateAsync<int>("plain", _ => throw new InvalidOperationException("cached"))).Should().Be(42);
    }

    private static ICacheRepository Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCaching();
        return services.BuildServiceProvider().GetRequiredService<ICacheRepository>();
    }
}
