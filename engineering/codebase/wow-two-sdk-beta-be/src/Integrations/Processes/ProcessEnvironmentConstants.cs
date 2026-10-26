namespace WoW.Two.Sdk.Beta.Be.Integrations.Processes;

/// <summary>Holds the environment variables a child process inherits by default: what any executable needs to find programs,
/// a home, a temporary folder and a locale — never an application's settings or secrets.</summary>
public static class ProcessEnvironmentConstants
{
    /// <summary>Holds the default inherited variables. Unix: <c>PATH</c>, <c>HOME</c>, <c>LANG</c>, <c>LC_ALL</c>, <c>LC_CTYPE</c>,
    /// <c>TMPDIR</c>, <c>TZ</c>; Windows: <c>SystemRoot</c>, <c>TEMP</c>, <c>TMP</c>, <c>USERPROFILE</c>, <c>PATHEXT</c>, <c>ComSpec</c>.</summary>
    public static readonly IReadOnlyList<string> Baseline =
    [
        "PATH", "HOME", "LANG", "LC_ALL", "LC_CTYPE", "TMPDIR", "TZ",
        "SystemRoot", "TEMP", "TMP", "USERPROFILE", "PATHEXT", "ComSpec",
    ];
}
