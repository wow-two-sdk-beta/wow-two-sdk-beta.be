using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>The harness's idempotency mode: Apply → Apply, Rollback → Rollback and Apply → Rollback → Apply, history bypassed (M59).</summary>
[Collection(PostgresMigrationCollection.Name)]
public sealed class MigratorIdempotencyTests(MigratorPostgresFixture fixture) : MigratorTestBase(fixture)
{
    [Fact]
    public async Task IdempotentScripts_ShouldPassEveryStep_AndTouchNoHistory()
    {
        Workspace.Write("001-plans", "create table if not exists plans(code text primary key, name text not null);", "drop table if exists plans;");
        Workspace.Write("002-free", "insert into plans(code, name) values ('free', 'Free') on conflict (code) do update set name = excluded.name;", "delete from plans where code = 'free';");
        Workspace.Write("003-index",
            "-- @no-transaction\ndrop index concurrently if exists ix_plans_name;\ncreate index concurrently ix_plans_name on plans(name);",
            "drop index if exists ix_plans_name;");
        await using var migrator = CreateMigrator();

        var report = await migrator.CheckIdempotencyAsync();

        report.Passed.Should().BeTrue(report.ToString());
        report.Proven.Should().Equal("001-plans", "002-free", "003-index");
        (await migrator.HasTableAsync("plans")).Should().BeTrue();
        (await migrator.HasTableAsync("migration_history")).Should().BeFalse("the proof bypasses history");
        await Fixture.ResetAsync();
        await migrator.AssertIdempotentAsync();
    }

    [Fact]
    public async Task ApplyThatFailsOnASecondRun_ShouldFailApplyTwice()
    {
        Workspace.Write("001-plans", "create table plans(code text primary key);", "drop table if exists plans;");
        await using var migrator = CreateMigrator();

        var report = await migrator.CheckIdempotencyAsync();

        report.Failure!.Should().Match<MigrationIdempotencyFailure>(failure =>
            failure.Migration == "001-plans" && failure.Step == "apply → apply" && failure.Error.Contains("42P07"));
        await Fixture.ResetAsync();
        var assert = () => migrator.AssertIdempotentAsync();
        await assert.Should().ThrowAsync<MigrationIdempotencyException>().WithMessage("*001-plans failed apply → apply*");
    }

    [Fact]
    public async Task ApplyThatChangesDataOnASecondRun_ShouldFailApplyTwice()
    {
        Workspace.Write("001-events", "create table if not exists events(name text); insert into events(name) values ('created');", "drop table if exists events;");
        await using var migrator = CreateMigrator();

        var report = await migrator.CheckIdempotencyAsync();

        report.Failure!.Step.Should().Be("apply → apply");
        report.Failure.Error.Should().Contain("changed the database").And.Contain("events");
    }

    [Fact]
    public async Task RollbackThatFailsOnASecondRun_ShouldFailRollbackTwice()
    {
        Workspace.Write("001-plans", "create table if not exists plans(code text primary key);", "drop table plans;");
        await using var migrator = CreateMigrator();

        var report = await migrator.CheckIdempotencyAsync();

        report.Failure!.Should().Match<MigrationIdempotencyFailure>(failure =>
            failure.Migration == "001-plans" && failure.Step == "rollback → rollback" && failure.Error.Contains("42P01"));
    }

    [Fact]
    public async Task RollbackThatRenames_ShouldFailItsSecondRun()
    {
        Workspace.Write("001-plans", "create table if not exists plans(code text primary key);", "drop table if exists plans;");
        Workspace.Write("002-name", "alter table plans add column if not exists name text;", "alter table plans rename column name to old_name;");
        await using var migrator = CreateMigrator();

        var report = await migrator.CheckIdempotencyAsync();

        report.Failure!.Should().Match<MigrationIdempotencyFailure>(failure => failure.Migration == "002-name" && failure.Step == "rollback → rollback");
    }
}
