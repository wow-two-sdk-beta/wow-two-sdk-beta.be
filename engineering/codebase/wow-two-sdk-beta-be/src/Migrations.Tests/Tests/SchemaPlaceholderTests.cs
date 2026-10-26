using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>The <c>{{schema}}</c> placeholder is substituted per dialect after the checksum is taken.</summary>
public sealed class SchemaPlaceholderTests : SqliteMigratorTestBase
{
    private const string UsersApply = "create table {{schema}}.identity_users(id integer primary key);";

    [Fact]
    public async Task ApplyPending_ShouldDropTheSchemaQualifier_OnSqlite()
    {
        Workspace.Write("001-users", UsersApply, "drop table {{schema}}.identity_users;");

        await using var migrator = CreateMigrator(options => options.SchemaName = "identity");
        (await migrator.Runner.ApplyPendingAsync("test")).ValueOrThrow().Should().Equal("001-users");

        (await migrator.HasTableAsync("identity_users")).Should().BeTrue();
    }

    [Fact]
    public async Task ApplyPending_ShouldRecordTheChecksumOfTheUnsubstitutedScript()
    {
        Workspace.Write("001-users", UsersApply, "drop table {{schema}}.identity_users;");

        await using (var first = CreateMigrator(options => options.SchemaName = "identity_haven"))
            (await first.Runner.ApplyPendingAsync("test")).ValueOrThrow();

        await using var second = CreateMigrator(options => options.SchemaName = "identity_desk");
        (await second.ReadHistoryAsync()).Should().ContainSingle()
            .Which.Checksum.Trim().Should().Be(new MigrationChecksumHasher().Hash(UsersApply));
        (await second.Runner.GetStatusAsync()).ValueOrThrow().Drifted.Should().BeEmpty();
    }

    [Fact]
    public void SubstituteSchema_ShouldUseMain_WhenSqliteSeesABarePlaceholder()
    {
        new SqliteMigrationDialect().SubstituteSchema("pragma {{schema}}.table_info('t'); select '{{schema}}';", "identity")
            .Should().Be("pragma table_info('t'); select 'main';");
    }

    [Fact]
    public void SubstituteSchema_ShouldInsertTheSchemaName_OnPostgres()
    {
        new PostgresMigrationDialect().SubstituteSchema("create table {{schema}}.identity_users(); grant usage on schema {{schema}} to app;", "identity")
            .Should().Be("create table identity.identity_users(); grant usage on schema identity to app;");
    }

    [Fact]
    public async Task ApplyPending_ShouldFailValidation_WhenThePlaceholderMeetsAnUnsafeSchemaName()
    {
        Workspace.Write("001-users", UsersApply, "drop table {{schema}}.identity_users;");

        await using var migrator = CreateMigrator(options => options.SchemaName = "Identity-Users");
        var result = await migrator.Runner.ApplyPendingAsync("test");

        result.IsFailure(out var error, out _).Should().BeTrue();
        error!.Type.Should().Be(AppErrorType.Validation);
        error.Message.Should().Contain("{{schema}}");
        (await migrator.HasTableAsync("identity_users")).Should().BeFalse();
    }
}
