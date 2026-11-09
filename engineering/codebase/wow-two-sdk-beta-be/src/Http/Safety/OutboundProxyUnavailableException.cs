namespace WoW.Two.Sdk.Beta.Be.Http.Safety;

/// <summary>Reports a proxy configuration, allocation or expiry failure without revealing credentials.</summary>
/// <param name="message">A diagnostic message without secret values.</param>
public sealed class OutboundProxyUnavailableException(string message) : InvalidOperationException(message);
