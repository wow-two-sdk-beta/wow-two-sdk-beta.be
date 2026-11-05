namespace WoW.Two.Sdk.Beta.Be.Messaging.Webhooks.Issuers;

/// <summary>Represents what one delivery attempt signs.</summary>
public sealed record WebhookSigningModel
{
    /// <summary>Gets the subscription's secret; empty under <see cref="WebhookSchemeNameConstants.KeySigned"/>.</summary>
    public required string Secret { get; init; }

    /// <summary>Gets the subscription's target, so a signer can bind the audience; the dispatcher always fills it.</summary>
    public Uri? Url { get; init; }

    /// <summary>Gets the delivery id, the same on every retry.</summary>
    public required string DeliveryId { get; init; }

    /// <summary>Gets the event type.</summary>
    public required string EventType { get; init; }

    /// <summary>Gets the signing time.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the body, signed as sent.</summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }
}
