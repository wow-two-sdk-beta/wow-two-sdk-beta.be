using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents an owner release by its numeric parts, so a contract's <c>since</c> compares with the release an image carries.</summary>
/// <remarks>
///   - parses <c>v0.5</c>, <c>0.5.13</c>, <c>10.0.88-beta</c> and <c>0.5.13+abc</c> alike: a leading <c>v</c>, a prerelease and build metadata are ignored
///   - missing parts count as zero, so <c>v0.5</c> equals <c>0.5.0</c>
/// </remarks>
internal sealed class MigrationRelease : IComparable<MigrationRelease>
{
    private readonly int[] _parts;

    private MigrationRelease(int[] parts, string text)
    {
        _parts = parts;
        Text = text;
    }

    /// <summary>Gets the release as written.</summary>
    public string Text { get; }

    /// <summary>Parses a release such as <c>v0.5</c> or <c>0.5.13+abc</c>.</summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="release">The parsed release, or null when the text is not a release.</param>
    /// <returns>Whether the text is a release.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out MigrationRelease? release)
    {
        release = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var core = text.Trim();
        if (core.StartsWith('v') || core.StartsWith('V'))
            core = core[1..];

        // Build metadata and a prerelease never change which schema a release reads.
        var cut = core.IndexOfAny(['+', '-']);
        if (cut >= 0)
            core = core[..cut];

        var pieces = core.Split('.');
        if (pieces.Length is 0 or > 4)
            return false;

        var parts = new int[4];
        for (var index = 0; index < pieces.Length; index++)
        {
            if (!int.TryParse(pieces[index], NumberStyles.None, CultureInfo.InvariantCulture, out parts[index]))
                return false;
        }

        release = new MigrationRelease(parts, text.Trim());
        return true;
    }

    /// <summary>Reads the release of the assembly that embeds a set: its informational version, else its assembly version.</summary>
    /// <param name="assembly">The assembly that embeds the set.</param>
    /// <returns>The release text with build metadata stripped, or null when the assembly carries no version.</returns>
    public static string? TextOf(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        string? informational;
        try
        {
            informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        }
        catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or TypeLoadException)
        {
            // An assembly loaded in isolation, as the CLI reads one, may not resolve its other attributes' types.
            informational = null;
        }

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString();
    }

    /// <inheritdoc />
    public int CompareTo(MigrationRelease? other)
    {
        if (other is null)
            return 1;

        for (var index = 0; index < _parts.Length; index++)
        {
            var compared = _parts[index].CompareTo(other._parts[index]);
            if (compared != 0)
                return compared;
        }

        return 0;
    }

    /// <inheritdoc />
    public override string ToString() => Text;
}
