# Person operations

> A long operation accepted for a person declares how it reacts when their session ends or they lose access. The work
> runs as the service with the person recorded, and never stores their token.

```csharp
builder.Services.AddPersonOperations();              // PersonOperationOptions.SignalMemory: 1 hour

using var lease = tracker.Begin(new PersonOperationDeclaration
{
    Name = "report-export",
    Subject = job.Subject,
    OrganizationId = job.OrganizationId,
}, cancellationToken);

try
{
    await exporter.RunAsync(job, lease.CancellationToken);
}
catch (OperationCanceledException) when (lease.CancelReason is not null)
{
    await jobs.MarkCancelledAsync(job.Id, lease.CancelReason, cancellationToken);   // session_ended | access_lost
}
```

## Reactions

| Declared | `SessionEnded(sessionId, subject)` | `AccessLost(subject, organizationId)` |
|---|---|---|
| neither (background work) | continues | cancels on `Subject`, and `OrganizationId` when set |
| `SessionId` (live work, one sign-in) | cancels on `SessionId` | same |
| `OnSessionEnded = Cancel`, no `SessionId` | cancels on `Subject` | same |
| `SessionId`, `OnSessionEnded = Continue` | continues | same |
| `OnAccessLost = Continue` | per the rows above | continues |

- A signal returns how many operations it cancelled, not counting one already cancelled or disposed.
- `CancelReason` is set before the token cancels, and stays null when the caller's own token cancelled first.
- A blank `SessionId` or `OrganizationId` counts as none, so a missing claim matches on the subject.
- Token callbacks run on the signal's thread, so keep them short; one that throws is logged and the signal goes on.

## Signal memory

- Work that starts later than it was asked for, such as a queued job, names `AcceptedAt`: for `SignalMemory`, `Begin` returns
  a lease already cancelled when a matching signal was heard since then.
- With no `AcceptedAt`, only a signal heard while the work runs reaches it, so signing in again or regaining access is never
  held against new work. Zero turns the memory off.
- Each signal sweeps the expired entries, and a repeated signal refreshes its own, so memory holds one window's signals.

## Scope

- In process only, one tracker per host. A job on another replica needs a broker to hear the signal: repeat it there and
  call the tracker on each host.
- Core does not know Identity's events; Identity bridges them to `SessionEnded` and `AccessLost`.
- The tracker is not durable: a restart forgets running leases and remembered signals, so re-check a resumed job's person.
