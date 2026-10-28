namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

/// <summary>Represents what the idempotency proof ran on one set and where it first failed.</summary>
/// <param name="Set">The set.</param>
/// <param name="Proven">The migrations that passed every step, in apply order.</param>
/// <param name="Failure">The first failed step, or null when every migration is idempotent.</param>
public sealed record MigrationIdempotencyReport(string Set, IReadOnlyList<string> Proven, MigrationIdempotencyFailure? Failure)
{
    /// <summary>Gets whether every migration passed every step.</summary>
    public bool Passed => Failure is null;

    /// <inheritdoc />
    public override string ToString() => Failure is { } failure
        ? $"Set '{Set}' is not idempotent: {failure}"
        : $"Set '{Set}' is idempotent: {string.Join(", ", Proven)}";
}
