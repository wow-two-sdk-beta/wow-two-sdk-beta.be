namespace WoW.Two.Sdk.Beta.Be.Foundation.Security;

/// <summary>Holds where <see cref="FileMasterKeyBroker"/> reads the master key (host section <c>EnvelopeCryptography:KeyFile</c>).</summary>
public sealed record FileMasterKeyBrokerOptions
{
    /// <summary>The host configuration section applied after code configuration.</summary>
    public const string SectionName = "EnvelopeCryptography:KeyFile";

    /// <summary>The key file's absolute path; required.</summary>
    public string Path { get; set; } = "";
}
