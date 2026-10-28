using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Verbs;

/// <summary>Represents one in-process run of an image's migrate command: its exit, both streams and the parsed <c>migrate-result</c> line.</summary>
/// <param name="Args">The arguments after <c>migrate</c>.</param>
/// <param name="Exit">The exit code.</param>
/// <param name="Output">Everything written to stdout; the result line is its last line.</param>
/// <param name="Error">Everything written to stderr: the human report and the host's logs.</param>
/// <param name="Result">The parsed result line.</param>
public sealed record MigrateVerbRun(IReadOnlyList<string> Args, int Exit, string Output, string Error, MigrateRunResult Result)
{
    /// <summary>Gets whether the run changed nothing: no set applied, removed, repaired or seeded anything.</summary>
    public bool ChangedNothing => Result.Sets.All(set =>
        set.Applied.Count == 0 && set.Removed.Count == 0 && set.Repaired.Count == 0 && set.Seeds.Count == 0);

    /// <inheritdoc />
    public override string ToString() => $"migrate {string.Join(' ', Args)} → exit {Exit}{Environment.NewLine}{Output}{Error}";
}
