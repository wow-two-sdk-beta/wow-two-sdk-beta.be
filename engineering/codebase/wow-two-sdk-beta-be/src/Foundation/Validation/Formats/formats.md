# Validation.Formats

*Format rules for infrastructure inventories: names, repositories, images, hosts, addresses, SSH keys and paths.*

Namespace: `WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats`.

## Use

```csharp
public sealed class ServerValidator : AbstractValidator<ServerRequest>
{
    public ServerValidator()
    {
        RuleFor(x => x.Name).MustBeNameSlug();
        RuleFor(x => x.Repository).MustBeGitHubRepository();
        RuleFor(x => x.Image).MustBeImageRepository();
        RuleFor(x => x.Host).MustBeNetworkHost();
        RuleFor(x => x.Address).MustBeIpv4Address();
        RuleFor(x => x.ConsoleUrl).MustBeHttpsAddress("https://console.example.com/");   // optional field
        RuleFor(x => x.Asset).MustMatch(FormatPatternConstants.FileName, "a release asset's file name");  // own wording
    }
}

var ok = Regex.IsMatch(value, FormatPatternConstants.DomainName);   // outside a validator
```

## Rules

| Rule | Pattern | Message shape (`'{PropertyName}' must be …`) |
|---|---|---|
| `MustBeNameSlug` | `NameSlug` | starts with a lowercase letter … (48 at most) — a `Matches` rule |
| `MustBeGitHubRepository` | `GitHubRepository` | a GitHub repository as owner/name |
| `MustBeGitBranch` | `GitBranch` | a branch name |
| `MustBeFileName` | `FileName` | a file name |
| `MustBeWorkflowFileName` | `WorkflowFileName` | a workflow file name ending in .yml or .yaml |
| `MustBeImageRepository` | `ImageRepository` | an image repository such as ghcr.io/owner/product/api |
| `MustBeDomainName` | `DomainName` | a lowercase host name such as app.example.com |
| `MustBeHostLabel` | `HostLabel` | a lowercase host name of one label, such as edge-1 |
| `MustBeNetworkHost` | `NetworkHost` | a host name or address |
| `MustBeUnixUserName` | `UnixUserName` | a lowercase user name |
| `MustBeIpv4Address` | `Ipv4Address` | an IPv4 address such as 203.0.113.10 |
| `MustBeSshKeyFingerprint` | `SshKeyFingerprint` | an SSH key fingerprint as ssh-keygen -l prints it |
| `MustBeSshPublicKey` | `SshPublicKey` | one SSH public key line |
| `MustBeSha256Hex` | `Sha256Hex` | a SHA-256 digest in lowercase hex |
| `MustBeHttpEndpoint` | `HttpEndpoint` | an address such as http://host:8080 |
| `MustBeDockerNetwork` | `DockerNetwork` | a Docker network name |
| `MustBeRegionCode` | `RegionCode` | a region such as hel1 |
| `MustBeRequestPath` | `RequestPath` | a path starting with / |
| `MustBeConfigurationKey` | `ConfigurationKey` | a configuration key such as Billing:SecretKey |
| `MustBeHttpsAddress(example)` | — | an https address, such as {example} — empty passes; no credentials, 2048 at most |
| `MustMatch(pattern, shape)` | yours | {shape} |

- A named rule fails `null` and reports `PredicateValidator`, so a client renders the server's message.
- `MustBeNameSlug` passes `null` and reports `RegularExpressionValidator`, which a client maps to `pattern`.
- Rename a shape with `MustMatch` and the same pattern: the accepted values stay the same.
- Each pattern is anchored and case-sensitive; .NET's `$` also matches before one trailing `\n`.
- `NameSlug` is not the `NotSlug` guard's slug: it must start with a letter, caps at 48, and allows `a--b` and `abc-`.
- Patterns check shape, not meaning: `Ipv4Address` takes a private address, `DomainName` an unregistered one.

## See also

- [validation.md](../validation.md) — registration, failure codes and advisories.
- [guards.md](../../Guards/guards.md) — `NotSlug`, the kebab-case slug guard.
