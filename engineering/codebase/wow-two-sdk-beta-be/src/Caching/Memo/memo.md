# Caching.Memo

*One in-process value shared by every caller, whose forced refresh joins a read already in flight.*

Namespace: `WoW.Two.Sdk.Beta.Be.Caching.Memo`.

`CoalescingMemo<T>` holds the last value a costly read produced — a remote snapshot, a child process's output — so every
page, tab and tool shares one read per holding time. HybridCache coalesces concurrent misses too, but its refresh
cannot join a read under way; a memo's `fresh` call can.

## Use

```csharp
builder.Services.AddSingleton<CoalescingMemo<AppResult<JsonElement>>>();   // one per read; TimeProvider from DI

public sealed class Vitals(IGateway gateway, CoalescingMemo<AppResult<JsonElement>> memo, Settings settings)
{
    public async Task<AppResult<JsonElement>> ReadAsync(bool fresh, CancellationToken ct) =>
        await memo.GetAsync(
            async token =>
            {
                var read = await gateway.ReadAsync(token);
                return read is AppResult<JsonElement>.Success { Data: var data }
                    ? CacheResult.Keep(AppResult<JsonElement>.Ok(data.Clone()))   // held
                    : CacheResult.Transient(read);                                 // returned, never held
            },
            TimeSpan.FromSeconds(Math.Clamp(settings.HoldSeconds, 0, 600)),
            fresh,
            ct);
}
```

| Call | Serves |
|---|---|
| `fresh: false`, held value younger than `holdFor` | the held value, no wait |
| `fresh: false`, otherwise | a read that finished while it waited at the gate, else its own read |
| `fresh: true` | a read that finished after it asked — the one under way — else its own read |

- One gate: concurrent callers wait for one read; each later waiter takes its result.
- `CacheResult.Keep` holds the value; `CacheResult.Transient` returns it unheld, as a failure should be.
- `holdFor` is per call, so a live setting changes it; zero or less holds nothing.
- `Forget()` drops the held value; `HeldAt` says when it was read.
- One process: the value spans no replicas. Across hosts, use `ICacheRepository` ([Caching.md](../Caching.md)).

## See also

- [Caching.md](../Caching.md) — the cache facade and `CacheResult`.
- `Jobs/Core/WakeableSchedule` — the other side of a costly read: when a periodic pass may make one.
