using Microsoft.Extensions.Logging;

namespace WoW.Two.Sdk.Beta.Be.Jobs.Core.PersonOperations;

/// <summary>Provides the in-process tracker: the running leases and the signals still remembered, both under one lock.</summary>
/// <param name="options">How long a signal is remembered.</param>
/// <param name="time">The clock signals age by.</param>
/// <param name="logger">Records a token callback that threw.</param>
internal sealed class PersonOperationTracker(PersonOperationOptions options, TimeProvider time, ILogger<PersonOperationTracker> logger) : IPersonOperationTracker
{
    private readonly Lock _lock = new();

    /// <summary>Keeps the leases not yet disposed that a signal could still reach.</summary>
    private readonly HashSet<PersonOperationLease> _running = [];

    /// <summary>Keeps when each signal was last heard; a repeat refreshes its entry, and each signal sweeps the expired ones.</summary>
    private readonly Dictionary<PersonOperationSignal, DateTimeOffset> _remembered = [];

    /// <inheritdoc />
    public PersonOperationLease Begin(PersonOperationDeclaration declaration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaration.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaration.Subject);

        var lease = new PersonOperationLease(declaration, Release, cancellationToken);
        string? reason;

        // One lock with Apply: a signal is either remembered by now, or it finds this lease running.
        lock (_lock)
        {
            reason = RememberedReason(declaration);
            if (reason is null)
                _running.Add(lease);
        }

        if (reason is not null)
            lease.TryCancel(reason);
        return lease;
    }

    /// <inheritdoc />
    public int SessionEnded(string sessionId, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        return Apply(new PersonOperationSignal { Reason = PersonOperationCancelReasonConstants.SessionEnded, Subject = subject, SessionId = sessionId });
    }

    /// <inheritdoc />
    public int AccessLost(string subject, string organizationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);
        return Apply(new PersonOperationSignal { Reason = PersonOperationCancelReasonConstants.AccessLost, Subject = subject, OrganizationId = organizationId });
    }

    /// <summary>Remembers the signal, then cancels what it reaches outside the lock, since a token callback runs arbitrary work.</summary>
    private int Apply(PersonOperationSignal signal)
    {
        List<PersonOperationLease> reached;
        lock (_lock)
        {
            var now = time.GetUtcNow();
            foreach (var (remembered, heardAt) in _remembered)
            {
                if (IsExpired(heardAt, now))
                    _remembered.Remove(remembered);
            }

            _remembered[signal] = now;
            reached = [.. _running.Where(lease => signal.Cancels(lease.Declaration))];
        }

        var cancelled = 0;
        foreach (var lease in reached)
        {
            if (TryCancel(lease, signal.Reason))
                cancelled++;
        }

        return cancelled;
    }

    /// <summary>Cancels one lease; a token callback that throws never keeps the signal from reaching the others.</summary>
    private bool TryCancel(PersonOperationLease lease, string reason)
    {
        try
        {
            return lease.TryCancel(reason);
        }
        catch (AggregateException exception)
        {
            logger.PersonOperationCallbackFailed(exception, lease.Declaration.Name, reason);
            return true;
        }
    }

    private string? RememberedReason(PersonOperationDeclaration declaration)
    {
        if (declaration.AcceptedAt is not { } acceptedAt)
            return null;

        var now = time.GetUtcNow();
        foreach (var (signal, heardAt) in _remembered)
        {
            if (heardAt > acceptedAt && !IsExpired(heardAt, now) && signal.Cancels(declaration))
                return signal.Reason;
        }

        return null;
    }

    private bool IsExpired(DateTimeOffset heardAt, DateTimeOffset now) => now - heardAt >= options.SignalMemory;

    private void Release(PersonOperationLease lease)
    {
        lock (_lock)
            _running.Remove(lease);
    }
}
