# Processes

> Bounded child processes: a CLI runner, `yt-dlp`, `ffmpeg`, a Python adapter. One client replaces each hand-written
> `Process` loop with its own timeout, kill and environment handling.

```csharp
builder.Services.AddProcessClient("runner", (options, services) =>
{
    options.Timeout = TimeSpan.FromSeconds(110);                 // past it, the process tree is killed
    options.InheritedVariables.Add("PYTHONPATH");                // the app picks what passes; nothing else does
    options.FailureParser = new JsonFailureLineParser            // last stderr line: {"reason":"…","failure":"Rejected"}
    {
        Kinds = { ["Rejected"] = AppErrorType.Conflict },
        Format = (type, reason) => type == AppErrorType.Conflict ? "Deployment rejected: " + reason : reason,
    };
});

public sealed class DeploymentGateway([FromKeyedServices("runner")] IProcessClient runner, RunnerSettings settings)
{
    public Task<Result<JsonElement>> SubmitAsync(string target, JsonElement values, CancellationToken ct) =>
        runner.RunJsonAsync(new ProcessSpec
        {
            FileName = settings.Python,
            Arguments = [settings.TransportPath, "submit", "--target", target, "--stdin"],
            Environment = new Dictionary<string, string?> { ["RUNNER_TOKENS_FILE"] = settings.TokensFile },
            StandardInput = values.GetRawText(),                 // values never sit in an argument
        }, ct);
}
```

- Arguments are an argument list, never a shell; a process listing shows them, so secrets go through `StandardInput`.
- The environment starts empty: only `InheritedVariables` (default `ProcessEnvironmentConstants.Baseline`: `PATH`,
  `HOME`, locale, temp) pass from this process, then the spec's own values (`null` removes one).
- Standard input is always redirected: written from `StandardInput`, then closed, so a child never reads the host's.
- A timeout (`ProcessClientOptions.Timeout`, or the spec's) and the caller's cancellation kill the whole process tree.
- Standard output is capped (`MaxOutputBytes`, 16 MiB; past it the tree is killed); standard error keeps its last
  `MaxErrorBytes` (64 KiB), which is all a failure-line parser reads.
- `RunAsync` returns the output on a success exit code (`SuccessExitCodes`, default `[0]`); `RunJsonAsync` also parses
  standard output as one JSON document.
- `Start` launches a child that outlives the call — nothing waits for it, its output is drained and dropped.
- Failures are `Result` errors marked with a `ProcessFailureKind` — read it with `ProcessFailureMapper.KindOf(error)`
  to word your own message:

| Kind | Error type | When |
|---|---|---|
| `NotStarted` | `ExternalUnavailable` | missing executable, permission, working folder |
| `TimedOut` | `OperationTimeout` | ran past its limit |
| `Exited` | the parser's, else `Unexpected` | exit code outside the success codes |
| `OutputTooLarge` | `Unexpected` | standard output past its cap |
| `InvalidOutput` | `SerializationFailed` | `RunJsonAsync` read something that is not JSON |

- No error message repeats arguments, environment values or output; only a parser's reason, which the child keeps
  display-safe, reaches it. Logs carry the executable's name, the kind and the exit code.
- Metrics (`WoW.Two.Sdk.Beta.Be.Integrations.Processes`): `process.runs`, `process.duration`, tagged `process` and
  `outcome`.
