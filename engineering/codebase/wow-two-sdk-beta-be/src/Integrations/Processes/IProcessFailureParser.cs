using WoW.Two.Sdk.Beta.Be.Foundation.Errors;

namespace WoW.Two.Sdk.Beta.Be.Integrations.Processes;

/// <summary>Parses a failed process's standard error into the display-safe error its caller returns.</summary>
public interface IProcessFailureParser
{
    /// <summary>Parses the failure of a process that exited with a code outside its success codes.</summary>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="standardError">The tail of the standard error.</param>
    /// <returns>The error, or <see langword="null"/> when the output carries no recognizable failure; the client then returns its
    /// generic error, which never repeats the output.</returns>
    AppError? Parse(int exitCode, string standardError);
}
