using System.Data.Common;
using System.Diagnostics;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Takes and releases a set's advisory locks with a bounded wait, so a held lock ends in a busy outcome instead of a stall.</summary>
/// <remarks>
///   - polls <see cref="IMigrationDialect.TryAcquireLockAsync"/> for each key, the reserved one first, until <see cref="MigrationOptions.AdvisoryLockWait"/> passes
///   - on a timeout it releases the keys it took, so a busy run holds nothing
///   - waits in wall-clock time: a held lock is a real session, whatever clock the host injects
/// </remarks>
/// <param name="dialect">The set's dialect.</param>
/// <param name="options">The set's options: lock id, legacy key and wait bound.</param>
internal sealed class MigrationSetLock(IMigrationDialect dialect, MigrationOptions options)
{
    private static readonly TimeSpan FirstPoll = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan LongestPoll = TimeSpan.FromSeconds(1);

    /// <summary>Gets the keys the set's migrator holds.</summary>
    public IReadOnlyList<MigrationLockKey> Keys => MigrationLockKey.For(options);

    /// <summary>Takes every key, waiting at most the set's bound.</summary>
    /// <param name="connection">The open connection that holds the locks for its session.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>Whether every key is held, and how long the wait took.</returns>
    public async Task<(bool Acquired, long WaitMs)> AcquireAsync(DbConnection connection, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var held = new List<MigrationLockKey>();
        var poll = FirstPoll;

        foreach (var key in Keys)
        {
            while (!await dialect.TryAcquireLockAsync(connection, key, ct).ConfigureAwait(false))
            {
                var remaining = options.AdvisoryLockWait - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    await ReleaseAsync(connection, held, ct).ConfigureAwait(false);
                    return (false, stopwatch.ElapsedMilliseconds);
                }

                await Task.Delay(remaining < poll ? remaining : poll, ct).ConfigureAwait(false);
                poll = poll * 2 > LongestPoll ? LongestPoll : poll * 2;
            }

            held.Add(key);
        }

        return (true, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Releases every key, the reserved one last.</summary>
    /// <param name="connection">The connection that holds the locks.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public Task ReleaseAsync(DbConnection connection, CancellationToken ct) => ReleaseAsync(connection, Keys, ct);

    private async Task ReleaseAsync(DbConnection connection, IReadOnlyList<MigrationLockKey> keys, CancellationToken ct)
    {
        for (var index = keys.Count - 1; index >= 0; index--)
            await dialect.ReleaseLockAsync(connection, keys[index], ct).ConfigureAwait(false);
    }
}
