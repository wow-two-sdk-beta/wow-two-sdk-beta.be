namespace WoW.Two.Sdk.Beta.Be.Comms.Email.SendGrid;

/// <summary>Holds sendGrid API settings.</summary>
public sealed record SendGridEmailOptions
{
    /// <summary>SendGrid API key. Required — source from a secret store.</summary>
    public string ApiKey { get; set; } = "";
}
