namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

/// <summary>Represents the step of the idempotency proof that failed.</summary>
/// <param name="Migration">The <c>NNN-name</c> label.</param>
/// <param name="Step">The step: <c>apply</c>, <c>apply → apply</c>, <c>rollback</c>, <c>rollback → rollback</c> or <c>apply → rollback → apply</c>.</param>
/// <param name="Error">The provider's error, or what the rerun changed.</param>
public sealed record MigrationIdempotencyFailure(string Migration, string Step, string Error)
{
    /// <inheritdoc />
    public override string ToString() => $"{Migration} failed {Step}: {Error}";
}
