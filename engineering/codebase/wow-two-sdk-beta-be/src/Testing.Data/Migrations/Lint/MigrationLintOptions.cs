using Microsoft.EntityFrameworkCore.Metadata;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Lint;

/// <summary>Holds what one lint run checks: the cutoffs per set, the rule groups, tables a dependency publishes outside the provider, and EF models.</summary>
public sealed class MigrationLintOptions
{
    /// <summary>Gets the sets to lint; empty lints every registered set.</summary>
    public ISet<string> Sets { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Gets the per-set cutoffs of the expand-only rule: files above the ordinal must expand; a set absent here is not expand-linted.</summary>
    public IDictionary<string, int> ExpandOnlyCutoffs { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Gets the per-set ordinals the lock-risk and published-table rules start above; a set absent here is linted from its first file.</summary>
    public IDictionary<string, int> LintCutoffs { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Gets or sets whether the lock-risk rules run: indexes, constraints, column types and volatile defaults on existing tables.</summary>
    public bool LockRisk { get; set; } = true;

    /// <summary>Gets or sets whether keys and mappings onto another set's tables are checked against what it publishes.</summary>
    public bool PublishedTables { get; set; } = true;

    /// <summary>Gets the tables sets publish that the provider does not register, by set name.</summary>
    public IDictionary<string, MigrationLintPublishedSet> Published { get; } = new Dictionary<string, MigrationLintPublishedSet>(StringComparer.Ordinal);

    /// <summary>Gets the EF models whose mappings onto another set's tables are checked, each with the set it belongs to.</summary>
    public IList<(string Set, IModel Model)> Models { get; } = new List<(string Set, IModel Model)>();

    /// <summary>Makes a set expand-only above an ordinal: a file above it that drops, renames, narrows or deletes needs a contract marker.</summary>
    /// <param name="set">The set name.</param>
    /// <param name="cutoff">The last ordinal written before the rule.</param>
    /// <returns>These options, for chaining.</returns>
    public MigrationLintOptions ExpandOnlyAfter(string set, int cutoff)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(set);
        ArgumentOutOfRangeException.ThrowIfNegative(cutoff);

        ExpandOnlyCutoffs[set] = cutoff;
        return this;
    }

    /// <summary>Starts the lock-risk and published-table rules above an ordinal, so files already applied everywhere stay as they are.</summary>
    /// <param name="set">The set name.</param>
    /// <param name="cutoff">The last ordinal the rules skip.</param>
    /// <returns>These options, for chaining.</returns>
    public MigrationLintOptions LintAfter(string set, int cutoff)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(set);
        ArgumentOutOfRangeException.ThrowIfNegative(cutoff);

        LintCutoffs[set] = cutoff;
        return this;
    }

    /// <summary>Declares a dependency's published tables when the provider does not register its set, such as a service pulled as an image.</summary>
    /// <param name="set">The dependency's set name, such as <c>identity</c>.</param>
    /// <param name="schema">The dependency's schema.</param>
    /// <param name="tables">The tables it publishes.</param>
    /// <returns>These options, for chaining.</returns>
    public MigrationLintOptions Publish(string set, string schema, params MigrationReferenceableTable[] tables)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(set);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        Published[set] = new MigrationLintPublishedSet(schema, tables);
        return this;
    }

    /// <summary>Checks an EF model's mappings: an entity mapped into another set's schema must map a table it publishes, reading only its keys and readable columns.</summary>
    /// <param name="set">The set the model belongs to.</param>
    /// <param name="model">The model, such as <c>context.Model</c>.</param>
    /// <returns>These options, for chaining.</returns>
    public MigrationLintOptions CheckModel(string set, IModel model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(set);
        ArgumentNullException.ThrowIfNull(model);

        Models.Add((set, model));
        return this;
    }
}

/// <summary>Represents the tables a set publishes, declared to the lint when the provider does not register the set.</summary>
/// <param name="Schema">The set's schema.</param>
/// <param name="Tables">The published tables.</param>
public sealed record MigrationLintPublishedSet(string Schema, IReadOnlyList<MigrationReferenceableTable> Tables);
