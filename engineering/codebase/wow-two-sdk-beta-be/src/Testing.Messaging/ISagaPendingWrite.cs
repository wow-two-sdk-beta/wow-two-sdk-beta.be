using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Messaging;
using WoW.Two.Sdk.Beta.Be.Messaging.Saga;
using WoW.Two.Sdk.Beta.Be.Messaging.Transport;

namespace WoW.Two.Sdk.Beta.Be.Testing.Messaging;

/// <summary>A load waiting for the write that will complete it — or for the end of the message, which is what makes it an ignored event.</summary>
internal interface ISagaPendingWrite
{
    /// <summary>Which pass over this instance produced it, from 1. Assigned by the bracket, which owns the counter.</summary>
    int Attempt { get; set; }

    /// <summary>Record the load as a non-write outcome.</summary>
    /// <param name="outcome">The outcome to record.</param>
    /// <param name="exception">The failure, when the message threw.</param>
    void Flush(SagaTransitionOutcome outcome, Exception? exception);
}
