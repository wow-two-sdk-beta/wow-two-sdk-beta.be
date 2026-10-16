using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Messaging.Saga;
using WoW.Two.Sdk.Beta.Be.Testing.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Messaging.Tests;

/// <summary>Published when the timeout fires instead of the payment.</summary>
public sealed record OrderCancelled : IEvent
{
    /// <summary>Gets the cancelled order's id.</summary>
    public required string OrderId { get; init; }
}
