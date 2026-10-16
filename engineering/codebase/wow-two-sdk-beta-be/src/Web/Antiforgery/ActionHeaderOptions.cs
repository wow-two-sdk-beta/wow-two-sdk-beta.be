namespace WoW.Two.Sdk.Beta.Be.Web.Antiforgery;

/// <summary>Holds the request header <see cref="RequireActionHeaderAttribute"/> reads the named action from.</summary>
public sealed record ActionHeaderOptions
{
    /// <summary>The host configuration section applied after code configuration.</summary>
    public const string SectionName = "Web:ActionHeader";

    /// <summary>Gets or sets the header a write carries its action name in, such as <c>X-Console-Action</c>. Defaults to <c>X-Requested-Action</c>.</summary>
    public string HeaderName { get; set; } = "X-Requested-Action";
}
