namespace WoW.Two.Sdk.Beta.Be.Identity.CurrentUser;

/// <summary>Refers to how <see cref="ICurrentUserService"/> identifies the calling principal.</summary>
public enum UserKind
{
    /// <summary>No identity — neither an authenticated principal nor a guest cookie is present.</summary>
    Anonymous,

    /// <summary>A cookie-provisioned guest — identified but not registered (see <c>IGuestSessionService</c> in the be sdk).</summary>
    Guest,

    /// <summary>A registered, authenticated account.</summary>
    User,

    /// <summary>A service calling as itself, identified by its client id.</summary>
    Service,

    /// <summary>A caller authenticated by an API key, identified by the key's id.</summary>
    ApiKey,
}
