using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using WoW.Two.Sdk.Beta.Be.Identity.Authorization;

namespace WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

/// <summary>Admits an API key that grants the requirement's scope, or a session identity the principal allowlist admits.</summary>
/// <remarks>
///   - the session branch skips every identity the <c>ApiKey</c> scheme authenticated, so a key's name never passes for an
///     allowlisted login and a key never rides the open (empty) allowlist
///   - the allowlist keeps its own rule: empty <see cref="AllowlistOptions.Allowed"/> admits any authenticated session
///   - without <c>AddPrincipalAllowlist</c> the allowlist is the open default
/// </remarks>
public sealed class AllowlistedSessionOrApiKeyAuthorizationHandler : AuthorizationHandler<AllowlistedSessionOrApiKeyRequirement>
{
    private readonly AllowlistOptions _allowlist;

    /// <summary>Creates the handler with the open default allowlist, for hosts that register none.</summary>
    public AllowlistedSessionOrApiKeyAuthorizationHandler()
        : this(new AllowlistOptions())
    {
    }

    /// <summary>Creates the handler from the registered principal allowlist.</summary>
    /// <param name="allowlist">Allowlist options — claim type, allowed values, case sensitivity.</param>
    public AllowlistedSessionOrApiKeyAuthorizationHandler(AllowlistOptions allowlist)
    {
        ArgumentNullException.ThrowIfNull(allowlist);
        _allowlist = allowlist;
    }

    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AllowlistedSessionOrApiKeyRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (context.User.HasApiKeyScope(requirement.Scope) || IsAllowlistedSession(context.User))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }

    private bool IsAllowlistedSession(ClaimsPrincipal user)
    {
        var comparer = _allowlist.CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return user.Identities.Any(identity =>
            identity.IsAuthenticated
            && !string.Equals(identity.AuthenticationType, ApiKeyAuthenticationDefaults.Scheme, StringComparison.Ordinal)
            && (_allowlist.Allowed.Count == 0
                || identity.FindAll(_allowlist.ClaimType).Any(claim => _allowlist.Allowed.Contains(claim.Value, comparer))));
    }
}
