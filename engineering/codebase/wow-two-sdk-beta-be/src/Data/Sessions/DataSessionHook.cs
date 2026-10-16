namespace WoW.Two.Sdk.Beta.Be.Data.Sessions;

/// <summary>Holds one commit callback and its optional deduplication key.</summary>
internal sealed record DataSessionHook
{
    internal required Func<CancellationToken, ValueTask> Action { get; init; }
    internal string? Key { get; init; }
}
