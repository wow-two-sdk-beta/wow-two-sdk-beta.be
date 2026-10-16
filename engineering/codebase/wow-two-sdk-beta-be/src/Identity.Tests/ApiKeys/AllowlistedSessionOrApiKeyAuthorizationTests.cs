using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;
using WoW.Two.Sdk.Beta.Be.Identity.Authorization;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.ApiKeys;

/// <summary>An allowlisted session or a key granting the scope passes; a key never rides the session branch.</summary>
public sealed class AllowlistedSessionOrApiKeyAuthorizationTests
{
    private const string Scope = "catalog:read";
    private const string SessionScheme = "Cookies";
    private const string UsernameClaim = "wt:username";

    [Fact]
    public async Task Session_OnTheAllowlist_Passes_AndOneOffItIsRefused()
    {
        var authorization = Build(allowed: ["alice"]);

        (await AuthorizeAsync(authorization, Session("alice"))).Should().BeTrue();
        (await AuthorizeAsync(authorization, Session("mallory"))).Should().BeFalse();
    }

    [Fact]
    public async Task Key_WithTheScope_Passes_WhateverTheAllowlist()
    {
        var authorization = Build(allowed: ["alice"]);

        (await AuthorizeAsync(authorization, Key("ci", Scope))).Should().BeTrue();
        (await AuthorizeAsync(authorization, Key("ci", "logs:read"))).Should().BeFalse();
    }

    [Fact]
    public async Task Key_WithoutTheScope_NeverPassesTheOpenAllowlist()
    {
        var authorization = Build(allowed: []);

        (await AuthorizeAsync(authorization, Session("anyone"))).Should().BeTrue("an empty allowlist stays open to sessions");
        (await AuthorizeAsync(authorization, Key("script", "logs:read"))).Should().BeFalse();
    }

    [Fact]
    public async Task Key_NamedLikeAnAllowlistedLogin_IsNotASession()
    {
        var authorization = Build(allowed: ["alice"]);
        var key = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "alice"), new Claim(UsernameClaim, "alice")],
            ApiKeyAuthenticationDefaults.Scheme);

        (await AuthorizeAsync(authorization, new ClaimsPrincipal(key))).Should().BeFalse();
    }

    [Fact]
    public async Task Handler_WithoutARegisteredAllowlist_AdmitsAnySession()
    {
        var authorization = Build(allowed: null);

        (await AuthorizeAsync(authorization, Session("anyone"))).Should().BeTrue();
        (await AuthorizeAsync(authorization, Key("script", "logs:read"))).Should().BeFalse();
    }

    [Fact]
    public void Policy_ShouldAuthenticateTheSessionAndKeySchemes()
    {
        var policy = new AuthorizationPolicyBuilder().RequireAllowlistedSessionOrApiKeyScope(Scope, SessionScheme).Build();

        policy.AuthenticationSchemes.Should().Equal(SessionScheme, ApiKeyAuthenticationDefaults.Scheme);
        policy.Requirements.OfType<AllowlistedSessionOrApiKeyRequirement>().Single().Scope.Should().Be(Scope);
        FluentActions.Invoking(() => new AuthorizationPolicyBuilder().RequireAllowlistedSessionOrApiKeyScope(Scope))
            .Should().Throw<ArgumentException>();
    }

    private static IAuthorizationService Build(string[]? allowed)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddApiKeyAuthentication(keys => keys.Marker = "tf_");
        if (allowed is not null)
        {
            services.AddPrincipalAllowlist(options =>
            {
                options.ClaimType = UsernameClaim;
                foreach (var login in allowed)
                    options.Allowed.Add(login);
            });
        }

        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static async Task<bool> AuthorizeAsync(IAuthorizationService authorization, ClaimsPrincipal user)
    {
        var policy = new AuthorizationPolicyBuilder().RequireAllowlistedSessionOrApiKeyScope(Scope, SessionScheme).Build();
        return (await authorization.AuthorizeAsync(user, resource: null, policy)).Succeeded;
    }

    private static ClaimsPrincipal Session(string login) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, login), new Claim(UsernameClaim, login)], SessionScheme));

    private static ClaimsPrincipal Key(string name, string scope) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, name), new Claim(ApiKeyAuthenticationDefaults.ScopeClaim, scope)],
            ApiKeyAuthenticationDefaults.Scheme));
}
