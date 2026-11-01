namespace WoW.Two.Sdk.Beta.Be.Foundation.Configuration.ConfigCheck;

/// <summary>A root section a service declares its own (<see cref="ConfigCheckServiceCollectionExtensions.AddSettingsRoot"/>).</summary>
internal sealed record SettingsRoot
{
    /// <summary>Gets the root section, such as <c>Identity</c>.</summary>
    public required string Section { get; init; }
}
