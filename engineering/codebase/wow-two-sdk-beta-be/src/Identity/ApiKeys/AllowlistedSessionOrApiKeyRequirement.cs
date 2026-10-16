using Microsoft.AspNetCore.Authorization;

namespace WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

/// <summary>Requires an allowlisted session or an API key that grants <see cref="Scope"/>; evaluated by <see cref="AllowlistedSessionOrApiKeyAuthorizationHandler"/>.</summary>
public sealed class AllowlistedSessionOrApiKeyRequirement : IAuthorizationRequirement
{
    /// <summary>Creates the requirement for the scope a key must grant.</summary>
    /// <param name="scope">The scope an API key must grant, such as <c>catalog:read</c>.</param>
    public AllowlistedSessionOrApiKeyRequirement(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        Scope = scope;
    }

    /// <summary>Gets the scope an API key must grant.</summary>
    public string Scope { get; }
}
