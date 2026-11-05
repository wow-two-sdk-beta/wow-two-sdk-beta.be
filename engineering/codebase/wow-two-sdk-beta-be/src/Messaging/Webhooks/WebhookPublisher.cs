using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Messaging.Reliability;

namespace WoW.Two.Sdk.Beta.Be.Messaging.Webhooks;

/// <summary>Default <see cref="IWebhookPublisher"/> — resolves matching subscriptions and fans a signed delivery out to each.</summary>
internal sealed class WebhookPublisher(IWebhookSubscriptionRepository store, HttpWebhookDispatcher dispatcher) : IWebhookPublisher
{
    public async ValueTask PublishAsync(string eventType, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventType);

        var subscriptions = await store.GetMatchingAsync(eventType, cancellationToken);

        // At once, each on its own retry budget: a dead or slow subscriber never delays another.
        await Task.WhenAll(subscriptions.Select(subscription => dispatcher.DeliverAsync(subscription, eventType, payload, cancellationToken).AsTask()));
    }

    public ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        // The bytes and the Type.Name token are subscriber-visible contract — webhooks.md § Payload contract.
        var payload = JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType());
        return PublishAsync(typeof(TEvent).Name, payload, cancellationToken);
    }
}
