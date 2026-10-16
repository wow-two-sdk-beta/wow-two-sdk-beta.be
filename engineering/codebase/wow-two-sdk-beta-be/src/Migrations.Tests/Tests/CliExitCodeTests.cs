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
    public async Task Rollback_ShouldExitTwo_WhenTargetGuardRefuses()
    {
        Directory.CreateDirectory(_root);
        var result = await CliProcess.RunAsync(
            "rollback",
            "--connection", "Host=localhost;Database=guarded;Username=test;Password=test",
            "--sql-dir", _root,
            "--i-understand-this-is", "wrong",
            "--force");

        result.ExitCode.Should().Be(2, result.StandardError);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
