namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Lint;

/// <summary>Represents one rule a migration file breaks.</summary>
/// <param name="Set">The set the file belongs to.</param>
/// <param name="Migration">The file's <c>NNN-name</c> label, or the EF entity a mapping finding names.</param>
/// <param name="Rule">The rule, one of <see cref="MigrationLintRules"/>.</param>
/// <param name="Message">What the file does and how to fix or mark it.</param>
public sealed record MigrationLintFinding(string Set, string Migration, string Rule, string Message)
{
    /// <inheritdoc />
    public override string ToString() => $"{Set}/{Migration} [{Rule}] {Message}";
}
