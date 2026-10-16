using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Messaging.Reliability;
using WoW.Two.Sdk.Beta.Be.Messaging.Saga;
using WoW.Two.Sdk.Beta.Be.Testing.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Messaging.Tests;

/// <summary>State for the machine that keeps running after payment.</summary>
public sealed class ShipmentSagaState : SagaState;
