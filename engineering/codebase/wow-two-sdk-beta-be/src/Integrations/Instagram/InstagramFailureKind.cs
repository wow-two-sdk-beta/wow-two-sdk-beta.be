namespace WoW.Two.Sdk.Beta.Be.Integrations.Instagram;

/// <summary>Identifies the closed set of Instagram request failure cases.</summary>
public enum InstagramFailureKind
{
    /// <summary>The request's identity, page size or cursor was invalid.</summary>
    InvalidRequest,
    /// <summary>The explicitly selected token source supplied no valid credential.</summary>
    CredentialsMissing,
    /// <summary>The provider requires renewed credentials.</summary>
    LoginRequired,
    /// <summary>Permissions or account eligibility do not permit the query.</summary>
    Restricted,
    /// <summary>The provider requires delayed requests.</summary>
    RateLimited,
    /// <summary>The provider returned a redirect, which is never followed.</summary>
    InvalidRedirect,
    /// <summary>The response exceeded the configured byte limit.</summary>
    ResponseTooLarge,
    /// <summary>The response JSON, media shape or pagination was invalid.</summary>
    UnexpectedContent,
    /// <summary>The provider returned an otherwise unclassified failing HTTP status.</summary>
    HttpError,
    /// <summary>The provider returned an unclassified API error or its circuit is open.</summary>
    ProviderFailed,
    /// <summary>The request could not complete because of a transport failure.</summary>
    NetworkError,
    /// <summary>The configured request deadline elapsed.</summary>
    Timeout,
}
