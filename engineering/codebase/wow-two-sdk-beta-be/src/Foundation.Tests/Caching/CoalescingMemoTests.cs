using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Caching.Core;
using WoW.Two.Sdk.Beta.Be.Caching.Memo;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Tests.Caching;

/// <summary>
/// One read serves every caller for the holding time, a fresh call replaces it, concurrent callers — a fresh one
/// included — share one read in flight, and a transient value is never held.
/// </summary>
public sealed class CoalescingMemoTests : IDisposable
{
    private static readonly TimeSpan Minute = TimeSpan.FromSeconds(60);

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-27T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly CoalescingMemo<int> _memo;
    private int _reads;

    public CoalescingMemoTests() => _memo = new CoalescingMemo<int>(_time);

    public void Dispose() => _memo.Dispose();

    private TaskCompletionSource? Hold { get; set; }

    private bool Fails { get; set; }

    private async ValueTask<CacheResult<int>> ReadAsync(CancellationToken ct)
    {
        var read = ++_reads;
        if (Hold is not null)
            await Hold.Task.WaitAsync(ct);
        return Fails ? CacheResult.Transient(-read) : CacheResult.Keep(read);
    }

    [Fact]
    public async Task GetAsync_ShouldShareOneRead_WhileItIsYoungerThanTheHoldingTime()
    {
        (await _memo.GetAsync(ReadAsync, Minute, fresh: false, CancellationToken.None)).Should().Be(1);
        _time.Advance(TimeSpan.FromSeconds(59));
        (await _memo.GetAsync(ReadAsync, Minute, fresh: false, CancellationToken.None)).Should().Be(1);
        _memo.HeldAt.Should().Be(_time.GetUtcNow() - TimeSpan.FromSeconds(59));

        _time.Advance(TimeSpan.FromSeconds(1));
        (await _memo.GetAsync(ReadAsync, Minute, fresh: false, CancellationToken.None)).Should().Be(2);
        _reads.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_ShouldReadNow_WhenFresh_AndServeThatReadToEveryone()
    {
        await _memo.GetAsync(ReadAsync, Minute, fresh: false, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(1));

        (await _memo.GetAsync(ReadAsync, Minute, fresh: true, CancellationToken.None)).Should().Be(2);
        (await _memo.GetAsync(ReadAsync, Minute, fresh: false, CancellationToken.None)).Should().Be(2);
        _reads.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_ShouldReadEveryTime_WhenHoldingIsOff()
    {
        await _memo.GetAsync(ReadAsync, TimeSpan.Zero, fresh: false, CancellationToken.None);
        await _memo.GetAsync(ReadAsync, TimeSpan.Zero, fresh: false, CancellationToken.None);

        _reads.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_ShouldNeverHoldATransientValue()
    {
        Fails = true;
        (await _memo.GetAsync(ReadAsync, Minute, fresh: false, CancellationToken.None)).Should().Be(-1);
        _memo.HeldAt.Should().BeNull();
        Fails = false;

        (await _memo.GetAsync(ReadAsync, Minute, fresh: false, CancellationToken.None)).Should().Be(2);
        _reads.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_ShouldShareOneRead_WhenCallersAndAFreshCallAskAtOnce()
    {
        Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = _memo.GetAsync(ReadAsync, Minute, fresh: false, CancellationToken.None).AsTask();
        var second = _memo.GetAsync(ReadAsync, Minute, fresh: false, CancellationToken.None).AsTask();
        var fresh = _memo.GetAsync(ReadAsync, Minute, fresh: true, CancellationToken.None).AsTask();

        Hold.SetResult();
        var values = await Task.WhenAll(first, second, fresh);

        _reads.Should().Be(1);
        values.Should().Equal(1, 1, 1);
    }

    [Fact]
    public async Task GetAsync_ShouldReadAgain_WhenAFreshCallAsksAfterTheLastReadFinished()
    {
        await _memo.GetAsync(ReadAsync, Minute, fresh: false, CancellationToken.None);
        _time.Advance(TimeSpan.FromMilliseconds(1));

        (await _memo.GetAsync(ReadAsync, Minute, fresh: true, CancellationToken.None)).Should().Be(2);
    }

    [Fact]
    public async Task Forget_ShouldDropTheHeldValue()
    {
        await _memo.GetAsync(ReadAsync, Minute, fresh: false, CancellationToken.None);

        _memo.Forget();

        _memo.HeldAt.Should().BeNull();
        (await _memo.GetAsync(ReadAsync, Minute, fresh: false, CancellationToken.None)).Should().Be(2);
    }
}
