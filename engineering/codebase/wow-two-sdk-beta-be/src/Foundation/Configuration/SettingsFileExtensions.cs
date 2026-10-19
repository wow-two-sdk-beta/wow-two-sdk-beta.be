using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Configuration;

/// <summary>
/// Adds the two optional settings files a .NET service reads beside its checked-in ones: the deployed target's mounted
/// <c>appsettings.Target.json</c> and the developer machine's own <c>appsettings.Local.json</c>.
/// </summary>
/// <remarks>
/// Load order: <c>appsettings.json</c>, <c>appsettings.{Environment}.json</c>, <c>appsettings.Target.json</c>,
/// <c>appsettings.Local.json</c>, then user secrets, environment variables and every later source, such as the vault.
///   - the target file is optional on every run; a deployed target's release bundle mounts it
///   - the local file is read only on a local run (<see cref="LocalRunExtensions"/>) whose launch profile sets
///     <c>LOCAL_SETTINGS=true</c>, so a test host never reads the developer's file
///   - transition: a deployed host without the target file reads a legacy <c>appsettings.Local.json</c>, mounted by a
///     release bundle older than pipelines v0.7.0, in its place and logs a warning; a later release drops this fallback
/// </remarks>
public static class SettingsFileExtensions
{
    /// <summary>The settings file a deployed target mounts beside the app, filled per environment.</summary>
    public const string TargetFile = "appsettings.Target.json";

    /// <summary>The developer machine's own overrides: gitignored, outside every image, read only by a local run.</summary>
    public const string LocalFile = "appsettings.Local.json";

    /// <summary>The host-bootstrap variable a launch profile sets to <c>true</c> so a local run reads <see cref="LocalFile"/>.</summary>
    public const string LocalSettingsVariable = "LOCAL_SETTINGS";

    private static readonly object Added = new();

    /// <summary>
    /// Inserts <see cref="TargetFile"/> and, on a local run whose launch profile asks, <see cref="LocalFile"/> right after
    /// <c>appsettings.{Environment}.json</c>, so user secrets, environment variables and every later source still win.
    /// Only a file present at startup is added, and values already set through the configuration indexer survive.
    /// Idempotent; <c>AddApiDefaults</c> calls it too, so call it first only when the host reads settings before that.
    /// </summary>
    /// <typeparam name="TBuilder">The host builder type, usually <c>WebApplicationBuilder</c>.</typeparam>
    /// <param name="builder">The host builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.AddSettingsFiles();
    /// var proxies = builder.Configuration.GetSection("Deployment:TrustedProxies").Get&lt;string[]&gt;();
    /// </code>
    /// </example>
    public static TBuilder AddSettingsFiles<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!builder.Properties.TryAdd(Added, true))
            return builder;

        // Decide everything before a source changes: inserting one rebuilds every provider.
        var configuration = builder.Configuration;
        var files = configuration.GetFileProvider();
        var target = files.GetFileInfo(TargetFile).Exists;
        var local = files.GetFileInfo(LocalFile).Exists;
        // A bundle older than pipelines v0.7.0 mounts the target's file at the machine file's name.
        var legacy = !target && local && LocalRunExtensions.IsDeployed(configuration);
        var machine = local && !legacy && ReadsLocalFile(builder.Environment, configuration);

        var added = new List<IConfigurationSource>(2);
        if (target || legacy)
            added.Add(Source(legacy ? LocalFile : TargetFile, files));
        if (machine)
            added.Add(Source(LocalFile, files));
        Insert(configuration, InsertionIndex(configuration.Sources, builder.Environment.EnvironmentName), added);

        if (legacy)
            builder.Services.AddHostedService<LegacySettingsFileWarning>();
        return builder;
    }

    /// <summary>Reports whether this run reads <see cref="LocalFile"/>: a local run whose launch profile sets
    /// <c>LOCAL_SETTINGS=true</c>, in configuration or the process environment.</summary>
    /// <param name="environment">The host environment.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <returns><see langword="true"/> when the machine's own file applies.</returns>
    public static bool ReadsLocalFile(IHostEnvironment environment, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        return environment.IsLocalRun(configuration)
            && (IsOn(configuration[LocalSettingsVariable]) || IsOn(Environment.GetEnvironmentVariable(LocalSettingsVariable)));
    }

    private static bool IsOn(string? value) => bool.TryParse(value, out var on) && on;

    /// <summary>Inserts <paramref name="added"/> at <paramref name="index"/>. Each insert rebuilds every provider, which
    /// drops a value set through the indexer, so each earlier provider's values are restored onto its rebuilt one.</summary>
    private static void Insert(IConfigurationManager configuration, int index, List<IConfigurationSource> added)
    {
        if (added.Count == 0)
            return;

        var root = configuration as IConfigurationRoot;
        var before = root?.Providers.Select(Values).ToArray() ?? [];
        for (var offset = 0; offset < added.Count; offset++)
            configuration.Sources.Insert(index + offset, added[offset]);
        if (root is null)
            return;

        var after = root.Providers.ToArray();
        for (var position = 0; position < before.Length; position++)
        {
            var provider = after[position < index ? position : position + added.Count];
            foreach (var (key, value) in before[position])
            {
                if (!provider.TryGet(key, out var current) || !string.Equals(current, value, StringComparison.Ordinal))
                    provider.Set(key, value);
            }
        }
    }

    /// <summary>Every key a provider holds, with its value.</summary>
    private static Dictionary<string, string?> Values(IConfigurationProvider provider)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string?>();
        pending.Push(null);
        while (pending.TryPop(out var parent))
        {
            foreach (var child in provider.GetChildKeys([], parent))
            {
                var key = parent is null ? child : ConfigurationPath.Combine(parent, child);
                if (!visited.Add(key))
                    continue;
                if (provider.TryGet(key, out var value))
                    values[key] = value;
                pending.Push(key);
            }
        }

        return values;
    }

    private static JsonConfigurationSource Source(string path, IFileProvider files)
        => new() { Path = path, Optional = true, ReloadOnChange = false, FileProvider = files };

    /// <summary>Right after <c>appsettings.{Environment}.json</c>; else after <c>appsettings.json</c>; else before the
    /// first environment-variable source; else last.</summary>
    private static int InsertionIndex(IList<IConfigurationSource> sources, string environmentName)
    {
        var environmentFile = "appsettings." + environmentName + ".json";
        int environmentAt = -1, baseAt = -1, variablesAt = -1;
        for (var index = 0; index < sources.Count; index++)
        {
            switch (sources[index])
            {
                case JsonConfigurationSource { Path: var path } when string.Equals(path, environmentFile, StringComparison.OrdinalIgnoreCase):
                    environmentAt = index;
                    break;
                case JsonConfigurationSource { Path: var path } when string.Equals(path, "appsettings.json", StringComparison.OrdinalIgnoreCase):
                    baseAt = index;
                    break;
                case EnvironmentVariablesConfigurationSource when variablesAt < 0:
                    variablesAt = index;
                    break;
            }
        }

        if (environmentAt >= 0) return environmentAt + 1;
        if (baseAt >= 0) return baseAt + 1;
        return variablesAt >= 0 ? variablesAt : sources.Count;
    }
}
