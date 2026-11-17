namespace WoW.Two.Sdk.Beta.Be.Integrations.Instagram;

/// <summary>Holds Instagram transport bounds without credentials or ingestion policy.</summary>
public sealed record InstagramIntegrationOptions
{
    /// <summary>The host configuration section applied after code configuration.</summary>
    public const string SectionName = "Integrations:Instagram";
    /// <summary>Gets or sets the explicit Graph origin, https://graph.facebook.com/.</summary>
    public Uri? BaseAddress { get; set; }
    /// <summary>Gets or sets the response limit, from one KiB to eight MiB.</summary>
    public int MaximumResponseBytes { get; set; } = 2_000_000;
    /// <summary>Gets or sets the permitted JSON depth, from four to sixty-four.</summary>
    public int MaximumJsonDepth { get; set; } = 32;
    /// <summary>Gets or sets the whole-call deadline, from ten milliseconds to one minute.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    internal bool IsValid() => BaseAddress is { IsAbsoluteUri: true } address
        && address.Scheme == Uri.UriSchemeHttps
        && address.IdnHost.Equals("graph.facebook.com", StringComparison.OrdinalIgnoreCase)
        && address.IsDefaultPort && address.AbsolutePath == "/"
        && address.UserInfo.Length == 0 && address.Query.Length == 0 && address.Fragment.Length == 0
        && MaximumResponseBytes is >= 1024 and <= 8_388_608
        && MaximumJsonDepth is >= 4 and <= 64
        && Timeout >= TimeSpan.FromMilliseconds(10) && Timeout <= TimeSpan.FromMinutes(1);
}
