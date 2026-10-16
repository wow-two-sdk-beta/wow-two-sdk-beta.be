using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Messaging.Tests;

/// <summary>Handles test events by always throwing.</summary>
public sealed class BoomHandler : IEventHandler<BoomEvent>
{
    public ValueTask HandleAsync(EventContext<BoomEvent> context, CancellationToken cancellationToken)
        => throw new InvalidOperationException("boom");
}
