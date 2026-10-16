using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Messaging.Reliability;
using WoW.Two.Sdk.Beta.Be.Testing.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Messaging.Tests;

/// <summary>An event whose handler fails until an operator flips <see cref="FlakyToggle"/> — the fix-then-redrive story.</summary>
public sealed record FlakyEvent : IEvent
{
    /// <summary>Gets the event's payload value.</summary>
    public required string Value { get; init; }
}
