using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Lint;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>The Testing.Data lint: expand-only from a cutoff, lock-risk rules, published tables, and suppressions — read from the sets, no database.</summary>
public sealed class MigrationLintTests : IDisposable
{
    private readonly MigrationsWorkspace _workspace = new();
    private readonly MigrationsWorkspace _identity = new();

    [Fact]
    public async Task Lint_ShouldFlagANonConcurrentIndexOnAnExistingTable_AndPassTheSafeForms()
    {
        _workspace.Write("001-users", "create table users(id integer primary key, email text); create index ix_users_id on users(id);", "drop table users;");
        _workspace.Write("002-email", "create index ix_users_email on users(email);", "drop index ix_users_email;");
        _workspace.Write("003-email-concurrently", "-- @no-transaction\ncreate index concurrently if not exists ix_users_email2 on users(email);", "drop index ix_users_email2;");

        var findings = await LintAsync();

        findings.Should().ContainSingle().Which.Should().Match<MigrationLintFinding>(finding =>
            finding.Migration == "002-email" && finding.Rule == MigrationLintRules.IndexConcurrently);
    }

    [Fact]
    public async Task Lint_ShouldFlagAnUnmarkedDropColumnAboveTheCutoff_AndPassAMarkedContract()
    {
        _workspace.Write("001-users", "create table users(id integer primary key, name text, nick text, alias text);", "drop table users;");
        _workspace.Write("002-drop-name", "alter table users drop column name;", "alter table users add column name text;");
        _workspace.Write("003-drop-nick", "-- @contract since=v0.3\nalter table users drop column nick;", "alter table users add column nick text;");
        _workspace.Write("004-drop-alias", "-- contract: v0.4 drops alias once no release reads it\nalter table users drop column alias;", "alter table users add column alias text;");

        var findings = await LintAsync(lint => lint.ExpandOnlyAfter(MigrationConstants.DefaultSetName, 1));

        findings.Should().ContainSingle().Which.Should().Match<MigrationLintFinding>(finding =>
            finding.Migration == "002-drop-name" && finding.Rule == MigrationLintRules.ExpandOnly && finding.Message.Contains("drop column", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Lint_ShouldHonourTheCutoff_AndExemptADropTheFileRecreates()
    {
        _workspace.Write("001-users", "create table users(id integer primary key, name text, email text);", "drop table users;");
        _workspace.Write("002-drop-name", "alter table users drop column name;", "alter table users add column name text;");
        _workspace.Write("003-recover-index",
            "-- @no-transaction\ndrop index concurrently if exists ix_users_email;\ncreate index concurrently ix_users_email on users(email);",
            "drop index if exists ix_users_email;");
        _workspace.Write("004-readd-check",
            "alter table users drop constraint if exists ck_email; alter table users add constraint ck_email check (email <> '') not valid;",
            "alter table users drop constraint if exists ck_email;");

        (await LintAsync(lint => lint.ExpandOnlyAfter(MigrationConstants.DefaultSetName, 2))).Should().BeEmpty();
        (await LintAsync(lint => lint.ExpandOnlyAfter(MigrationConstants.DefaultSetName, 1))).Select(finding => finding.Migration).Should().Equal("002-drop-name");
    }

    [Theory]
    [InlineData("ALTER TABLE products DROP COLUMN status;", true)]
    [InlineData("DROP TABLE pins;", true)]
    [InlineData("ALTER TABLE targets RENAME COLUMN root TO folder;", true)]
    [InlineData("ALTER TABLE targets ALTER COLUMN root TYPE varchar(20);", true)]
    [InlineData("ALTER TABLE targets ALTER COLUMN root SET NOT NULL;", true)]
    [InlineData("DELETE FROM targets WHERE root IS NULL;", true)]
    [InlineData("ALTER TABLE targets ADD COLUMN notes text NULL;", false)]
    [InlineData("CREATE TABLE IF NOT EXISTS vault_keys (id uuid PRIMARY KEY);", false)]
    [InlineData("-- DROP TABLE pins; only a comment\nALTER TABLE targets ADD COLUMN notes text NULL;", false)]
    public async Task Lint_ShouldTellContractingFromExpanding_AsWheelhouseDid(string sql, bool contracting)
    {
        _workspace.Write("001-change", sql, "select 1;");

        var findings = await LintAsync(lint =>
        {
            lint.LockRisk = false;
            lint.ExpandOnlyAfter(MigrationConstants.DefaultSetName, 0);
        });

        findings.Any(finding => finding.Rule == MigrationLintRules.ExpandOnly).Should().Be(contracting);
    }

    [Theory]
    [InlineData("alter table users add constraint fk_users_team foreign key (team_id) references teams(id);", "constraint-not-valid")]
    [InlineData("alter table users add constraint ck_users_email check (email <> '');", "constraint-not-valid")]
    [InlineData("alter table users alter column email type varchar(320);", "alter-column-type")]
    [InlineData("alter table users add column token uuid not null default gen_random_uuid();", "volatile-default")]
    [InlineData("alter table users add constraint fk_users_team foreign key (team_id) references teams(id) not valid;", null)]
    [InlineData("alter table users add column created_at timestamptz not null default now();", null)]
    [InlineData("create table audit(id uuid primary key default gen_random_uuid(), at timestamptz); create index ix_audit_at on audit(at);", null)]
    public async Task Lint_ShouldTellLockRisksFromSafeChanges(string sql, string? rule)
    {
        _workspace.Write("001-users", "create table teams(id integer primary key); create table users(id integer primary key, email text, team_id integer);", "drop table users; drop table teams;");
        _workspace.Write("002-change", sql, "select 1;");

        var findings = await LintAsync();

        findings.Select(finding => finding.Rule).Should().Equal(rule is null ? [] : [rule]);
    }

    [Fact]
    public async Task Lint_ShouldHonourASuppressionWithAReason_AndFlagOneWithout()
    {
        _workspace.Write("001-users", "create table users(id integer primary key, email text);", "drop table users;");
        _workspace.Write("002-email", "-- lint-allow: index-concurrently users stays empty until v0.6 ships\ncreate index ix_users_email on users(email);", "drop index ix_users_email;");
        _workspace.Write("003-id", "-- lint-allow: index-concurrently\ncreate index ix_users_id on users(id);", "drop index ix_users_id;");

        var findings = await LintAsync();

        findings.Select(finding => (finding.Migration, finding.Rule)).Should().BeEquivalentTo(
        [
            ("003-id", MigrationLintRules.Suppression),
            ("003-id", MigrationLintRules.IndexConcurrently),
        ]);
    }

    [Fact]
    public async Task Lint_ShouldFlagKeysOntoUnpublishedTablesOrColumns_AndUndeclaredDependencies()
    {
        _identity.Write("001-users", "create table {{schema}}.identity_users(id uuid primary key, email text); create table {{schema}}.identity_secrets(id uuid primary key);", "select 1;");
        _workspace.Write("001-orders", "create table orders(id uuid primary key, user_id uuid not null references {{schema:identity}}.identity_users(id));", "drop table orders;");
        _workspace.Write("002-mail", "create table mail(id uuid primary key, email text references identity.identity_users(email));", "drop table mail;");
        _workspace.Write("003-secrets", "create table vault(id uuid primary key, secret_id uuid references identity.identity_secrets(id));", "drop table vault;");

        var findings = await LintAsync(register: services => services.AddDatabaseBespokeMigrationSet("identity", _identity.Root, options =>
        {
            options.SchemaName = "identity";
            options.AddReferenceableTable("identity_users", keys: ["id"], readableColumns: ["email"]);
        }), dependsOnIdentity: true);
        var undeclared = await LintAsync(register: services => services.AddDatabaseBespokeMigrationSet("identity", _identity.Root, options =>
        {
            options.SchemaName = "identity";
            options.AddReferenceableTable("identity_users", keys: ["id"]);
        }), dependsOnIdentity: false);

        findings.Where(finding => finding.Rule == MigrationLintRules.PublishedTables).Select(finding => finding.Migration)
            .Should().Equal("002-mail", "003-secrets");
        undeclared.Should().Contain(finding => finding.Migration == "001-orders" && finding.Message.Contains("DependsOn"));
    }

    [Fact]
    public async Task Lint_ShouldCheckAnEfMappingOntoAnotherSetsTable()
    {
        _identity.Write("001-users", "create table {{schema}}.identity_users(id uuid primary key, display_name text, email text);", "select 1;");
        _workspace.Write("001-orders", "create table orders(id uuid primary key);", "drop table orders;");
        await using var context = new MappingContext(new DbContextOptionsBuilder<MappingContext>().UseNpgsql("Host=localhost;Database=unused").Options);

        var findings = await LintAsync(
            register: services => services.AddDatabaseBespokeMigrationSet("identity", _identity.Root, options =>
            {
                options.SchemaName = "identity";
                options.AddReferenceableTable("identity_users", keys: ["id"], readableColumns: ["display_name"]);
            }),
            dependsOnIdentity: true,
            configure: lint => lint.CheckModel(MigrationConstants.DefaultSetName, context.Model));

        findings.Should().ContainSingle().Which.Message.Should().Contain("identity_users.email");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _workspace.Dispose();
        _identity.Dispose();
    }

    private async Task<IReadOnlyList<MigrationLintFinding>> LintAsync(
        Action<MigrationLintOptions>? configure = null, Action<IServiceCollection>? register = null, bool dependsOnIdentity = false)
    {
        var services = new ServiceCollection().AddLogging();
        register?.Invoke(services);
        services.AddDatabaseBespokeMigrations(_workspace.Root, options =>
        {
            if (dependsOnIdentity)
                options.DependsOn("identity", 1);
        });
        await using var provider = services.BuildServiceProvider();
        return await MigrationLint.CheckAsync(provider, configure);
    }

    /// <summary>Maps Identity's users table read-only, one column past what Identity publishes.</summary>
    /// <param name="options">The context options.</param>
    private sealed class MappingContext(DbContextOptions<MappingContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<UserView>(user =>
        {
            user.ToTable("identity_users", "identity");
            user.Property(view => view.Id).HasColumnName("id");
            user.Property(view => view.DisplayName).HasColumnName("display_name");
            user.Property(view => view.Email).HasColumnName("email");
        });
    }

    /// <summary>A read-only view of an Identity user.</summary>
    private sealed class UserView
    {
        public Guid Id { get; set; }

        public string? DisplayName { get; set; }

        public string? Email { get; set; }
    }
}
