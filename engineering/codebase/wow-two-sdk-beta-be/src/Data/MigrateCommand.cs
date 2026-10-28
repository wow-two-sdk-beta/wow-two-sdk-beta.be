using System.CommandLine;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;

namespace WoW.Two.Sdk.Beta.Be.Data;

/// <summary>Answers <c>app migrate &lt;verb&gt;</c> from an image's own services — the command a rollout runs in a one-off container.</summary>
/// <remarks>
///   - verbs: <c>check</c>, <c>plan</c>, <c>apply</c>, <c>seed</c>, <c>sets</c>, <c>rollback</c>, <c>forget</c>, <c>repair</c>, <c>baseline</c>, <c>claim</c>,
///     <c>locks</c>, <c>unlock</c>, <c>bg</c>; a bare <c>migrate</c> runs <c>apply</c>, then <c>seed</c>
///   - exits <c>0</c> done or current · <c>1</c> refused · <c>2</c> failed · <c>3</c> busy; the last stdout line is one <c>migrate-result {json}</c> line
///   - EF and DbUp sets answer <c>check</c>, <c>plan</c>, <c>apply</c>, <c>seed</c> and <c>sets</c>; every other verb refuses them
///   - <c>Migrations:ConnectionString</c>, when set, connects every bespoke set for the run; the host calls nothing to turn it on
/// </remarks>
/// <example>
/// <code>
/// var app = builder.Build();
/// if (MigrateCommand.IsCommand(args))
///     return await app.RunMigrateCommandAsync(args);
/// </code>
/// </example>
public static class MigrateCommand
{
    /// <summary>Holds the command's name: the first argument that selects it.</summary>
    public const string Name = "migrate";

    /// <summary>Holds the stamp an image's <c>apply</c> and <c>seed</c> record on the rows they write.</summary>
    public const string AppliedBy = "deploy";

    /// <summary>Returns whether the process arguments ask for the migrate command: <c>migrate</c> first.</summary>
    /// <param name="args">The process arguments.</param>
    public static bool IsCommand(string[] args) => args is [Name, ..];

    /// <summary>Runs one migrate invocation over a host's services, writing the human report to <paramref name="error"/> and the result line last to <paramref name="output"/>.</summary>
    /// <remarks>
    ///   - for an image's E2E tier and the host helper; turn the run on before anything resolves a set's runner, or the migrate key stays off
    ///   - never starts the host and never disposes <paramref name="services"/>
    /// </remarks>
    /// <param name="services">The host's root provider.</param>
    /// <param name="args">The arguments, <c>migrate</c> first, such as <c>migrate apply --dry-run</c>.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The exit code.</returns>
    public static Task<int> RunAsync(
        IServiceProvider services, IReadOnlyList<string> args, TextWriter output, TextWriter error, CancellationToken ct = default) =>
        RunAsync(services, args, output, error, handleTermination: false, ct);

    /// <summary>Runs one invocation; <paramref name="handleTermination"/> lets SIGINT and SIGTERM cancel it, as in a process of its own.</summary>
    internal static Task<int> RunAsync(
        IServiceProvider services, IReadOnlyList<string> args, TextWriter output, TextWriter error, bool handleTermination, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var builder = new MigrateCommandBuilder(new ServiceProviderMigrateCommandHost(services, AppliedBy), output, error);
        var migrate = new Command(Name, "Migrate this image's sets: a bare migrate runs apply, then seed.");
        builder.AddVerbs(migrate, bareMigrate: true);
        var root = new RootCommand("The image's migrate command.") { migrate };

        return builder.InvokeAsync(root.Parse(args), handleTermination, ct);
    }
}
