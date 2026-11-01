using WoW.Two.Sdk.Beta.Be.Data;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration.ConfigCheck;

namespace WoW.Two.Sdk.Beta.Be.Meta;

/// <summary>Holds a service's own one-off verbs, beside the built-in <c>migrate</c> and <c>config</c>, for <see cref="ServiceHost"/> to dispatch.</summary>
/// <remarks>
///   - a pre-host verb runs before any builder exists: no settings, logging or services, and stdout is the verb's alone
///     (<c>keys generate</c> printing a PEM bundle)
///   - a host verb runs on the built host's services without serving: no listener, hosted service or startup work; the
///     host's console logs go to stderr, so stdout carries only what the verb writes
///   - a verb matches the first argument exactly; any other first argument serves
/// </remarks>
public sealed class ServiceVerbs
{
    private readonly Dictionary<string, Func<ServiceVerbInvocation, CancellationToken, Task<int>>> _preHost = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<IServiceProvider, ServiceVerbInvocation, CancellationToken, Task<int>>> _host = new(StringComparer.Ordinal);

    internal ServiceVerbs()
    {
    }

    /// <summary>Gets the verbs every service answers without registering them: <c>migrate</c> and <c>config</c>.</summary>
    public static IReadOnlyList<string> BuiltIn { get; } = [MigrateCommand.Name, ConfigCheckCommand.Name];

    /// <summary>Adds a verb that runs before any host is built and returns its exit code.</summary>
    /// <param name="name">The first argument that selects the verb, such as <c>keys</c>.</param>
    /// <param name="run">Runs the verb over its arguments and the process streams.</param>
    /// <returns>The verbs, for chaining.</returns>
    /// <example>
    /// <code>
    /// verbs.AddPreHostVerb("keys", verb => SigningKeyCommand.Run(verb.Arguments, verb.Output, verb.Error, TimeProvider.System));
    /// </code>
    /// </example>
    public ServiceVerbs AddPreHostVerb(string name, Func<ServiceVerbInvocation, int> run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return AddPreHostVerb(name, (invocation, _) => Task.FromResult(run(invocation)));
    }

    /// <summary>Adds an asynchronous verb that runs before any host is built and returns its exit code.</summary>
    /// <param name="name">The first argument that selects the verb.</param>
    /// <param name="run">Runs the verb over its arguments and the process streams.</param>
    /// <returns>The verbs, for chaining.</returns>
    public ServiceVerbs AddPreHostVerb(string name, Func<ServiceVerbInvocation, CancellationToken, Task<int>> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        ThrowIfTaken(name);

        _preHost.Add(name, run);
        return this;
    }

    /// <summary>Adds a verb that runs on the built host's services without serving and returns its exit code.</summary>
    /// <param name="name">The first argument that selects the verb, such as <c>reindex</c>.</param>
    /// <param name="run">Runs the verb over the host's root provider, its arguments and the streams.</param>
    /// <returns>The verbs, for chaining.</returns>
    /// <remarks>The host is built from the same builder configuration as a serving boot, then disposed when the verb returns.</remarks>
    public ServiceVerbs AddHostVerb(string name, Func<IServiceProvider, ServiceVerbInvocation, CancellationToken, Task<int>> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        ThrowIfTaken(name);

        _host.Add(name, run);
        return this;
    }

    internal bool TryGetPreHost(string name, out Func<ServiceVerbInvocation, CancellationToken, Task<int>> run) =>
        _preHost.TryGetValue(name, out run!);

    internal bool TryGetHost(string name, out Func<IServiceProvider, ServiceVerbInvocation, CancellationToken, Task<int>> run) =>
        _host.TryGetValue(name, out run!);

    private void ThrowIfTaken(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.StartsWith('-') || name.Any(char.IsWhiteSpace))
            throw new ArgumentException($"A verb name is one word that does not start with '-': '{name}'.", nameof(name));
        if (BuiltIn.Contains(name, StringComparer.Ordinal))
            throw new ArgumentException($"'{name}' is built in; every service answers it.", nameof(name));
        if (_preHost.ContainsKey(name) || _host.ContainsKey(name))
            throw new ArgumentException($"The verb '{name}' is registered already.", nameof(name));
    }
}
