namespace WoW.Two.Sdk.Beta.Be.Messaging;

/// <summary>
/// Defines the marker for an event contract — a fact that happened, carried by the be sdk's <c>IEventBus</c>.
/// Only events cross the bus; commands and queries stay on the in-process mediator. Delivery topology
/// (in-memory vs broker) is a wiring choice and is deliberately absent from the contract.
/// </summary>
public interface IEvent;
