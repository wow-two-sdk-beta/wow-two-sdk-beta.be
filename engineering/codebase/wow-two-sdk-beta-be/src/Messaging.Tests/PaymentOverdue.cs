using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Messaging.Saga;
using WoW.Two.Sdk.Beta.Be.Testing.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Messaging.Tests;

/// <summary>The saga's own scheduled timeout — correlates by the envelope's correlation id.</summary>
public sealed record PaymentOverdue : IEvent
{
    /// <summary>Gets the overdue order's id.</summary>
    public required string OrderId { get; init; }
}
