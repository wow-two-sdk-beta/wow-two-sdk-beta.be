using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Messaging.Transport;
using WoW.Two.Sdk.Beta.Be.Testing.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Messaging.Tests;

/// <summary>
/// Handles <see cref="PriceQuoted"/> events so the test pipeline can report dispatch success. Nothing asserts on this
/// handler's behaviour — what matters is the <see cref="ConsumeOutcome"/>, which is <c>Success</c> only when a reply was
/// not intercepted by the request client's consume filter.
/// </summary>
public sealed class PriceQuotedHandler : IEventHandler<PriceQuoted>
{
    public ValueTask HandleAsync(EventContext<PriceQuoted> context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
