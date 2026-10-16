using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Messaging.Transport;
using WoW.Two.Sdk.Beta.Be.Testing.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Messaging.Tests;

/// <summary>Handles requests without replying.</summary>
public sealed class SilentRequestedHandler : IEventHandler<SilentRequested>
{
    public ValueTask HandleAsync(EventContext<SilentRequested> context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
