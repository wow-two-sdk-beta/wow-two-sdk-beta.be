namespace WoW.Two.Sdk.Beta.Be.Jobs.Core.PersonOperations;

/// <summary>Represents one signal the tracker heard: a session ending, or a person losing access in an organization.</summary>
internal sealed record PersonOperationSignal
{
    /// <summary>Gets what happened, as <see cref="PersonOperationLease.CancelReason"/> states it.</summary>
    public required string Reason { get; init; }

    /// <summary>Gets the person the signal is about.</summary>
    public required string Subject { get; init; }

    /// <summary>Gets the session that ended; null on an access signal.</summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the organization access went in; null on a session signal.</summary>
    public string? OrganizationId { get; init; }

    /// <summary>Tells whether this signal cancels the operation; a blank session or organization counts as none, so it fails toward cancelling.</summary>
    /// <param name="operation">The operation's declaration.</param>
    public bool Cancels(PersonOperationDeclaration operation)
        => Reason == PersonOperationCancelReasonConstants.SessionEnded ? EndsSessionOf(operation) : RemovesAccessOf(operation);

    private bool EndsSessionOf(PersonOperationDeclaration operation)
        => string.IsNullOrWhiteSpace(operation.SessionId)
            ? operation.OnSessionEnded == PersonOperationReaction.Cancel && operation.Subject == Subject
            : operation.OnSessionEnded != PersonOperationReaction.Continue && operation.SessionId == SessionId;

    private bool RemovesAccessOf(PersonOperationDeclaration operation)
        => operation.OnAccessLost == PersonOperationReaction.Cancel
            && operation.Subject == Subject
            && (string.IsNullOrWhiteSpace(operation.OrganizationId) || operation.OrganizationId == OrganizationId);
}
