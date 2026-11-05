namespace WoW.Two.Sdk.Beta.Be.Identity.CurrentPrincipal;

/// <summary>Represents one link of an <c>act</c> claim: who acts for the person, and who acts for that actor in turn.</summary>
public sealed record ActorInfo
{
    /// <summary>Gets the acting party's <c>sub</c> value.</summary>
    public required string Subject { get; init; }

    /// <summary>Gets the party acting for this actor, or <c>null</c> at the end of the chain.</summary>
    public ActorInfo? Actor { get; init; }
}
