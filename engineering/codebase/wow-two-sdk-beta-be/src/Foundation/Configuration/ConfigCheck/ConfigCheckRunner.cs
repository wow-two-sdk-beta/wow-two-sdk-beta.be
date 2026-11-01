using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Configuration.ConfigCheck;

/// <summary>One check run: the result, plus notes for the human report or the boot log (an exception's message, a skipped check).</summary>
internal sealed record ConfigCheckRun
{
    /// <summary>Gets the result line's content.</summary>
    public required ConfigCheckResult Result { get; init; }

    /// <summary>Gets the diagnostics that stay off the result line.</summary>
    public required IReadOnlyList<string> Notes { get; init; }
}

/// <summary>Runs the checks behind <c>config check</c> and the boot over a built host's services, starting nothing and writing nothing.</summary>
/// <remarks>
///   - invalid values: every recorded settings record resolved as the boot resolves it, then the host's startup validator for any other record
///   - unknown keys: <see cref="SettingsKeyWalker"/> over the declared roots and the recorded sections
///   - stranding: every <see cref="IStrandingCheck"/>, in one fresh scope; none registered leaves it empty
///   - an exception that stops a check ends the run with exit <c>2</c>
/// </remarks>
internal static partial class ConfigCheckRunner
{
    /// <summary>Runs every check once and collects every finding.</summary>
    /// <param name="services">The built host's root provider.</param>
    /// <param name="service">The service name for the result.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public static async Task<ConfigCheckRun> RunAsync(IServiceProvider services, string service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        var started = Stopwatch.GetTimestamp();
        var notes = new List<string>();
        try
        {
            var configuration = services.GetRequiredService<IConfiguration>();
            var records = services.GetServices<SettingsRecord>().DistinctBy(record => record.Type).ToList();
            var roots = services.GetServices<SettingsRoot>().Select(root => root.Section);

            var unknown = SettingsKeyWalker.FindUnknown(configuration, records, roots);
            var invalid = Validate(services, records, unknown);
            var (stranded, failure) = await CountStrandedAsync(services, settingsInvalid: invalid.Count > 0, notes, ct).ConfigureAwait(false);
            if (failure is { } failed)
            {
                notes.Add(Redact($"{failed.Check}: {failed.Exception.GetType().Name}: {failed.Exception.Message}"));
                return Failed(service, $"stranding check {failed.Check} failed ({failed.Exception.GetType().Name})", started, notes);
            }

            return new ConfigCheckRun
            {
                Result = new ConfigCheckResult { Service = service, Invalid = invalid, Unknown = unknown, Stranded = stranded, DurationMs = Elapsed(started) },
                Notes = notes,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Failed(service, "the check was cancelled", started, notes);
        }
        catch (Exception exception)
        {
            notes.Add(Redact($"{exception.GetType().Name}: {exception.Message}"));
            return Failed(service, $"the settings validation could not finish ({exception.GetType().Name})", started, notes);
        }
    }

    private static List<ConfigCheckInvalid> Validate(IServiceProvider services, List<SettingsRecord> records, IReadOnlyList<string> unknown)
    {
        var invalid = new List<ConfigCheckInvalid>();
        var bindingFailed = false;
        foreach (var record in records)
        {
            try
            {
                record.Resolve(services);
            }
            catch (OptionsValidationException exception)
            {
                invalid.AddRange(SettingsFailureReader.Read(record.Type, record.Section, exception));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                bindingFailed = true;
                var finding = SettingsFailureReader.ReadBinding(record.Type, record.Section, exception);
                var reported = finding.Code == "unknown-key" && unknown.Any(key => IsUnder(key, finding.Setting));
                if (!reported)
                    invalid.Add(finding);
            }
        }

        // Records registered outside the SDK recipe still validate at boot; the host's startup validator runs them.
        var recorded = records.Select(record => record.Type).ToHashSet();
        try
        {
            services.GetService<IStartupValidator>()?.Validate();
        }
        catch (OptionsValidationException exception)
        {
            AddUnrecorded(invalid, recorded, exception);
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(inner => inner is OptionsValidationException))
        {
            foreach (var inner in exception.InnerExceptions.Cast<OptionsValidationException>())
                AddUnrecorded(invalid, recorded, inner);
        }
        catch (Exception) when (bindingFailed)
        {
            // A recorded record that failed to bind fails the startup validator again; it is reported already.
        }

        return invalid;
    }

    private static void AddUnrecorded(List<ConfigCheckInvalid> invalid, HashSet<Type> recorded, OptionsValidationException exception)
    {
        if (!recorded.Contains(exception.OptionsType))
            invalid.AddRange(SettingsFailureReader.Read(exception.OptionsType, section: null, exception));
    }

    private static async Task<(List<StrandedCount> Stranded, StrandingFailure? Failure)> CountStrandedAsync(
        IServiceProvider services, bool settingsInvalid, List<string> notes, CancellationToken ct)
    {
        var stranded = new List<StrandedCount>();
        var scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            foreach (var check in scope.ServiceProvider.GetServices<IStrandingCheck>())
            {
                var name = check.GetType().Name;
                IReadOnlyList<StrandedCount> counts;
                try
                {
                    counts = await check.CountAsync(ct).ConfigureAwait(false);
                }
                catch (OptionsValidationException) when (settingsInvalid)
                {
                    notes.Add($"{name}: skipped, its settings are invalid");
                    continue;
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    return (stranded, new StrandingFailure { Check = name, Exception = exception });
                }

                stranded.AddRange(counts.Where(count => count.Count > 0));
            }
        }

        return (stranded, null);
    }

    private static bool IsUnder(string key, string section) =>
        key.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase);

    private static ConfigCheckRun Failed(string service, string error, long started, List<string> notes) =>
        new() { Result = new ConfigCheckResult { Service = service, Error = error, DurationMs = Elapsed(started) }, Notes = notes };

    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    /// <summary>Redacts <c>Password=…</c> pairs and a URI's <c>user:password@</c>, so a note never carries a credential.</summary>
    private static string Redact(string text) => UriCredential().Replace(Credential().Replace(text, "$1=***"), "$1***@");

    [GeneratedRegex(@"\b(password|pwd)\s*=\s*[^;\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex Credential();

    [GeneratedRegex(@"(://[^:/@\s]+:)[^@\s]+@")]
    private static partial Regex UriCredential();

    /// <summary>The stranding check that threw, and its exception.</summary>
    private sealed record StrandingFailure
    {
        /// <summary>Gets the check's type name.</summary>
        public required string Check { get; init; }

        /// <summary>Gets what it threw.</summary>
        public required Exception Exception { get; init; }
    }
}
