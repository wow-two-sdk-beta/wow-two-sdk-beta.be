using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Messaging.Reliability;
using WoW.Two.Sdk.Beta.Be.Testing.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Messaging.Tests;

/// <summary>An event whose handler never recovers — the infinite-redrive guard's subject.</summary>
public sealed record RedrivePoison : IEvent
{
    /// <summary>Gets the event's payload value.</summary>
    public required string Value { get; init; }
}
