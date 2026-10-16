using Microsoft.AspNetCore.Authorization;

namespace WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

/// <summary>Builds authorization policies that admit API keys by the scopes they grant.</summary>
public static class ApiKeyAuthorizationPolicyBuilderExtensions
{
    /// <summary>Authenticates the policy with the <c>ApiKey</c> scheme and requires a key that grants <paramref name="scope"/>.</summary>
    /// <remarks>A key without the scope is forbidden; a caller without a key is challenged.</remarks>
    /// <param name="builder">The policy builder.</param>
    /// <param name="scope">The scope the key must grant.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static AuthorizationPolicyBuilder RequireApiKeyScope(this AuthorizationPolicyBuilder builder, string scope)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return builder
            .AddAuthenticationSchemes(ApiKeyAuthenticationDefaults.Scheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.HasApiKeyScope(scope));
    }

    /// <summary>
    /// Authenticates the policy with <paramref name="sessionSchemes"/> and the <c>ApiKey</c> scheme, and admits a session the
    /// principal allowlist admits or a key that grants <paramref name="scope"/>.
    /// </summary>
    /// <remarks>A key never passes through the session branch; a key without the scope is refused. Evaluated by <see cref="AllowlistedSessionOrApiKeyAuthorizationHandler"/>, which <c>AddApiKeyAuthentication</c> registers.</remarks>
    /// <param name="builder">The policy builder.</param>
    /// <param name="scope">The scope a key must grant.</param>
    /// <param name="sessionSchemes">The session schemes (cookie, JWT) whose principal the allowlist checks; at least one.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static AuthorizationPolicyBuilder RequireAllowlistedSessionOrApiKeyScope(
        this AuthorizationPolicyBuilder builder, string scope, params string[] sessionSchemes)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(sessionSchemes);
        if (sessionSchemes.Length == 0 || sessionSchemes.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one non-empty session scheme is required.", nameof(sessionSchemes));

        return builder
            .AddAuthenticationSchemes([.. sessionSchemes, ApiKeyAuthenticationDefaults.Scheme])
            .AddRequirements(new AllowlistedSessionOrApiKeyRequirement(scope));
    }
}
