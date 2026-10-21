namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>Holds the roles a membership may carry and the permissions each role grants.</summary>
/// <remarks>Set in code with <c>.AddOrganizations(o => …)</c> or in the host section <c>Identity:Organizations</c>, which is applied last.</remarks>
public sealed record OrganizationOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:Organizations";

    /// <summary>Gets or sets the role an organization's creator receives; it always exists. Default <c>owner</c>.</summary>
    public string OwnerRole { get; set; } = "owner";

    /// <summary>
    /// Gets the other roles by name, each with the permissions it grants, such as <c>editor</c> → <c>can-edit</c>.
    /// Names compare case-insensitively; the owner role may appear here to list its permissions.
    /// </summary>
    public Dictionary<string, List<string>> Roles { get; } = new(StringComparer.OrdinalIgnoreCase);
}
