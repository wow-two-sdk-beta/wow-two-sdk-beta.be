using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2TokenExchange;

/// <summary>Reads the subject token from the current request's <c>Authorization: Bearer</c> header, keyed by the authenticated user's <c>sid</c> and <c>org_id</c> claims or else the token's SHA-256 hex.</summary>
internal sealed class HttpContextBearerSubjectTokenSource : IOAuth2SubjectTokenSource
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextBearerSubjectTokenSource(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _httpContextAccessor = httpContextAccessor;
    }

    public ValueTask<OAuth2SubjectToken?> GetAsync(CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null
            || !AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter))
        {
            return ValueTask.FromResult<OAuth2SubjectToken?>(null);
        }

        return ValueTask.FromResult<OAuth2SubjectToken?>(new OAuth2SubjectToken { Token = header.Parameter, SessionKey = ReadSessionKey(context.User, header.Parameter) });
    }

    private static string ReadSessionKey(ClaimsPrincipal user, string token)
    {
        // An unauthenticated principal's claims prove nothing, so only an authenticated sid may share a cached token.
        var sessionId = user.Identity?.IsAuthenticated == true ? user.FindFirst("sid")?.Value : null;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }

        // The exchanged token carries the active organization, so switching it needs a token of its own.
        return user.FindFirst("org_id")?.Value is { Length: > 0 } organizationId ? $"{sessionId}:{organizationId}" : sessionId;
    }
}
