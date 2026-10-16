using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace WoW.Two.Sdk.Beta.Be.Web.SystemStatus;

/// <summary>Represents what build is running: its informational version, the commit it was built from, and when it was built.</summary>
public sealed record BuildVersion
{
    /// <summary>The configuration section whose <c>Version</c>, <c>Commit</c> and <c>Time</c> override the assembly metadata.</summary>
    public const string SectionName = "Build";

    /// <summary>The <see cref="AssemblyMetadataAttribute"/> key read for the build time, an ISO-8601 instant.</summary>
    public const string BuildTimeMetadataKey = "BuildTime";

    /// <summary>The version a build without any version metadata reports.</summary>
    public const string UnknownVersion = "unknown";

    /// <summary>Gets the informational version, such as <c>1.4.2+3f9c2ab</c>; <see cref="UnknownVersion"/> when nothing declares one.</summary>
    public required string Version { get; init; }

    /// <summary>Gets the commit the build came from: the part of <see cref="Version"/> after <c>+</c>, or the configured commit.</summary>
    public string? Commit { get; init; }

    /// <summary>Gets when the build ran, from the <c>BuildTime</c> assembly metadata or the configured time.</summary>
    public DateTimeOffset? BuiltAtUtc { get; init; }

    /// <summary>Reads the build version of <paramref name="assembly"/>, letting the <c>Build</c> configuration section override each value.</summary>
    /// <param name="assembly">The assembly whose informational version and <c>BuildTime</c> metadata are read, usually the host's entry assembly.</param>
    /// <param name="configuration">The configuration whose <c>Build:Version</c>, <c>Build:Commit</c> and <c>Build:Time</c> win when set; <see langword="null"/> reads the assembly alone.</param>
    /// <remarks>A configured version that carries <c>+commit</c> also supplies the commit, unless <c>Build:Commit</c> is set.</remarks>
    public static BuildVersion Read(Assembly assembly, IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var section = configuration?.GetSection(SectionName);
        var version = NonBlank(section?["Version"])
            ?? NonBlank(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
            ?? NonBlank(assembly.GetName().Version?.ToString())
            ?? UnknownVersion;
        var commit = NonBlank(section?["Commit"]) ?? CommitOf(version);
        var builtAt = ParseInstant(section?["Time"])
            ?? ParseInstant(assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .LastOrDefault(attribute => string.Equals(attribute.Key, BuildTimeMetadataKey, StringComparison.Ordinal))?.Value);

        return new BuildVersion { Version = version, Commit = commit, BuiltAtUtc = builtAt };
    }

    private static string? CommitOf(string version)
    {
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? NonBlank(version[(plus + 1)..]) : null;
    }

    private static DateTimeOffset? ParseInstant(string? value)
    {
        return DateTimeOffset.TryParse(
            value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant)
            ? instant
            : null;
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
