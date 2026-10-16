namespace WoW.Two.Sdk.Beta.Be.Mediator.Idempotency;

/// <summary>Holds one cached response and the contract under which it was acquired.</summary>
internal sealed record CachedIdempotentResponse
{
    internal required Type ResponseType { get; init; }
    internal object? Response { get; init; }
}
