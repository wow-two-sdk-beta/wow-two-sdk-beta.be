using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Messaging;
using WoW.Two.Sdk.Beta.Be.Messaging.InMemory;
using WoW.Two.Sdk.Beta.Be.Messaging.Reliability;
using WoW.Two.Sdk.Beta.Be.Messaging.Saga;

namespace WoW.Two.Sdk.Beta.Be.Testing.Messaging;

/// <summary>Holds timing and clock defaults for a <see cref="SagaTestHarness{TState}"/>.</summary>
public sealed record SagaHarnessOptions
{
    /// <summary>Where the harness's <see cref="FakeTimeProvider"/> starts. Fixed by default, so <see cref="ISagaState.FinalizedAtUtc"/> and a timeout's due time are exact values a test can assert on.</summary>
    public DateTimeOffset StartTime { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>How far past a timeout's due time <see cref="SagaTestHarness{TState}.FireTimeoutAsync"/> advances. Default 1s — enough that a due-at-exactly-now comparison cannot go the wrong way.</summary>
    public TimeSpan TimeoutOvershoot { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Timings for the message harness underneath — quiet period, wait budget.</summary>
    public MessagingHarnessOptions Messaging { get; set; } = new();
}
