using System.Text.RegularExpressions;
using FluentValidation;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

/// <summary>
/// Extends FluentValidation with format rules — one named rule per <see cref="FormatPatternConstants"/> entry, an https-address
/// rule, and <see cref="MustMatch{T}"/> for a pattern of the caller's own.
/// </summary>
/// <remarks>
///   - a named rule fails <see langword="null"/> and reads <c>'{PropertyName}' must be {shape}.</c>; it reports
///     FluentValidation's <c>PredicateValidator</c> code, so a client renders the server's message
///   - <see cref="MustBeNameSlug{T}"/> is a <c>Matches</c> rule: it passes <see langword="null"/> and reports
///     <c>RegularExpressionValidator</c>
///   - to word a shape differently, call <see cref="MustMatch{T}"/> with the same pattern; the accepted values stay the same
/// </remarks>
public static partial class FormatRuleExtensions
{
    /// <summary>Requires a name slug (<see cref="FormatPatternConstants.NameSlug"/>): a lowercase letter, then lowercase letters,
    /// digits or dashes, 48 at most.</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeNameSlug<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .Matches(NameSlugRegex())
            .WithMessage("'{PropertyName}' must start with a lowercase letter and hold only lowercase letters, digits and dashes (48 at most).");

    /// <summary>Requires text that matches <paramref name="pattern"/>, named in the message as <paramref name="shape"/>.</summary>
    /// <param name="rule">The rule builder.</param>
    /// <param name="pattern">The regular expression, such as one of the <see cref="FormatPatternConstants"/>.</param>
    /// <param name="shape">How the message names the expected shape: <c>'{PropertyName}' must be {shape}.</c></param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustMatch<T>(this IRuleBuilder<T, string> rule, string pattern, string shape)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        ArgumentException.ThrowIfNullOrEmpty(shape);
        return rule
            .Must(value => value is not null && Regex.IsMatch(value, pattern))
            .WithMessage("'{PropertyName}' must be " + shape + ".");
    }

    /// <summary>Requires nothing, or an absolute https address of 2048 characters at most with a host and no user
    /// information, so a rendered link never carries a credential or opens another scheme. The value is trimmed first.</summary>
    /// <param name="rule">The rule builder.</param>
    /// <param name="example">The address the message offers as an example.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string?> MustBeHttpsAddress<T>(
        this IRuleBuilder<T, string?> rule, string example = "https://example.com/")
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentException.ThrowIfNullOrEmpty(example);
        return rule
            .Must(IsHttpsAddressOrEmpty)
            .WithMessage("'{PropertyName}' must be an https address, such as " + example + ".");
    }

    /// <summary>Whether <paramref name="value"/> is empty, or an https address <see cref="MustBeHttpsAddress{T}"/> accepts.</summary>
    /// <param name="value">The candidate address.</param>
    /// <returns><see langword="true"/> when nothing was given or the address passes.</returns>
    public static bool IsHttpsAddressOrEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || (value.Trim().Length <= 2048
            && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Length > 0
            && uri.UserInfo.Length == 0);

    /// <summary>Requires a GitHub repository as <c>owner/name</c> (<see cref="FormatPatternConstants.GitHubRepository"/>).</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeGitHubRepository<T>(this IRuleBuilder<T, string> rule) =>
        Shaped(rule, GitHubRepositoryRegex(), "a GitHub repository as owner/name");

    /// <summary>Requires a Git branch name (<see cref="FormatPatternConstants.GitBranch"/>).</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeGitBranch<T>(this IRuleBuilder<T, string> rule) =>
        Shaped(rule, GitBranchRegex(), "a branch name");

    /// <summary>Requires a file name (<see cref="FormatPatternConstants.FileName"/>).</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeFileName<T>(this IRuleBuilder<T, string> rule) =>
        Shaped(rule, FileNameRegex(), "a file name");

    /// <summary>Requires a GitHub Actions workflow file name (<see cref="FormatPatternConstants.WorkflowFileName"/>).</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeWorkflowFileName<T>(this IRuleBuilder<T, string> rule) =>
        Shaped(rule, WorkflowFileNameRegex(), "a workflow file name ending in .yml or .yaml");

    /// <summary>Requires a container image repository (<see cref="FormatPatternConstants.ImageRepository"/>).</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeImageRepository<T>(this IRuleBuilder<T, string> rule) =>
        Shaped(rule, ImageRepositoryRegex(), "an image repository such as ghcr.io/owner/product/api");

    /// <summary>Requires a domain name of two or more lowercase labels (<see cref="FormatPatternConstants.DomainName"/>).</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeDomainName<T>(this IRuleBuilder<T, string> rule) =>
        Shaped(rule, DomainNameRegex(), "a lowercase host name such as app.example.com");

    /// <summary>Requires a host name of one lowercase label (<see cref="FormatPatternConstants.HostLabel"/>).</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeHostLabel<T>(this IRuleBuilder<T, string> rule) =>
        Shaped(rule, HostLabelRegex(), "a lowercase host name of one label, such as edge-1");

    /// <summary>Requires a host name or IP address to connect to (<see cref="FormatPatternConstants.NetworkHost"/>).</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeNetworkHost<T>(this IRuleBuilder<T, string> rule) =>
        Shaped(rule, NetworkHostRegex(), "a host name or address");

    /// <summary>Requires a Unix user name (<see cref="FormatPatternConstants.UnixUserName"/>).</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeUnixUserName<T>(this IRuleBuilder<T, string> rule) =>
        Shaped(rule, UnixUserNameRegex(), "a lowercase user name");

    /// <summary>Requires an IPv4 address in dotted-decimal form (<see cref="FormatPatternConstants.Ipv4Address"/>).</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeIpv4Address<T>(this IRuleBuilder<T, string> rule) =>
        Shaped(rule, Ipv4AddressRegex(), "an IPv4 address such as 203.0.113.10");

    /// <summary>Requires an SSH key fingerprint (<see cref="FormatPatternConstants.SshKeyFingerprint"/>).</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeSshKeyFingerprint<T>(this IRuleBuilder<T, string> rule) =>
        Shaped(rule, SshKeyFingerprintRegex(), "an SSH key fingerprint as ssh-keygen -l prints it");

    /// <summary>Requires one SSH public key line (<see cref="FormatPatternConstants.SshPublicKey"/>).</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeSshPublicKey<T>(this IRuleBuilder<T, string> rule) =>
        Shaped(rule, SshPublicKeyRegex(), "one SSH public key line");

    /// <summary>Requires a SHA-256 digest in lowercase hex (<see cref="FormatPatternConstants.Sha256Hex"/>).</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeSha256Hex<T>(this IRuleBuilder<T, string> rule) =>
        Shaped(rule, Sha256HexRegex(), "a SHA-256 digest in lowercase hex");

    /// <summary>Requires an HTTP or HTTPS address with no path (<see cref="FormatPatternConstants.HttpEndpoint"/>).</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeHttpEndpoint<T>(this IRuleBuilder<T, string> rule) =>
        Shaped(rule, HttpEndpointRegex(), "an address such as http://host:8080");

    /// <summary>Requires a Docker network name (<see cref="FormatPatternConstants.DockerNetwork"/>).</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeDockerNetwork<T>(this IRuleBuilder<T, string> rule) =>
        Shaped(rule, DockerNetworkRegex(), "a Docker network name");

    /// <summary>Requires a provider region code (<see cref="FormatPatternConstants.RegionCode"/>).</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeRegionCode<T>(this IRuleBuilder<T, string> rule) =>
        Shaped(rule, RegionCodeRegex(), "a region such as hel1");

    /// <summary>Requires a request path (<see cref="FormatPatternConstants.RequestPath"/>).</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeRequestPath<T>(this IRuleBuilder<T, string> rule) =>
        Shaped(rule, RequestPathRegex(), "a path starting with /");

    /// <summary>Requires a configuration key (<see cref="FormatPatternConstants.ConfigurationKey"/>).</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeConfigurationKey<T>(this IRuleBuilder<T, string> rule) =>
        Shaped(rule, ConfigurationKeyRegex(), "a configuration key such as Billing:SecretKey");

    /// <summary>A rule that fails <see langword="null"/> and any value <paramref name="regex"/> does not match.</summary>
    private static IRuleBuilderOptions<T, string> Shaped<T>(IRuleBuilder<T, string> rule, Regex regex, string shape)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule
            .Must(value => value is not null && regex.IsMatch(value))
            .WithMessage("'{PropertyName}' must be " + shape + ".");
    }

    [GeneratedRegex(FormatPatternConstants.NameSlug)]
    private static partial Regex NameSlugRegex();

    [GeneratedRegex(FormatPatternConstants.GitHubRepository)]
    private static partial Regex GitHubRepositoryRegex();

    [GeneratedRegex(FormatPatternConstants.GitBranch)]
    private static partial Regex GitBranchRegex();

    [GeneratedRegex(FormatPatternConstants.FileName)]
    private static partial Regex FileNameRegex();

    [GeneratedRegex(FormatPatternConstants.WorkflowFileName)]
    private static partial Regex WorkflowFileNameRegex();

    [GeneratedRegex(FormatPatternConstants.ImageRepository)]
    private static partial Regex ImageRepositoryRegex();

    [GeneratedRegex(FormatPatternConstants.DomainName)]
    private static partial Regex DomainNameRegex();

    [GeneratedRegex(FormatPatternConstants.HostLabel)]
    private static partial Regex HostLabelRegex();

    [GeneratedRegex(FormatPatternConstants.NetworkHost)]
    private static partial Regex NetworkHostRegex();

    [GeneratedRegex(FormatPatternConstants.UnixUserName)]
    private static partial Regex UnixUserNameRegex();

    [GeneratedRegex(FormatPatternConstants.Ipv4Address)]
    private static partial Regex Ipv4AddressRegex();

    [GeneratedRegex(FormatPatternConstants.SshKeyFingerprint)]
    private static partial Regex SshKeyFingerprintRegex();

    [GeneratedRegex(FormatPatternConstants.SshPublicKey)]
    private static partial Regex SshPublicKeyRegex();

    [GeneratedRegex(FormatPatternConstants.Sha256Hex)]
    private static partial Regex Sha256HexRegex();

    [GeneratedRegex(FormatPatternConstants.HttpEndpoint)]
    private static partial Regex HttpEndpointRegex();

    [GeneratedRegex(FormatPatternConstants.DockerNetwork)]
    private static partial Regex DockerNetworkRegex();

    [GeneratedRegex(FormatPatternConstants.RegionCode)]
    private static partial Regex RegionCodeRegex();

    [GeneratedRegex(FormatPatternConstants.RequestPath)]
    private static partial Regex RequestPathRegex();

    [GeneratedRegex(FormatPatternConstants.ConfigurationKey)]
    private static partial Regex ConfigurationKeyRegex();
}
