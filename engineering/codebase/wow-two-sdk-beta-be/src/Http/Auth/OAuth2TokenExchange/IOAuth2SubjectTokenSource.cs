namespace WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2TokenExchange;

/// <summary>Defines where the token-exchange handler finds the person a call acts for.</summary>
/// <remarks>Keep an implementation singleton-safe: handlers run in their own DI scope, so a request's scoped services are out of reach.</remarks>
public interface IOAuth2SubjectTokenSource
{
    /// <summary>Reads the current call's subject token.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The subject token, or <c>null</c> when no person is on this call.</returns>
    ValueTask<OAuth2SubjectToken?> GetAsync(CancellationToken cancellationToken);
}
