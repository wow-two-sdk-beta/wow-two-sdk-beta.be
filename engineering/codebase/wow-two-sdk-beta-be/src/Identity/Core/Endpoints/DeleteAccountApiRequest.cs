namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Confirms deleting the signed-in account.</summary>
public sealed record DeleteAccountApiRequest
{
    /// <summary>The account's password; required when the account has one.</summary>
    public string? Password { get; init; }
}
