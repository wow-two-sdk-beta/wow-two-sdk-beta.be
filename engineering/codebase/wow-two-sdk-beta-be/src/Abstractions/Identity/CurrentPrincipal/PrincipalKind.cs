namespace WoW.Two.Sdk.Beta.Be.Identity.CurrentPrincipal;

/// <summary>Refers to how <see cref="ICurrentPrincipal"/> identifies the caller.</summary>
public enum PrincipalKind
{
    /// <summary>Refers to a caller with no identity.</summary>
    Anonymous,

    /// <summary>Refers to a guest: identified but not registered.</summary>
    Guest,

    /// <summary>Refers to a registered, authenticated person.</summary>
    Person,

    /// <summary>Refers to a service calling as itself, identified by its client id.</summary>
    Service,

    /// <summary>Refers to a caller authenticated by an API key, identified by the key's id.</summary>
    ApiKey,
}
