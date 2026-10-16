using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Messaging.Transport;
using WoW.Two.Sdk.Beta.Be.Testing.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Messaging.Tests;

/// <summary>A request nothing ever answers — the timeout path.</summary>
public sealed record SilentRequested : IEvent
{
    /// <summary>Gets the order id the silent request is for.</summary>
    public required string OrderId { get; init; }
}
