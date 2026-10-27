namespace WoW.Two.Sdk.Beta.Be.Web.SecureHeaders;

/// <summary>
/// Marks an endpoint that answers one secure header its own way: <c>UseOwaspSecureHeaders</c> writes the preset, then
/// sets <see cref="Name"/> to <see cref="Value"/>, or removes it when the value is null. Reach for it on an endpoint
/// whose content needs a stricter or looser header than the API's, such as a sandboxing policy on user content.
/// </summary>
/// <param name="name">The header to answer.</param>
/// <param name="value">The value to send; null removes the header.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
public sealed class SecureHeaderOverrideAttribute(string name, string? value) : Attribute
{
    /// <summary>Gets the header the endpoint answers its own way.</summary>
    public string Name { get; } = name;

    /// <summary>Gets the value the endpoint sends; null removes the header.</summary>
    public string? Value { get; } = value;
}
