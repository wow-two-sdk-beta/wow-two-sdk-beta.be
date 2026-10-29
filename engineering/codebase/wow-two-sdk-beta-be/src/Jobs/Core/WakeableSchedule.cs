namespace WoW.Two.Sdk.Beta.Be.Jobs.Core;

/// <summary>
/// Decides whether a periodic job's pass does its costly read — a child process, a remote call — so an idle host does
/// none: at once after <see cref="Wake"/>, then when the last read named its next look while work is open, else once
/// per idle interval, a safety pass that catches work started elsewhere. Register one per job as a singleton; derive a
/// type per job when a host runs several.
/// </summary>
/// <remarks>
///   - the job runs on a short fixed delay and asks <see cref="TryBegin"/> first; a pass that is not due reads nothing
///   - the first pass after start is due, so a restarted host learns what is still in flight
///   - a wake during a read keeps the next pass due, since the read may have missed the work that woke it
///   - one process: the schedule lives in memory and spans no replicas
/// </remarks>
/// <example>
/// <code>
/// services.AddSingleton&lt;FollowSchedule&gt;();   // sealed class FollowSchedule(TimeProvider time) : WakeableSchedule(time);
/// services.AddPeriodicJob&lt;Follower&gt;("follow", (follower, ct) =&gt; follower.PassAsync(ct),
///     (options, _) =&gt; options.Interval = TimeSpan.FromSeconds(10));
///
/// // Follower.PassAsync
/// if (!schedule.TryBegin(out var ticket))
///     return;
/// try
/// {
///     var read = await runner.ReadAsync(ct);                       // costly: starts a process
///     schedule.Settle(ticket, read.Open, read.NextLookIn, idle: TimeSpan.FromMinutes(5));
/// }
/// catch
/// {
///     schedule.Missed(ticket, idle: TimeSpan.FromMinutes(5));
///     throw;
/// }
///
/// // where work is submitted
/// schedule.Wake();
/// </code>
/// </example>
/// <param name="time">The clock passes are timed with.</param>
public class WakeableSchedule(TimeProvider time)
{
    /// <summary>
    /// How much earlier than the named time a pass may read: passes are timed from the end of the one before, which the
    /// reader's own clock cannot see.
    /// </summary>
    public static readonly TimeSpan Slack = TimeSpan.FromSeconds(1);

    private readonly Lock _lock = new();
    private DateTimeOffset _dueAt = DateTimeOffset.MinValue;
    private bool _open = true;
    private long _wakes;

    /// <summary>Gets whether the last read found open work; <see langword="true"/> until a read settles, since nothing is known yet.</summary>
    public bool IsOpen
    {
        get
        {
            lock (_lock)
                return _open;
        }
    }

    /// <summary>Makes the next pass read, even while a read is in flight: work was just submitted.</summary>
    public void Wake()
    {
        lock (_lock)
        {
            _wakes++;
            _dueAt = DateTimeOffset.MinValue;
        }
    }

    /// <summary>Starts a pass's read when one is due.</summary>
    /// <param name="ticket">What <see cref="Settle"/> or <see cref="Missed"/> takes once the read returns.</param>
    /// <returns><see langword="true"/> when this pass reads.</returns>
    public bool TryBegin(out long ticket)
    {
        lock (_lock)
        {
            ticket = _wakes;
            return time.GetUtcNow() + Slack >= _dueAt;
        }
    }

    /// <summary>
    /// Records a read. While work is open the next pass is due when the read named, at most <paramref name="idle"/> away
    /// and at once when it named no time; otherwise after <paramref name="idle"/>. A wake since
    /// <paramref name="ticket"/> keeps the next pass due.
    /// </summary>
    /// <param name="ticket">The ticket <see cref="TryBegin"/> gave this pass.</param>
    /// <param name="open">Whether the read found open work.</param>
    /// <param name="next">When the next look is due, if the read said.</param>
    /// <param name="idle">The safety pass's interval.</param>
    public void Settle(long ticket, bool open, TimeSpan? next, TimeSpan idle)
    {
        lock (_lock)
        {
            _open = open;
            if (_wakes != ticket)
                return;
            var wait = open ? (next is { } named ? Clamp(named, idle) : TimeSpan.Zero) : idle;
            _dueAt = time.GetUtcNow() + wait;
        }
    }

    /// <summary>
    /// Records a read that failed: the next pass reads again while work was open, else after <paramref name="idle"/>.
    /// A wake since <paramref name="ticket"/> keeps the next pass due.
    /// </summary>
    /// <param name="ticket">The ticket <see cref="TryBegin"/> gave this pass.</param>
    /// <param name="idle">The safety pass's interval.</param>
    public void Missed(long ticket, TimeSpan idle)
    {
        lock (_lock)
        {
            if (_wakes != ticket)
                return;
            _dueAt = _open ? DateTimeOffset.MinValue : time.GetUtcNow() + idle;
        }
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan max) =>
        value < TimeSpan.Zero ? TimeSpan.Zero : value > max ? max : value;
}
