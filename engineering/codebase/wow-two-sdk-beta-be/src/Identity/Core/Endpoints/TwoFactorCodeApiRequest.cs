namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Proves the authenticator holds the account's key.</summary>
public sealed record TwoFactorCodeApiRequest
{
    /// <summary>A current authenticator code.</summary>
    public required string Code { get; init; }
}
