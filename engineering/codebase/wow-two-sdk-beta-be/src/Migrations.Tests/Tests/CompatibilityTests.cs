using AwesomeAssertions;
using Dapper;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>An image at level 32 on a database at 33 runs when 033 is an expand or a contract its release has reached, on boot and on apply.</summary>
[Collection(PostgresMigrationCollection.Name)]
public sealed class CompatibilityTests(MigratorPostgresFixture fixture) : EngineTestBase(fixture)
{
    private const string Older = "0.5.0";
    private const string Newer = "0.6.0";

    [Fact]
    public async Task OlderImage_ShouldBootAndApply_WhenTheNewerMigrationIsAnExpand()
    {
        await ApplyNewerAsync("create table t33(id integer primary key);");
        Workspace.DeleteFolder("033-newer");

        await using var older = CreateDefaultHost(Older);
        await older.Services.VerifyMigrationLevelsOnStartupAsync();

        older.Logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("survives"));
        var check = await Engine(older).CheckAsync();
        check.Exit.Should().Be(0);
        check.Sets.Single().State.Should().Be(MigrationLevelState.AheadCompatible);
        var apply = await Engine(older).ApplyAsync();
        apply.Exit.Should().Be(0);
        apply.Sets.Single().Status.Should().Be(MigrateOutcome.Current);
    }

    [Fact]
    public async Task OlderImage_ShouldRefuseBootAndApply_WhenTheNewerMigrationIsAContractAboveItsRelease()
    {
        await ApplyNewerAsync("-- @contract since=v0.6\ndrop table t32;");
        Workspace.DeleteFolder("033-newer");

        await using var older = CreateDefaultHost(Older);
        var boot = () => older.Services.VerifyMigrationLevelsOnStartupAsync();

        (await boot.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*033-newer*");
        var check = await Engine(older).CheckAsync();
        check.Exit.Should().Be(1);
        check.Sets.Single().State.Should().Be(MigrationLevelState.AheadIncompatible);
        check.Sets.Single().IncompatibleAhead.Should().Equal("033-newer");
        var apply = await older.Sets.GetRunner(MigrationConstants.DefaultSetName).ApplyPendingAsync("deploy");
        apply.IsFailure(out var error, out _).Should().BeTrue();
        error!.Type.Should().Be(AppErrorType.DataIntegrity);
        (await Engine(older).ApplyAsync()).Exit.Should().Be(1);
    }

    [Fact]
    public async Task OlderImage_ShouldRun_WhenTheContractNamesAReleaseItHasReached()
    {
        await ApplyNewerAsync("-- @contract since=v0.5\ndrop table t32;");
        Workspace.DeleteFolder("033-newer");

        await using var older = CreateDefaultHost(Older);
        await older.Services.VerifyMigrationLevelsOnStartupAsync();

        var check = await Engine(older).CheckAsync();
        check.Exit.Should().Be(0);
        check.Sets.Single().RollbackFloor.Should().Be("v0.5");
    }

    [Fact]
    public async Task OlderImage_ShouldRefuse_WhenTheNewerRowHasNoKind()
    {
        await ApplyNewerAsync("create table t33(id integer primary key);");
        await ExecuteAsync("update migration_history set kind = null where ordinal = 33");
        Workspace.DeleteFolder("033-newer");

        await using var older = CreateDefaultHost(Older);

        (await Engine(older).CheckAsync()).Sets.Single().State.Should().Be(MigrationLevelState.AheadIncompatible);
        (await Engine(older).ApplyAsync()).Exit.Should().Be(1);
    }

    [Fact]
    public async Task Apply_ShouldRecordKindAndSince_AndReportTheRollbackFloor()
    {
        await ApplyNewerAsync("-- @contract since=v0.6\ndrop table t32;");

        var rows = (await QueryAsync<(int Ordinal, string? Kind, string? Since)>(
            "select ordinal, kind, contract_since from migration_history order by ordinal")).ToList();
        rows.Should().Equal((32, "expand", (string?)null), (33, "contract", "v0.6"));

        await using var newer = CreateDefaultHost(Newer);
        (await Engine(newer).CheckAsync()).Sets.Single().RollbackFloor.Should().Be("v0.6");
    }

    [Fact]
    public async Task Apply_ShouldUpgradeAnOlderHistoryTable_AndRecordTheKindsItsFilesCarry()
    {
        Workspace.Write("032-older", "create table t32(id integer primary key);", "drop table t32;");
        Workspace.Write("033-newer", "-- @contract since=v0.4\ncreate table t33(id integer primary key);", "drop table t33;");
        var hasher = new MigrationChecksumHasher();
        await ExecuteAsync(
            """
            create table t32(id integer primary key);
            create table t33(id integer primary key);
            create table migration_history (
                ordinal integer not null, version varchar(20) not null, name varchar(120) not null, checksum char(64) not null,
                applied_at timestamptz not null, applied_by varchar(60) not null, execution_ms integer not null,
                constraint pk_migration_history primary key (ordinal));
            insert into migration_history values (32, 'v1.0', 'older', @first, now(), 'startup', 3), (33, 'v1.0', 'newer', @second, now(), 'startup', 4);
            """,
            new
            {
                first = hasher.Hash(File.ReadAllText(Path.Combine(Workspace.Root, "032-older", "Apply.sql"))),
                second = hasher.Hash(File.ReadAllText(Path.Combine(Workspace.Root, "033-newer", "Apply.sql"))),
            });

        await using var host = CreateDefaultHost(Newer);
        var check = await Engine(host).CheckAsync();
        check.Exit.Should().Be(0, "the read-only check reads a table no apply has upgraded");
        (await ScalarAsync<long>("select count(*) from information_schema.columns where table_name = 'migration_history' and column_name = 'kind'"))
            .Should().Be(0, "check never alters the history");

        (await Engine(host).ApplyAsync()).Exit.Should().Be(0);

        (await ScalarAsync<string>("select data_type from information_schema.columns where table_name = 'migration_history' and column_name = 'execution_ms'"))
            .Should().Be("bigint");
        var rows = (await QueryAsync<(int Ordinal, string? Kind, string? Since)>(
            "select ordinal, kind, contract_since from migration_history order by ordinal")).ToList();
        rows.Should().Equal((32, "expand", (string?)null), (33, "contract", "v0.4"));
    }

    private async Task ApplyNewerAsync(string newerSql)
    {
        Workspace.Write("032-older", "create table t32(id integer primary key);", "drop table t32;");
        Workspace.Write("033-newer", newerSql, "select 1;");
        await using var newer = CreateDefaultHost(Newer);
        (await Engine(newer).ApplyAsync()).Exit.Should().Be(0);
    }

    private async Task<IEnumerable<T>> QueryAsync<T>(string sql)
    {
        await using var connection = await OpenAsync();
        return await connection.QueryAsync<T>(sql);
    }
}
