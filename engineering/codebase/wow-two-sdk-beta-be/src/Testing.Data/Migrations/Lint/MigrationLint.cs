using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke.Parsers;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Lint;

/// <summary>Lints a repository's migration sets in its migrations tier: expand-only from a cutoff, lock-risk rules, and keys or mappings onto unpublished tables (M22, M79).</summary>
/// <remarks>
///   - reads the sets a provider registers — sources, headers, dependencies, published tables — and never connects
///   - expand-only: a file above <see cref="MigrationLintOptions.ExpandOnlyAfter"/> that drops, renames, narrows a type, sets <c>NOT NULL</c>, truncates
///     or deletes needs <c>-- @contract since=vX.Y</c>, or the legacy <c>-- contract:</c> line until Wheelhouse converts; dropping an index or constraint
///     the file re-creates is exempt
///   - lock risk, on a table the file did not create: <c>CREATE INDEX</c> without <c>CONCURRENTLY</c>, a foreign key or check without <c>NOT VALID</c>,
///     <c>ALTER COLUMN … TYPE</c>, <c>ADD COLUMN</c> with a volatile default
///   - published tables: <c>REFERENCES</c> into another set's schema, or an EF mapping onto it, names a table that set publishes, through <c>DependsOn</c>
///   - <c>-- lint-allow: &lt;rule&gt; &lt;reason&gt;</c> suppresses one rule for its file; the reason is required
///   - matches statements with comments stripped; dynamic SQL inside a <c>DO</c> block is read as written
/// </remarks>
/// <example>
/// <code>
/// await using var migrator = MigratorHarness.CreatePostgres(fixture.ConnectionString, typeof(AppDbContext).Assembly);
/// await MigrationLint.AssertCleanAsync(migrator.Services, lint => lint.ExpandOnlyAfter("default", 30));
/// </code>
/// </example>
public static partial class MigrationLint
{
    /// <summary>Lints every registered set, or the sets <see cref="MigrationLintOptions.Sets"/> names.</summary>
    /// <param name="services">A provider that registers the sets, such as a <see cref="MigratorHarness"/>'s.</param>
    /// <param name="configure">Sets cutoffs, rule groups, declared published tables and EF models.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>Every finding, ordered by set and file.</returns>
    public static async Task<IReadOnlyList<MigrationLintFinding>> CheckAsync(
        IServiceProvider services, Action<MigrationLintOptions>? configure = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new MigrationLintOptions();
        configure?.Invoke(options);
        var registry = services.GetRequiredService<IMigrationSetRegistry>();
        var engine = services.GetRequiredService<IMigrationEngine>();
        var context = new LintContext(registry, options, services.GetService<ISqlStatementParser>() ?? new SqlStatementParser());

        var findings = new List<MigrationLintFinding>();
        foreach (var set in registry.Names.Where(name => options.Sets.Count == 0 || options.Sets.Contains(name)))
        {
            var description = await engine.DescribeAsync([set], ct).ConfigureAwait(false);
            if (description.Result.Exit != 0 || registry.GetBroker(set).Read().IsFailure(out _, out var source))
            {
                var reason = (description.Result.Sets.Count > 0 ? description.Result.Sets[0].Message : null) ?? description.Result.Message ?? "the source does not read";
                findings.Add(new MigrationLintFinding(set, "*", MigrationLintRules.Source, reason));
                continue;
            }

            var contracts = description.Sets.Single().Contracts.Select(contract => contract.Label).ToHashSet(StringComparer.Ordinal);
            foreach (var migration in source.OrderBy(migration => migration.Name, StringComparer.Ordinal))
            {
                var match = MigrationConstants.FolderPattern().Match(migration.Name);
                var ordinal = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var label = $"{ordinal:D3}-{match.Groups[2].Value}";
                findings.AddRange(context.CheckFile(set, label, ordinal, migration.ApplySql, contracts.Contains(label)));
            }
        }

        foreach (var (set, model) in options.Models)
            findings.AddRange(context.CheckModel(set, model));

        return findings;
    }

    /// <summary>Lints like <see cref="CheckAsync"/> and throws when any rule is broken.</summary>
    /// <param name="services">A provider that registers the sets.</param>
    /// <param name="configure">Sets cutoffs, rule groups, declared published tables and EF models.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <exception cref="MigrationLintException">A finding, listed one per line.</exception>
    public static async Task AssertCleanAsync(IServiceProvider services, Action<MigrationLintOptions>? configure = null, CancellationToken ct = default)
    {
        var findings = await CheckAsync(services, configure, ct).ConfigureAwait(false);
        if (findings.Count > 0)
            throw new MigrationLintException($"Migration lint found {findings.Count}:{Environment.NewLine}{string.Join(Environment.NewLine, findings)}");
    }

    /// <summary>Normalizes a table, index or constraint name: the last segment, unquoted, lowercase.</summary>
    private static string NameOf(string raw)
    {
        var name = raw.Trim().TrimEnd(';', ',');
        var dot = name.LastIndexOf('.');
        return (dot >= 0 ? name[(dot + 1)..] : name).Trim('"').ToLowerInvariant();
    }

    /// <summary>Reads the qualifier of a table reference: <c>{{schema:set}}</c>, <c>{{schema}}</c>, a schema name, or null when unqualified.</summary>
    private static string? QualifierOf(string raw)
    {
        var name = raw.Trim();
        var dot = name.LastIndexOf('.');
        return dot < 0 ? null : name[..dot].Trim('"').ToLowerInvariant();
    }

    private static string StripComments(string statement) => LineComment().Replace(BlockComment().Replace(statement, " "), " ").Trim();

    [GeneratedRegex(@"\bDROP\s+(TABLE|COLUMN|TYPE|CONSTRAINT|INDEX|VIEW|SCHEMA)\b|\bRENAME\b|\bALTER\s+COLUMN\s+\S+\s+(SET\s+DATA\s+)?TYPE\b|\bSET\s+NOT\s+NULL\b|\bTRUNCATE\b|\bDELETE\s+FROM\b", RegexOptions.IgnoreCase)]
    private static partial Regex Contracting();

    [GeneratedRegex(@"^\s*DROP\s+INDEX\s+(?:CONCURRENTLY\s+)?IF\s+EXISTS\s+(?<name>[^\s;]+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DropIndexIfExists();

    [GeneratedRegex(@"\bDROP\s+CONSTRAINT\s+IF\s+EXISTS\s+(?<name>[^\s,;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DropConstraintIfExists();

    [GeneratedRegex(@"\bCREATE\s+(?:(?:GLOBAL|LOCAL)\s+)?(?:(?:TEMP|TEMPORARY|UNLOGGED)\s+)?TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<name>[^\s(]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateTable();

    [GeneratedRegex(@"\bCREATE\s+(?:UNIQUE\s+)?INDEX\s+(?<concurrently>CONCURRENTLY\s+)?(?:IF\s+NOT\s+EXISTS\s+)?(?:(?<index>[^\s(]+)\s+)?ON\s+(?:ONLY\s+)?(?<table>[^\s(]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateIndex();

    [GeneratedRegex(@"^\s*ALTER\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?:ONLY\s+)?(?<table>[^\s(]+)(?<body>[\s\S]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex AlterTable();

    [GeneratedRegex(@"\bADD\s+CONSTRAINT\s+(?<name>[^\s(]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AddNamedConstraint();

    [GeneratedRegex(@"\bADD\s+(?:CONSTRAINT\s+[^\s(]+\s+)?(?:FOREIGN\s+KEY|CHECK)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AddForeignKeyOrCheck();

    [GeneratedRegex(@"\bNOT\s+VALID\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotValid();

    [GeneratedRegex(@"\bALTER\s+(?:COLUMN\s+)?(?<column>[^\s,]+)\s+(?:SET\s+DATA\s+)?TYPE\b", RegexOptions.IgnoreCase)]
    private static partial Regex AlterColumnType();

    [GeneratedRegex(@"\bADD\s+(?:COLUMN\s+)?(?:IF\s+NOT\s+EXISTS\s+)?(?<column>[^\s,]+)[^,]*?\bDEFAULT\s+[^,]*?\b(?<function>random|gen_random_uuid|uuid_generate_v1|uuid_generate_v1mc|uuid_generate_v4|uuidv4|uuidv7|clock_timestamp|timeofday|nextval)\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex VolatileDefault();

    [GeneratedRegex(@"\bREFERENCES\s+(?<target>[^\s(]+)\s*\((?<columns>[^)]*)\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReferencesClause();

    [GeneratedRegex(@"^\s*--\s*contract:", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex LegacyContract();

    [GeneratedRegex(@"^\s*--\s*lint-allow:\s*(?<rule>[A-Za-z-]+)(?<reason>[^\r\n]*)$", RegexOptions.Multiline)]
    private static partial Regex Suppression();

    [GeneratedRegex(@"--[^\r\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"/\*[\s\S]*?\*/")]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"^\{\{schema:(?<set>[a-z][a-z0-9_]*)\}\}$")]
    private static partial Regex SetSchemaQualifier();

    /// <summary>Holds what every file check reads: the registry, the options, the published tables by set and the sets by schema.</summary>
    private sealed class LintContext
    {
        private readonly IMigrationSetRegistry _registry;
        private readonly MigrationLintOptions _options;
        private readonly ISqlStatementParser _parser;
        private readonly Dictionary<string, MigrationLintPublishedSet> _published = new(StringComparer.Ordinal);

        public LintContext(IMigrationSetRegistry registry, MigrationLintOptions options, ISqlStatementParser parser)
        {
            _registry = registry;
            _options = options;
            _parser = parser;
            foreach (var name in registry.Names)
            {
                var setOptions = registry.GetOptions(name);
                _published[name] = new MigrationLintPublishedSet(setOptions.SchemaName, setOptions.ReferenceableTables.ToList());
            }

            foreach (var (name, declared) in options.Published)
                _published[name] = declared;
        }

        /// <summary>Lints one Apply script.</summary>
        public IEnumerable<MigrationLintFinding> CheckFile(string set, string label, int ordinal, string sql, bool isContract)
        {
            var findings = new List<MigrationLintFinding>();
            var allowed = Suppressions(set, label, sql, findings);
            var statements = _parser.Parse(sql).Select(StripComments).Where(statement => statement.Length > 0).ToList();
            var tables = statements.SelectMany(statement => CreateTable().Matches(statement)).Select(match => NameOf(match.Groups["name"].Value)).ToHashSet(StringComparer.Ordinal);
            var indexes = statements.SelectMany(statement => CreateIndex().Matches(statement))
                .Where(match => match.Groups["index"].Success).Select(match => NameOf(match.Groups["index"].Value)).ToHashSet(StringComparer.Ordinal);
            var constraints = statements.SelectMany(statement => AddNamedConstraint().Matches(statement)).Select(match => NameOf(match.Groups["name"].Value)).ToHashSet(StringComparer.Ordinal);

            if (_options.ExpandOnlyCutoffs.TryGetValue(set, out var expandCutoff) && ordinal > expandCutoff && !isContract && !LegacyContract().IsMatch(sql))
            {
                foreach (var statement in statements)
                {
                    if (DropIndexIfExists().Match(statement) is { Success: true } dropIndex && indexes.Contains(NameOf(dropIndex.Groups["name"].Value)))
                        continue;

                    var unrecreated = DropConstraintIfExists().Replace(statement, match => constraints.Contains(NameOf(match.Groups["name"].Value)) ? " " : match.Value);
                    if (Contracting().Match(unrecreated) is { Success: true } contracting)
                    {
                        findings.Add(new MigrationLintFinding(set, label, MigrationLintRules.ExpandOnly,
                            $"'{contracting.Value}' contracts above the cutoff {expandCutoff:D3}; put it in a file of its own marked -- @contract since=vX.Y."));
                    }
                }
            }

            var lintCutoff = _options.LintCutoffs.TryGetValue(set, out var cutoff) ? cutoff : 0;
            if (ordinal > lintCutoff)
            {
                if (_options.LockRisk)
                    findings.AddRange(statements.SelectMany(statement => LockRisks(set, label, statement, tables)));
                if (_options.PublishedTables)
                    findings.AddRange(statements.SelectMany(statement => ReferencesIn(statement).SelectMany(reference => Reference(set, label, reference))));
            }

            return findings.Where(finding => !allowed.Contains(finding.Rule));
        }

        /// <summary>Lints one EF model's mappings onto other sets' schemas.</summary>
        public IEnumerable<MigrationLintFinding> CheckModel(string set, IModel model)
        {
            var own = _registry.Contains(set) ? _registry.GetOptions(set).SchemaName : "public";
            foreach (var entity in model.GetEntityTypes())
            {
                if (entity.GetTableName() is not { } table)
                    continue;

                var schema = (entity.GetSchema() ?? model.GetDefaultSchema() ?? "public").ToLowerInvariant();
                if (schema == own)
                    continue;

                var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
                var columns = entity.GetProperties().Select(property => property.GetColumnName(store)).OfType<string>().ToList();
                foreach (var finding in Published(set, entity.DisplayName(), schema, table, columns, reading: true))
                    yield return finding;
            }
        }

        private static IEnumerable<MigrationLintFinding> LockRisks(string set, string label, string statement, HashSet<string> created)
        {
            if (CreateIndex().Match(statement) is { Success: true } index
                && !index.Groups["concurrently"].Success
                && !created.Contains(NameOf(index.Groups["table"].Value)))
            {
                yield return new MigrationLintFinding(set, label, MigrationLintRules.IndexConcurrently,
                    $"CREATE INDEX on the existing table {NameOf(index.Groups["table"].Value)} blocks its writes; build it CONCURRENTLY in a -- @no-transaction file.");
            }

            if (AlterTable().Match(statement) is not { Success: true } alter || created.Contains(NameOf(alter.Groups["table"].Value)))
                yield break;

            var table = NameOf(alter.Groups["table"].Value);
            var body = alter.Groups["body"].Value;
            if (AddForeignKeyOrCheck().IsMatch(body) && !NotValid().IsMatch(body))
            {
                yield return new MigrationLintFinding(set, label, MigrationLintRules.ConstraintNotValid,
                    $"a foreign key or check on the existing table {table} scans it under lock; add it NOT VALID, then VALIDATE CONSTRAINT in a later statement.");
            }

            if (AlterColumnType().Match(body) is { Success: true } retyped)
            {
                yield return new MigrationLintFinding(set, label, MigrationLintRules.AlterColumnType,
                    $"ALTER COLUMN {NameOf(retyped.Groups["column"].Value)} TYPE on the existing table {table} may rewrite it under an exclusive lock; add a column and backfill instead.");
            }

            if (VolatileDefault().Match(body) is { Success: true } volatileDefault)
            {
                yield return new MigrationLintFinding(set, label, MigrationLintRules.VolatileDefault,
                    $"ADD COLUMN {NameOf(volatileDefault.Groups["column"].Value)} with the volatile default {volatileDefault.Groups["function"].Value}() rewrites the existing table {table}; add it without a default, then backfill.");
            }
        }

        private static IEnumerable<(string Target, IReadOnlyList<string> Columns)> ReferencesIn(string statement) =>
            ReferencesClause().Matches(statement).Select(match => (
                match.Groups["target"].Value,
                (IReadOnlyList<string>)match.Groups["columns"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(NameOf).ToList()));

        private IEnumerable<MigrationLintFinding> Reference(string set, string label, (string Target, IReadOnlyList<string> Columns) reference)
        {
            var qualifier = QualifierOf(reference.Target);
            var own = _registry.GetOptions(set).SchemaName;
            if (qualifier is null || qualifier == MigrationConstants.SchemaPlaceholder || qualifier == own)
                return [];

            var table = NameOf(reference.Target);
            return SetSchemaQualifier().Match(qualifier) is { Success: true } named
                ? PublishedBy(set, label, named.Groups["set"].Value, table, reference.Columns)
                : Published(set, label, qualifier, table, reference.Columns, reading: false);
        }

        /// <summary>Checks a key or mapping onto a table in another schema, finding the set that owns the schema.</summary>
        private IEnumerable<MigrationLintFinding> Published(string set, string subject, string schema, string table, IReadOnlyList<string> columns, bool reading)
        {
            var owners = _published.Where(pair => pair.Key != set && string.Equals(pair.Value.Schema, schema, StringComparison.Ordinal)).Select(pair => pair.Key).ToList();
            if (owners.Count == 0)
            {
                return [new MigrationLintFinding(set, subject, MigrationLintRules.PublishedTables,
                    $"{(reading ? "maps" : "references")} {schema}.{table}, a schema no registered or declared set owns; register the owner's set or declare it with Publish.")];
            }

            var owner = owners.FirstOrDefault(name => _published[name].Tables.Any(published => published.Table == table)) ?? owners[0];
            return PublishedBy(set, subject, owner, table, columns, reading);
        }

        /// <summary>Checks a key or mapping onto one set's table: a declared dependency, a published table, keys or readable columns only.</summary>
        private IEnumerable<MigrationLintFinding> PublishedBy(string set, string subject, string owner, string table, IReadOnlyList<string> columns, bool reading = false)
        {
            if (_registry.Contains(set) && _registry.GetOptions(set).Dependencies.All(dependency => dependency.Set != owner))
            {
                yield return new MigrationLintFinding(set, subject, MigrationLintRules.PublishedTables,
                    $"uses set {owner}'s table {table} without declaring DependsOn(\"{owner}\", level).");
            }

            if (!_published.TryGetValue(owner, out var published) || published.Tables.FirstOrDefault(item => item.Table == table) is not { } listed)
            {
                yield return new MigrationLintFinding(set, subject, MigrationLintRules.PublishedTables,
                    $"uses {owner}'s table {table}, which {owner} does not publish (AddReferenceableTable).");
                yield break;
            }

            var allowed = reading ? listed.Keys.Concat(listed.ReadableColumns).ToHashSet(StringComparer.Ordinal) : listed.Keys.ToHashSet(StringComparer.Ordinal);
            var unlisted = columns.Where(column => !allowed.Contains(column)).ToList();
            if (unlisted.Count > 0)
            {
                yield return new MigrationLintFinding(set, subject, MigrationLintRules.PublishedTables,
                    $"{(reading ? "reads" : "keys into")} {owner}'s {table}.{string.Join(", ", unlisted)}, which {owner} does not publish as {(reading ? "a key or readable column" : "a key")}.");
            }
        }

        /// <summary>Reads the file's suppressions; an unknown rule or a missing reason is a finding and suppresses nothing.</summary>
        private static HashSet<string> Suppressions(string set, string label, string sql, List<MigrationLintFinding> findings)
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in Suppression().Matches(sql))
            {
                var rule = match.Groups["rule"].Value.ToLowerInvariant();
                if (!MigrationLintRules.Suppressible.Contains(rule))
                    findings.Add(new MigrationLintFinding(set, label, MigrationLintRules.Suppression, $"lint-allow names the unknown rule '{rule}'."));
                else if (string.IsNullOrWhiteSpace(match.Groups["reason"].Value))
                    findings.Add(new MigrationLintFinding(set, label, MigrationLintRules.Suppression, $"lint-allow: {rule} needs a reason after the rule."));
                else
                    allowed.Add(rule);
            }

            return allowed;
        }
    }
}
