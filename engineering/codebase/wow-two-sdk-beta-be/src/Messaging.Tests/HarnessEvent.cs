using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Messaging.Tests;

/// <summary>An event for the harness suites — asserted through <c>MessagingTestHarness</c>, so its handler needs no collector.</summary>
public sealed record HarnessEvent : IEvent
{
    /// <summary>Gets the event's tag.</summary>
    public required string Tag { get; init; }
}
