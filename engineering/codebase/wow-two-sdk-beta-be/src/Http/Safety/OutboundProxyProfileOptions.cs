using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace WoW.Two.Sdk.Beta.Be.Http.Safety;

/// <summary>Configures one fixed proxy endpoint or an HTTP proxy lease broker.</summary>
public sealed class OutboundProxyProfileOptions
{
    /// <summary>Gets or sets the fixed proxy URI without credentials.</summary>
    public string? Server { get; set; }
    /// <summary>Gets or sets the fixed proxy username supplied by the host.</summary>
    public string? Username { get; set; }
    /// <summary>Gets or sets the fixed proxy password supplied by the host's secret store.</summary>
    public string? Password { get; set; }
    /// <summary>Gets or sets the broker settings for an allocated proxy.</summary>
    public OutboundProxyBrokerOptions? Broker { get; set; }

    /// <summary>Checks whether the profile declares exactly one complete connection mode.</summary>
    public bool IsValid() => Broker is null
        ? TryGetFixedEndpoint(out _)
        : !HasFixedSettings && Broker.TryGetEndpoints(out _, out _);

    internal bool HasFixedSettings =>
        !string.IsNullOrEmpty(Server) || !string.IsNullOrEmpty(Username) || !string.IsNullOrEmpty(Password);

    internal bool TryGetFixedEndpoint([NotNullWhen(true)] out Uri? endpoint) =>
        SafeOutboundProxy.TryParseEndpoint(Server, out endpoint)
        && (string.IsNullOrEmpty(Password) || !string.IsNullOrWhiteSpace(Username));

    internal static NetworkCredential? GetCredentials(string? username, string? password) =>
        string.IsNullOrEmpty(username) ? null : new NetworkCredential(username, password);
}
