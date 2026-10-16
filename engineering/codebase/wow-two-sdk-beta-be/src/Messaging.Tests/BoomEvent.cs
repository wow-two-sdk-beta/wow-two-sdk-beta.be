using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Messaging.Tests;

/// <summary>An event whose handler always throws (drives retry → dead-letter).</summary>
public sealed record BoomEvent : IEvent
{
    /// <summary>Gets the event's payload value.</summary>
    public required string Value { get; init; }
}
