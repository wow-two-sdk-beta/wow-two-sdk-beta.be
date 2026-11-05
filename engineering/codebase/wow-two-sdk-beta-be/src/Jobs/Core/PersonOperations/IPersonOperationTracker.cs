namespace WoW.Two.Sdk.Beta.Be.Jobs.Core.PersonOperations;

/// <summary>
/// Defines the record of person-bound operations running in this process, and the signals that reach them. One tracker per
/// host, in process only: a job on another replica hears a signal only through a broker that repeats it there.
/// </summary>
public interface IPersonOperationTracker
{
    /// <summary>
    /// Starts tracking an operation. A signal still remembered that matches it applies here, so the returned lease is
    /// already cancelled when the person's session ended or their access went before the work began.
    /// </summary>
    /// <param name="declaration">The operation and its reactions.</param>
    /// <param name="cancellationToken">The caller's token; it cancels the lease as well.</param>
    /// <returns>The lease; dispose it when the work ends.</returns>
    PersonOperationLease Begin(PersonOperationDeclaration declaration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signals a session's end: cancels operations tied to <paramref name="sessionId"/> unless they declared
    /// <see cref="PersonOperationReaction.Continue"/>, and operations of <paramref name="subject"/> tied to no session that
    /// declared <see cref="PersonOperationReaction.Cancel"/>.
    /// </summary>
    /// <param name="sessionId">The session that ended.</param>
    /// <param name="subject">The person it belonged to.</param>
    /// <returns>How many operations this signal cancelled.</returns>
    int SessionEnded(string sessionId, string subject);

    /// <summary>
    /// Signals lost access: cancels operations of <paramref name="subject"/> in <paramref name="organizationId"/>, or with no
    /// organization, unless they declared <see cref="PersonOperationReaction.Continue"/>.
    /// </summary>
    /// <param name="subject">The person who lost access.</param>
    /// <param name="organizationId">The organization they lost it in.</param>
    /// <returns>How many operations this signal cancelled.</returns>
    int AccessLost(string subject, string organizationId);
}
