using System.Diagnostics;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Integrations.Processes;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Tests.Processes;

/// <summary>The process client runs real child processes within bounds: JSON out, input in, a scrubbed environment, a time
/// limit that kills the tree, capped output and display-safe failures. Unix shells only; skipped on Windows.</summary>
public sealed class ProcessClientTests : IDisposable
{
    private const string Shell = "/bin/sh";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"processes-{Guid.NewGuid():N}");

    public ProcessClientTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task RunJson_ParsesStandardOutput()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = await Client().RunJsonAsync(Sh("""printf '{"ok":true,"items":[1,2]}'"""));

        JsonElement json = Value(result);
        json.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task StandardInput_ReachesTheProcessAndIsClosed()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = await Client().RunJsonAsync(Sh("cat") with { StandardInput = """{"settings":{"KEY":"value"}}""" });

        Value(result).GetProperty("settings").GetProperty("KEY").GetString().Should().Be("value");
    }

    [Fact]
    public async Task WithoutInput_StandardInputIsClosedSoAReaderEnds()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = await Client(options => options.Timeout = TimeSpan.FromSeconds(5)).RunAsync(Sh("cat; printf done"));

        Value(result).StandardOutput.Should().Be("done");
    }

    [Fact]
    public async Task Environment_HoldsOnlyTheAllowlistAndTheSpecsValues()
    {
        if (OperatingSystem.IsWindows()) return;

        string variable = $"WOW2_TEST_SECRET_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variable, "ambient-secret");
        try
        {
            string script = $$"""printf '{"secret":"%s","path":"%s","given":"%s"}' "${{variable}}" "$PATH" "$GIVEN" """;
            var scrubbed = Value(await Client().RunJsonAsync(Sh(script) with
            {
                Environment = new Dictionary<string, string?> { ["GIVEN"] = "explicit" },
            }));
            var allowed = Value(await Client(options => options.InheritedVariables.Add(variable)).RunJsonAsync(Sh(script)));

            scrubbed.GetProperty("secret").GetString().Should().BeEmpty("an ambient variable outside the allowlist never passes");
            scrubbed.GetProperty("path").GetString().Should().NotBeEmpty();
            scrubbed.GetProperty("given").GetString().Should().Be("explicit");
            allowed.GetProperty("secret").GetString().Should().Be("ambient-secret");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task Timeout_KillsTheProcessTree()
    {
        if (OperatingSystem.IsWindows()) return;

        string pidFile = Path.Combine(_root, "child.pid");
        var watch = Stopwatch.StartNew();

        var result = await Client().RunAsync(Sh($"sleep 30 & echo $! > '{pidFile}'; wait") with { Timeout = TimeSpan.FromSeconds(1) });

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        AppError error = Error(result);
        error.Type.Should().Be(AppErrorType.OperationTimeout);
        ProcessFailureMapper.KindOf(error).Should().Be(ProcessFailureKind.TimedOut);
        int child = int.Parse((await File.ReadAllTextAsync(pidFile)).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        await WaitUntilAsync(() => !IsAlive(child));
    }

    [Fact]
    public async Task CallerCancellation_ThrowsAndKillsTheProcess()
    {
        if (OperatingSystem.IsWindows()) return;

        string pidFile = Path.Combine(_root, "self.pid");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var act = () => Client().RunAsync(Sh($"echo $$ > '{pidFile}'; exec sleep 30"), cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        int self = int.Parse((await File.ReadAllTextAsync(pidFile)).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        await WaitUntilAsync(() => !IsAlive(self));
    }

    [Fact]
    public async Task FailureLine_BecomesTheParsersError()
    {
        if (OperatingSystem.IsWindows()) return;

        var parser = new JsonFailureLineParser
        {
            Kinds = { ["Rejected"] = AppErrorType.Conflict },
            Format = (type, reason) => type == AppErrorType.Conflict ? "Deployment rejected: " + reason : reason,
        };
        var client = Client(options => options.FailureParser = parser);

        AppError error = Error(await client.RunJsonAsync(
            Sh("""echo 'Traceback: /etc/secret' >&2; echo '{"reason":"target is locked","failure":"Rejected"}' >&2; exit 3""")));

        error.Type.Should().Be(AppErrorType.Conflict);
        error.Message.Should().Be("Deployment rejected: target is locked");
        ProcessFailureMapper.KindOf(error).Should().Be(ProcessFailureKind.Exited);
        error.Metadata![ProcessFailureMapper.ExitCodeKey].Should().Be(3);
    }

    [Fact]
    public async Task UnparsedExit_GetsTheGenericErrorWithoutTheOutput()
    {
        if (OperatingSystem.IsWindows()) return;

        AppError error = Error(await Client(options => options.FailureParser = new JsonFailureLineParser())
            .RunAsync(Sh("echo 'password=hunter2' >&2; exit 2")));

        error.Type.Should().Be(AppErrorType.Unexpected);
        error.Message.Should().NotContain("hunter2");
        ProcessFailureMapper.KindOf(error).Should().Be(ProcessFailureKind.Exited);
    }

    [Fact]
    public async Task SuccessExitCodes_AcceptANonZeroExit()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = await Client().RunAsync(Sh("printf diff; exit 1") with { SuccessExitCodes = [0, 1] });

        Value(result).ExitCode.Should().Be(1);
        Value(result).StandardOutput.Should().Be("diff");
    }

    [Fact]
    public async Task OversizedOutput_KillsTheProcess()
    {
        if (OperatingSystem.IsWindows()) return;

        AppError error = Error(await Client(options => options.MaxOutputBytes = 1024).RunAsync(Sh("yes")));

        ProcessFailureMapper.KindOf(error).Should().Be(ProcessFailureKind.OutputTooLarge);
    }

    [Fact]
    public async Task ErrorTail_KeepsTheLastLineWithinItsCap()
    {
        if (OperatingSystem.IsWindows()) return;

        var client = Client(options =>
        {
            options.MaxErrorBytes = 256;
            options.FailureParser = new JsonFailureLineParser();
        });

        AppError error = Error(await client.RunAsync(
            Sh("""i=0; while [ $i -lt 500 ]; do echo "noise line $i" >&2; i=$((i+1)); done; echo '{"reason":"last words"}' >&2; exit 1""")));

        error.Message.Should().Be("last words");
        error.Type.Should().Be(AppErrorType.ExternalUnavailable);
    }

    [Fact]
    public async Task InvalidJson_IsASerializationFailure()
    {
        if (OperatingSystem.IsWindows()) return;

        AppError error = Error(await Client().RunJsonAsync(Sh("printf 'not json'")));

        error.Type.Should().Be(AppErrorType.SerializationFailed);
        ProcessFailureMapper.KindOf(error).Should().Be(ProcessFailureKind.InvalidOutput);
    }

    [Fact]
    public async Task MissingExecutable_IsNotStarted()
    {
        AppError error = Error(await Client().RunAsync(new ProcessSpec { FileName = Path.Combine(_root, "absent-tool") }));

        error.Type.Should().Be(AppErrorType.ExternalUnavailable);
        ProcessFailureMapper.KindOf(error).Should().Be(ProcessFailureKind.NotStarted);
    }

    [Fact]
    public async Task Start_LeavesTheProcessRunningOnItsOwn()
    {
        if (OperatingSystem.IsWindows()) return;

        string marker = Path.Combine(_root, "detached.txt");

        Result<int> started = Client().Start(Sh($"sleep 0.2; cat > '{marker}'") with { StandardInput = "from stdin" });

        Value(started).Should().BePositive();
        await WaitUntilAsync(() => File.Exists(marker) && File.ReadAllText(marker) == "from stdin");
    }

    [Fact]
    public void Registration_ResolvesTheDefaultAndKeyedClients()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddProcessClient();
        services.AddProcessClient("runner", (options, _) => options.Timeout = TimeSpan.FromMinutes(2));
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        provider.GetRequiredService<IProcessClient>().Should().BeOfType<ProcessClient>();
        provider.GetRequiredKeyedService<IProcessClient>("runner").Should().BeOfType<ProcessClient>()
            .And.NotBeSameAs(provider.GetRequiredService<IProcessClient>());
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("plain text", null)]
    [InlineData("""{"other":"x"}""", null)]
    [InlineData("""{"reason":"   "}""", null)]
    [InlineData("""{"reason":42}""", null)]
    [InlineData("""{"reason":"bad\u0007bell"}""", "badbell")]
    [InlineData("""{"reason":"refused","failure":"Rejected"}""", "refused")]
    public void JsonFailureLine_ReadsOnlyAStringReason(string line, string? expected) =>
        (new JsonFailureLineParser().Parse(1, "earlier output\n" + line)?.Message).Should().Be(expected);

    [Fact]
    public void JsonFailureLine_CapsTheReason() =>
        new JsonFailureLineParser { MaxReasonLength = 5 }.Parse(1, """{"reason":"abcdefghij"}""")!.Message.Should().Be("abcde");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static ProcessClient Client(Action<ProcessClientOptions>? configure = null)
    {
        var options = new ProcessClientOptions { Timeout = TimeSpan.FromSeconds(20) };
        configure?.Invoke(options);
        return new ProcessClient(options, TimeProvider.System, NullLogger<ProcessClient>.Instance);
    }

    private static ProcessSpec Sh(string script) => new() { FileName = Shell, Arguments = ["-c", script] };

    private static T Value<T>(Result<T> result)
        where T : notnull
    {
        result.Should().BeOfType<Result<T>.Success>(result is Result<T>.Failure failure ? failure.Error.Message : string.Empty);
        return ((Result<T>.Success)result).Value;
    }

    private static AppError Error<T>(Result<T> result)
        where T : notnull
    {
        result.Should().BeOfType<Result<T>.Failure>();
        return ((Result<T>.Failure)result).Error;
    }

    private static bool IsAlive(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the process should reach the expected state");
            await Task.Delay(50);
        }
    }
}
