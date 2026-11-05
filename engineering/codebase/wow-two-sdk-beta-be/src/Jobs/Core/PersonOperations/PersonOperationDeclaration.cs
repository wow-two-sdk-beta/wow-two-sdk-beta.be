namespace WoW.Two.Sdk.Beta.Be.Jobs.Core.PersonOperations;

/// <summary>
/// Represents a long operation accepted for a person: who it is recorded for and how it reacts when their session ends or
/// they lose access. The work runs as the service; the person is recorded, and their token is never stored.
/// </summary>
public sealed record PersonOperationDeclaration
{
    /// <summary>Gets the operation's name, for logs.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the person recorded.</summary>
    public required string Subject { get; init; }

    /// <summary>Gets the organization the work acts in; null matches an access signal on the subject alone.</summary>
    public string? OrganizationId { get; init; }

    /// <summary>Gets the one sign-in a live operation is tied to; null matches a session signal on the subject.</summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets when the person asked for the work, for work that starts later, such as a queued job: a signal heard since then
    /// cancels it at <see cref="IPersonOperationTracker.Begin"/>. Null means the work starts as it is asked for, so only a
    /// signal heard while it runs reaches it; regaining access later is never held against new work.
    /// </summary>
    public DateTimeOffset? AcceptedAt { get; init; }

    /// <summary>Gets the reaction to the session ending; null cancels with a <see cref="SessionId"/>, else continues.</summary>
    public PersonOperationReaction? OnSessionEnded { get; init; }

    /// <summary>Gets the reaction to the person losing access. Defaults to <see cref="PersonOperationReaction.Cancel"/>.</summary>
    public PersonOperationReaction OnAccessLost { get; init; } = PersonOperationReaction.Cancel;
}
