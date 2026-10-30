using WoW.Two.Sdk.Beta.Be.Caching.Core;

namespace WoW.Two.Sdk.Beta.Be.Caching.Memo;

/// <summary>
/// Holds the last value a read produced for every caller in the process, so they share one read per holding time. One
/// gate keeps concurrent misses to one read, and a forced refresh joins a read that finishes after it asked rather
/// than starting another — what HybridCache's refresh cannot do.
/// </summary>
/// <remarks>
///   - register one per read as a singleton; the reader that calls <see cref="GetAsync"/> may be scoped
///   - the read marks its value <see cref="CacheResult.Keep{T}"/> to hold it, or <see cref="CacheResult.Transient{T}"/> to
///     return it unheld, as a failure should be
///   - the holding time is passed per call, so a live setting changes it without a restart; zero or less holds nothing,
///     yet concurrent callers still share a read in flight
///   - one process: the value lives in memory and spans no replicas
/// </remarks>
/// <example>
/// <code>
/// services.AddSingleton&lt;CoalescingMemo&lt;Snapshot&gt;&gt;();
///
/// var snapshot = await memo.GetAsync(
///     async ct =&gt; await source.ReadAsync(ct) is { IsOk: true } read ? CacheResult.Keep(read) : CacheResult.Transient(read),
///     holdFor: TimeSpan.FromSeconds(settings.HoldSeconds),
///     fresh: request.Refresh,
///     ct);
/// </code>
/// </example>
/// <typeparam name="T">The value the read produces.</typeparam>
/// <param name="time">The clock the holding time is measured with; <see langword="null"/> uses <see cref="TimeProvider.System"/>.</param>
public sealed class CoalescingMemo<T>(TimeProvider? time = null) : IDisposable
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Entry? _last;

    /// <summary>Gets when the held value was read; <see langword="null"/> while nothing is held.</summary>
    public DateTimeOffset? HeldAt => _last?.At;

    /// <summary>
    /// Returns the held value while it is younger than <paramref name="holdFor"/>, else runs <paramref name="read"/> once
    /// for every caller waiting on it. A <paramref name="fresh"/> call skips the held value but takes any read that
    /// finished after it asked, so a refresh that arrives while a read is under way joins that read.
    /// </summary>
    /// <param name="read">Reads the value and says whether to hold it.</param>
    /// <param name="holdFor">How long a held value serves; zero or less serves none.</param>
    /// <param name="fresh">Whether to skip the held value.</param>
    /// <param name="ct">A cancellation token, for the wait and the read.</param>
    /// <returns>The held, shared or newly read value.</returns>
    public async ValueTask<T> GetAsync(
        Func<CancellationToken, ValueTask<CacheResult<T>>> read, TimeSpan holdFor, bool fresh, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(read);

        if (!fresh && Held(holdFor) is { } held)
            return held.Value;

        var asked = _time.GetUtcNow();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // A read that finished while this call waited serves it; a fresh call takes one that finished after it asked.
            if ((fresh ? ReadSince(asked) : Held(holdFor)) is { } shared)
                return shared.Value;

            var result = await read(ct).ConfigureAwait(false);
            if (!result.IsTransient)
                _last = new Entry(_time.GetUtcNow(), result.Value);
            return result.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Drops the held value, so the next call reads.</summary>
    public void Forget() => _last = null;

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    /// <summary>The held entry when it was read at or after <paramref name="asked"/>.</summary>
    private Entry? ReadSince(DateTimeOffset asked) => _last is { } last && last.At >= asked ? last : null;

    /// <summary>The held entry while it is younger than <paramref name="holdFor"/>.</summary>
    private Entry? Held(TimeSpan holdFor) =>
        holdFor > TimeSpan.Zero && _last is { } last && _time.GetUtcNow() - last.At < holdFor ? last : null;

    /// <summary>One held value and when it was read.</summary>
    private sealed record Entry(DateTimeOffset At, T Value);
}
