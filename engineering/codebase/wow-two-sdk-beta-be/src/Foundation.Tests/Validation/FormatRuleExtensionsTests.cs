using System.Text.RegularExpressions;
using Ardalis.GuardClauses;
using AwesomeAssertions;
using FluentValidation;
using WoW.Two.Sdk.Beta.Be.Foundation.Guards;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Tests.Validation;

/// <summary>
/// Verifies the format vocabulary: each pattern is the one the extraction source enforced, character for character; each
/// named rule accepts exactly what its pattern accepts and reads its shape; the name slug stays apart from the
/// <c>NotSlug</c> guard; and the https-address rule refuses credentials and other schemes.
/// </summary>
public sealed class FormatRuleExtensionsTests
{
    /// <summary>The patterns as the extraction source (Wheelhouse inventory, infrastructure and vault rules) wrote them on
    /// 2026-10-30; a change here changes which values a product accepts.</summary>
    public static TheoryData<string, string> SourcePatterns => new()
    {
        { FormatPatternConstants.NameSlug, "^[a-z][a-z0-9-]{0,47}$" },
        { FormatPatternConstants.GitHubRepository, "^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$" },
        { FormatPatternConstants.GitBranch, "^[A-Za-z0-9][A-Za-z0-9._/-]{0,199}$" },
        { FormatPatternConstants.FileName, "^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$" },
        { FormatPatternConstants.WorkflowFileName, @"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}\.ya?ml$" },
        { FormatPatternConstants.ImageRepository, "^[a-z0-9][a-z0-9.-]*(?::[0-9]{1,5})?(?:/[a-z0-9][a-z0-9._-]*)+$" },
        { FormatPatternConstants.DomainName, @"^(?=.{1,253}$)[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+$" },
        { FormatPatternConstants.HostLabel, "^[a-z][a-z0-9-]{0,61}[a-z0-9]$" },
        { FormatPatternConstants.NetworkHost, "^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$" },
        { FormatPatternConstants.UnixUserName, "^[a-z_][a-z0-9_-]{0,31}$" },
        { FormatPatternConstants.Ipv4Address, @"^(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])(\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])){3}$" },
        { FormatPatternConstants.SshKeyFingerprint, "^SHA256:[A-Za-z0-9+/]{43}=?$" },
        { FormatPatternConstants.SshPublicKey, @"^(ssh-ed25519|ssh-rsa|ecdsa-sha2-nistp(256|384|521)) [A-Za-z0-9+/]+=*( [^\s]{1,100})?$" },
        { FormatPatternConstants.Sha256Hex, "^[a-f0-9]{64}$" },
        { FormatPatternConstants.HttpEndpoint, "^https?://[A-Za-z0-9.-]+(?::[0-9]{1,5})?$" },
        { FormatPatternConstants.DockerNetwork, "^[A-Za-z0-9][A-Za-z0-9_.-]{0,62}$" },
        { FormatPatternConstants.RegionCode, "^[a-z0-9][a-z0-9-]{0,31}$" },
        { FormatPatternConstants.RequestPath, "^/[A-Za-z0-9/_?=&.%~-]{0,199}$" },
        { FormatPatternConstants.ConfigurationKey, "^[A-Za-z0-9][A-Za-z0-9._:-]{0,255}$" },
    };

    /// <summary>Values that probe every pattern's edges; each named rule must agree with its pattern on all of them.</summary>
    private static readonly string[] Probes =
    [
        "", "a", "ab", "a-", "-a", "1a", "a--b", "billing-api", "Billing", new string('a', 48), new string('a', 49),
        "owner/repo", "Owner-1/my.repo_x", "-owner/repo", "owner/", "owner/repo/extra", new string('o', 40) + "/r",
        "main", "feature/x.y", "/main", "app-linux-x64.tar.gz", ".hidden", "ci.yml", "ci.yaml", "ci.json",
        "ghcr.io/owner/product/api", "localhost:5000/app", "GHCR.io/owner/app", "ghcr.io", "ghcr.io/owner/app:1.0",
        "app.example.com", "example", "App.example.com", "a.b", "-a.com", "edge-1", "edge-", "e", "10.0.0.1",
        "203.0.113.10", "256.1.1.1", "01.2.3.4", "1.2.3", "host_name", "deploy", "_svc", "Deploy", "root$",
        "SHA256:" + new string('A', 43), "SHA256:" + new string('A', 43) + "=", "SHA256:" + new string('A', 42),
        "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAA user@host", "ssh-ed25519 AAAA", "ssh-dss AAAA", "ssh-rsa AAAA= a b",
        new string('a', 64), new string('A', 64), new string('a', 63),
        "http://vault:8080", "https://ingress", "http://vault:8080/path", "ftp://x", "bridge", "my_net.1", "hel1", "eu-central-1",
        "/health?ready=1", "health", "/a b", "Billing:SecretKey", ":key", "abc\n",
    ];

    public static TheoryData<string, Func<InlineValidator<Probe>, IRuleBuilderOptions<Probe, string>>> NamedRules => new()
    {
        { FormatPatternConstants.GitHubRepository, v => v.RuleFor(p => p.Value).MustBeGitHubRepository() },
        { FormatPatternConstants.GitBranch, v => v.RuleFor(p => p.Value).MustBeGitBranch() },
        { FormatPatternConstants.FileName, v => v.RuleFor(p => p.Value).MustBeFileName() },
        { FormatPatternConstants.WorkflowFileName, v => v.RuleFor(p => p.Value).MustBeWorkflowFileName() },
        { FormatPatternConstants.ImageRepository, v => v.RuleFor(p => p.Value).MustBeImageRepository() },
        { FormatPatternConstants.DomainName, v => v.RuleFor(p => p.Value).MustBeDomainName() },
        { FormatPatternConstants.HostLabel, v => v.RuleFor(p => p.Value).MustBeHostLabel() },
        { FormatPatternConstants.NetworkHost, v => v.RuleFor(p => p.Value).MustBeNetworkHost() },
        { FormatPatternConstants.UnixUserName, v => v.RuleFor(p => p.Value).MustBeUnixUserName() },
        { FormatPatternConstants.Ipv4Address, v => v.RuleFor(p => p.Value).MustBeIpv4Address() },
        { FormatPatternConstants.SshKeyFingerprint, v => v.RuleFor(p => p.Value).MustBeSshKeyFingerprint() },
        { FormatPatternConstants.SshPublicKey, v => v.RuleFor(p => p.Value).MustBeSshPublicKey() },
        { FormatPatternConstants.Sha256Hex, v => v.RuleFor(p => p.Value).MustBeSha256Hex() },
        { FormatPatternConstants.HttpEndpoint, v => v.RuleFor(p => p.Value).MustBeHttpEndpoint() },
        { FormatPatternConstants.DockerNetwork, v => v.RuleFor(p => p.Value).MustBeDockerNetwork() },
        { FormatPatternConstants.RegionCode, v => v.RuleFor(p => p.Value).MustBeRegionCode() },
        { FormatPatternConstants.RequestPath, v => v.RuleFor(p => p.Value).MustBeRequestPath() },
        { FormatPatternConstants.ConfigurationKey, v => v.RuleFor(p => p.Value).MustBeConfigurationKey() },
        { FormatPatternConstants.NameSlug, v => v.RuleFor(p => p.Value).MustBeNameSlug() },
        { FormatPatternConstants.GitBranch, v => v.RuleFor(p => p.Value).MustMatch(FormatPatternConstants.GitBranch, "a branch name") },
    };

    [Theory]
    [MemberData(nameof(SourcePatterns))]
    public void Pattern_ShouldBeTheOneTheSourceEnforced(string pattern, string source) => pattern.Should().Be(source);

    [Theory]
    [MemberData(nameof(NamedRules))]
    public void NamedRule_ShouldAcceptExactlyWhatItsPatternAccepts(
        string pattern, Func<InlineValidator<Probe>, IRuleBuilderOptions<Probe, string>> rule)
    {
        var validator = new InlineValidator<Probe>();
        rule(validator);

        foreach (var probe in Probes)
            validator.Validate(new Probe(probe)).IsValid.Should().Be(Regex.IsMatch(probe, pattern), $"'{probe}' against {pattern}");
    }

    [Fact]
    public void NamedRule_ShouldFailNull_AndNameTheShape()
    {
        var validator = new InlineValidator<Probe>();
        validator.RuleFor(p => p.Value).MustBeGitHubRepository();

        var result = validator.Validate(new Probe(null!));

        result.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Be("'Value' must be a GitHub repository as owner/name.");
        result.Errors[0].ErrorCode.Should().Be("PredicateValidator");
    }

    [Fact]
    public void NameSlug_ShouldKeepItsMessageAndCode_AndPassNull()
    {
        var validator = new InlineValidator<Probe>();
        validator.RuleFor(p => p.Value).MustBeNameSlug();

        var failure = validator.Validate(new Probe("1abc")).Errors.Should().ContainSingle().Subject;

        failure.ErrorMessage.Should().Be(
            "'Value' must start with a lowercase letter and hold only lowercase letters, digits and dashes (48 at most).");
        failure.ErrorCode.Should().Be("RegularExpressionValidator");
        validator.Validate(new Probe(null!)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("a--b", true, false)]
    [InlineData("abc-", true, false)]
    [InlineData("1abc", false, true)]
    [InlineData("billing-api", true, true)]
    public void NameSlug_ShouldStayApartFromTheSlugGuard(string value, bool nameSlug, bool guard)
    {
        var guarded = () => Guard.Against.NotSlug(value, nameof(value));

        Regex.IsMatch(value, FormatPatternConstants.NameSlug).Should().Be(nameSlug);
        if (guard)
            guarded.Should().NotThrow();
        else
            guarded.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("  ", true)]
    [InlineData("https://www.example.com/manager/", true)]
    [InlineData(" https://example.com ", true)]
    [InlineData("http://example.com", false)]
    [InlineData("https://user:pass@example.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/relative", false)]
    public void MustBeHttpsAddress_ShouldTakeNothingOrAnHttpsAddressWithoutCredentials(string? value, bool valid)
    {
        var validator = new InlineValidator<OptionalProbe>();
        validator.RuleFor(p => p.Value).MustBeHttpsAddress("https://www.example.com/manager/");

        var result = validator.Validate(new OptionalProbe(value));

        result.IsValid.Should().Be(valid);
        if (!valid)
            result.Errors[0].ErrorMessage.Should().Be("'Value' must be an https address, such as https://www.example.com/manager/.");
        FormatRuleExtensions.IsHttpsAddressOrEmpty("https://x." + new string('a', 2048)).Should().BeFalse();
    }

    /// <summary>A validated value.</summary>
    public sealed record Probe
    {
        /// <summary>Initializes a probe holding <paramref name="value"/>.</summary>
        /// <param name="value">The value under test.</param>
        public Probe(string value) => Value = value;

        /// <summary>The value under test.</summary>
        public string Value { get; init; }
    }

    /// <summary>A validated optional value.</summary>
    public sealed record OptionalProbe
    {
        /// <summary>Initializes a probe holding <paramref name="value"/>.</summary>
        /// <param name="value">The value under test.</param>
        public OptionalProbe(string? value) => Value = value;

        /// <summary>The value under test.</summary>
        public string? Value { get; init; }
    }
}
