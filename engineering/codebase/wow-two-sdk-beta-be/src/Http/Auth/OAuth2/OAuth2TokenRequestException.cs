using System.Net;

namespace WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;

/// <summary>Represents a token endpoint's non-success answer; <see cref="HttpRequestException.StatusCode"/> carries its status.</summary>
public sealed class OAuth2TokenRequestException : HttpRequestException
{
    /// <summary>Creates a token-request failure whose message holds no server text beyond a registered error code.</summary>
    /// <param name="clientName">The HTTP client the token was fetched for.</param>
    /// <param name="statusCode">The token endpoint's status.</param>
    /// <param name="error">The body's <c>error</c> value, if any.</param>
    /// <param name="errorDescription">The body's <c>error_description</c> value, if any.</param>
    public OAuth2TokenRequestException(string clientName, HttpStatusCode statusCode, string? error, string? errorDescription)
        : base(BuildMessage(clientName, statusCode, error), inner: null, statusCode)
    {
        Error = error;
        ErrorDescription = errorDescription;
    }

    /// <summary>Gets the body's <c>error</c> value, or <c>null</c> when the body has none.</summary>
    public string? Error { get; }

    /// <summary>Gets the body's <c>error_description</c> value, or <c>null</c> when the body has none; server text, so it may echo the request.</summary>
    public string? ErrorDescription { get; }

    // A server may echo what it was sent, so the message takes only codes RFC 6749 and RFC 8707 register.
    private static string BuildMessage(string clientName, HttpStatusCode statusCode, string? error) => error
        is "invalid_request" or "invalid_client" or "invalid_grant" or "unauthorized_client"
        or "unsupported_grant_type" or "invalid_scope" or "invalid_target"
        ? $"OAuth2 token request for HTTP client '{clientName}' failed with status {(int)statusCode} ({error})."
        : $"OAuth2 token request for HTTP client '{clientName}' failed with status {(int)statusCode}.";
}
