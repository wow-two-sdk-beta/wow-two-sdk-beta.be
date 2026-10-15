using WoW.Two.Sdk.Backend.Beta.Integrations;

namespace WoW.Two.Sdk.Backend.Beta.Testing.Integrations;

/// <summary>Supplies one fixed access token to the SDK integration clients, standing in for a signed-in user's OAuth token.</summary>
/// <param name="token">The token every call carries; <see langword="null"/> drives the no-token (unauthorized or anonymous) branch.</param>
public sealed class FixedAccessTokenService(string? token) : IAccessTokenService
{
    /// <summary>Gets how many times a token was asked for.</summary>
    public int Calls { get; private set; }

    /// <inheritdoc />
    public Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(token);
    }
}
