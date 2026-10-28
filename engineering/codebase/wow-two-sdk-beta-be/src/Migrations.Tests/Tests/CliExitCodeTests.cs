using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Migrations.Tests.Harness;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Tests;

/// <summary>Verifies the migration CLI's process-level exit-code contract.</summary>
public sealed class CliExitCodeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "wow2-migration-cli-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task New_ShouldExitZero_WhenInputIsValid()
    {
        Directory.CreateDirectory(_root);
        var result = await CliProcess.RunAsync("new", "sample", "--sql-dir", _root);

        result.ExitCode.Should().Be(0, result.StandardError);
        File.Exists(Path.Combine(_root, "Dev", "sample.sql")).Should().BeTrue();
    }

    [Fact]
    public async Task New_ShouldExitOne_WhenRequiredNameIsMissing()
    {
        Directory.CreateDirectory(_root);
        var result = await CliProcess.RunAsync("new", "--sql-dir", _root);

        result.ExitCode.Should().Be(1, result.StandardError);
    }

    [Fact]
    public async Task Rollback_ShouldExitOne_WhenTheConfirmationNamesAnotherDatabase()
    {
        Directory.CreateDirectory(_root);
        var result = await CliProcess.RunAsync(
            "rollback", "--to", "0",
            "--connection", "Host=localhost;Database=guarded;Username=test;Password=test",
            "--sql-dir", _root,
            "--confirm", "wrong");

        result.ExitCode.Should().Be(1, result.StandardError);
        result.StandardError.Should().Contain("--confirm guarded");
        result.StandardOutput.Should().StartWith("migrate-result ").And.NotContain("Password");
    }

    [Fact]
    public async Task Sets_ShouldDescribeTheSet_WithoutADatabase()
    {
        Directory.CreateDirectory(Path.Combine(_root, "001-users"));
        File.WriteAllText(Path.Combine(_root, "001-users", "Apply.sql"), "create table users(id integer);");
        File.WriteAllText(Path.Combine(_root, "001-users", "Rollback.sql"), "drop table users;");

        var result = await CliProcess.RunAsync(
            "sets", "--json", "--connection", "Host=127.0.0.1;Port=1;Database=nowhere;Username=u;Password=p", "--sql-dir", _root);

        result.ExitCode.Should().Be(0, result.StandardError);
        result.StandardOutput.Should().StartWith("{\"sets\":[{\"name\":\"default\"");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
