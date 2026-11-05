using Microsoft.AspNetCore.Http;

namespace WoW.Two.Sdk.Beta.Be.Messaging.Webhooks.Inbound.Validators;

/// <summary>
/// Defines one signature scheme's validation of an inbound webhook. Registered as a keyed service under the scheme name
/// (<see cref="WebhookSchemeNameConstants"/>), so a product adds a scheme by registering its own key.
/// </summary>
public interface IWebhookSignatureValidator
{
    /// <summary>Validates the request's signature with the receiver's secrets and reads the delivery id.</summary>
    /// <param name="headers">The request headers.</param>
    /// <param name="body">The raw request body, exactly as received.</param>
    /// <param name="receiver">The receiver's secrets and tolerance.</param>
    /// <param name="now">The current time, for the replay window.</param>
    WebhookSignatureResult Validate(IHeaderDictionary headers, ReadOnlySpan<byte> body, WebhookReceiverOptions receiver, DateTimeOffset now);

    /// <summary>
    /// Validates the request the way <see cref="WebhookReceiverService"/> calls it. The default runs <see cref="Validate"/>;
    /// a scheme whose check waits on I/O, such as fetching the sender's JWKS, implements this one to stay off sync-over-async
    /// and answers <see cref="Validate"/> with a failure.
    /// </summary>
    /// <param name="headers">The request headers.</param>
    /// <param name="body">The raw request body, exactly as received.</param>
    /// <param name="receiver">The receiver's secrets and tolerance.</param>
    /// <param name="now">The current time, for the replay window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome, with what the sender stated about the delivery.</returns>
    ValueTask<WebhookSignatureResult> ValidateAsync(IHeaderDictionary headers, ReadOnlyMemory<byte> body, WebhookReceiverOptions receiver, DateTimeOffset now, CancellationToken cancellationToken)
        => ValueTask.FromResult(Validate(headers, body.Span, receiver, now));
}
