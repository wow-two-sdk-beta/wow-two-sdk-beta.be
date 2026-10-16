using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Messaging.Saga;
using WoW.Two.Sdk.Beta.Be.Testing.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Messaging.Tests;

/// <summary>Published when a saga instance completes.</summary>
public sealed record OrderConfirmed : IEvent
{
    /// <summary>Gets the confirmed order's id.</summary>
    public required string OrderId { get; init; }

    /// <summary>Gets the confirmed order's total.</summary>
    public required decimal Total { get; init; }
}
