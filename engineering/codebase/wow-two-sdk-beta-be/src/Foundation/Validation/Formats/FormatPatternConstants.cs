namespace WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

/// <summary>
/// Holds the regular expressions behind the format rules (<see cref="FormatRuleExtensions"/>): names, repositories,
/// images, hosts, addresses, SSH keys and paths, as infrastructure inventories and deploy tools take them.
/// </summary>
/// <remarks>
///   - each pattern is anchored and case-sensitive; use it as written with <c>Regex.IsMatch</c>, a FluentValidation
///     <c>Matches</c>, or the named rule
///   - <c>$</c> is .NET's: it also matches before one trailing <c>\n</c>, so trim a value that may carry a line break
///   - a pattern checks shape, not meaning: <see cref="Ipv4Address"/> accepts a private address, <see cref="DomainName"/>
///     an unregistered domain
/// </remarks>
public static class FormatPatternConstants
{
    /// <summary>A name slug: a lowercase letter, then lowercase letters, digits or dashes, 48 characters at most
    /// (<c>billing-api</c>). Unlike the <c>NotSlug</c> guard it may not start with a digit, and may hold double or
    /// trailing dashes.</summary>
    public const string NameSlug = "^[a-z][a-z0-9-]{0,47}$";

    /// <summary>A GitHub repository as <c>owner/name</c>: an owner of 1–39 letters, digits or dashes, then a name of 1–100
    /// letters, digits, dots, dashes or underscores.</summary>
    public const string GitHubRepository = "^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$";

    /// <summary>A Git branch name of 1–200 characters: a letter or digit, then letters, digits, dots, slashes, dashes or
    /// underscores.</summary>
    public const string GitBranch = "^[A-Za-z0-9][A-Za-z0-9._/-]{0,199}$";

    /// <summary>A file name of 1–100 characters: a letter or digit, then letters, digits, dots, dashes or underscores
    /// (<c>app-linux-x64.tar.gz</c>).</summary>
    public const string FileName = "^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$";

    /// <summary>A GitHub Actions workflow file name ending in <c>.yml</c> or <c>.yaml</c>.</summary>
    public const string WorkflowFileName = @"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}\.ya?ml$";

    /// <summary>A container image repository without a tag, lowercase, with a registry host, an optional port and one or
    /// more path segments (<c>ghcr.io/owner/product/api</c>).</summary>
    public const string ImageRepository = "^[a-z0-9][a-z0-9.-]*(?::[0-9]{1,5})?(?:/[a-z0-9][a-z0-9._-]*)+$";

    /// <summary>A domain name of two or more lowercase labels, 253 characters at most (<c>app.example.com</c>).</summary>
    public const string DomainName =
        @"^(?=.{1,253}$)[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+$";

    /// <summary>A host name of one lowercase label, 2–63 characters: a letter first, a letter or digit last (<c>edge-1</c>).</summary>
    public const string HostLabel = "^[a-z][a-z0-9-]{0,61}[a-z0-9]$";

    /// <summary>A host name or IP address to connect to: a letter or digit, then letters, digits, dots or dashes, 253
    /// characters at most.</summary>
    public const string NetworkHost = "^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$";

    /// <summary>A Unix user name: a lowercase letter or underscore, then lowercase letters, digits, underscores or dashes,
    /// 32 characters at most.</summary>
    public const string UnixUserName = "^[a-z_][a-z0-9_-]{0,31}$";

    /// <summary>An IPv4 address in dotted-decimal form, without leading zeros (<c>203.0.113.10</c>).</summary>
    public const string Ipv4Address =
        @"^(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])(\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])){3}$";

    /// <summary>An SSH key fingerprint as <c>ssh-keygen -l</c> prints it: <c>SHA256:</c> and 43 base64 characters.</summary>
    public const string SshKeyFingerprint = "^SHA256:[A-Za-z0-9+/]{43}=?$";

    /// <summary>One SSH public key line: an Ed25519, RSA or ECDSA type, the base64 key, and an optional comment of 1–100
    /// characters without spaces.</summary>
    public const string SshPublicKey =
        @"^(ssh-ed25519|ssh-rsa|ecdsa-sha2-nistp(256|384|521)) [A-Za-z0-9+/]+=*( [^\s]{1,100})?$";

    /// <summary>A SHA-256 digest as 64 lowercase hex digits.</summary>
    public const string Sha256Hex = "^[a-f0-9]{64}$";

    /// <summary>An HTTP or HTTPS address with a host and an optional port, and no path (<c>http://vault:8080</c>).</summary>
    public const string HttpEndpoint = "^https?://[A-Za-z0-9.-]+(?::[0-9]{1,5})?$";

    /// <summary>A Docker network name: a letter or digit, then letters, digits, underscores, dots or dashes, 63
    /// characters at most.</summary>
    public const string DockerNetwork = "^[A-Za-z0-9][A-Za-z0-9_.-]{0,62}$";

    /// <summary>A provider region code: a lowercase letter or digit, then lowercase letters, digits or dashes, 32
    /// characters at most (<c>hel1</c>, <c>eu-central-1</c>).</summary>
    public const string RegionCode = "^[a-z0-9][a-z0-9-]{0,31}$";

    /// <summary>A request path starting with <c>/</c>, with an optional query, 200 characters at most
    /// (<c>/health?ready=1</c>).</summary>
    public const string RequestPath = "^/[A-Za-z0-9/_?=&.%~-]{0,199}$";

    /// <summary>A configuration key, sections included (<c>Billing:SecretKey</c>): a letter or digit, then letters,
    /// digits, dots, underscores, colons or dashes, 256 characters at most.</summary>
    public const string ConfigurationKey = "^[A-Za-z0-9][A-Za-z0-9._:-]{0,255}$";
}
