using System.Globalization;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Parses the directives in an Apply script's leading comment header.</summary>
/// <remarks>
///   - the header is every leading blank or <c>--</c> line; the first other line ends it
///   - a header line <c>-- @name args</c> is a directive; plain comments are ignored
///   - an unknown or malformed directive fails <c>Validation</c>, so a typo never turns a contract into an expand
/// </remarks>
internal static class MigrationHeaderParser
{
    private const string NoTransaction = "@no-transaction";
    private const string Contract = "@contract";
    private const string Requires = "@requires";
    private const string LockTimeout = "@lock-timeout";
    private const string SincePrefix = "since=";

    /// <summary>Parses a script's header.</summary>
    /// <param name="migration">The <c>NNN-name</c> label, for error messages.</param>
    /// <param name="applySql">The Apply script.</param>
    /// <returns>The header, or a <c>Validation</c> failure naming the bad directive.</returns>
    public static Result<MigrationHeader> Parse(string migration, string applySql)
    {
        var noTransaction = false;
        var kind = MigrationKind.Expand;
        string? since = null;
        var requires = new List<string>();
        TimeSpan? lockTimeout = null;

        using var reader = new StringReader(applySql);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            if (!trimmed.StartsWith("--", StringComparison.Ordinal))
                break; // The first non-comment line ends the header.

            var comment = trimmed[2..].Trim();
            if (!comment.StartsWith('@'))
                continue;

            var space = comment.IndexOfAny([' ', '\t']);
            var name = (space < 0 ? comment : comment[..space]).ToLowerInvariant();
            var argument = space < 0 ? "" : comment[(space + 1)..].Trim();

            switch (name)
            {
                case NoTransaction when argument.Length == 0:
                    noTransaction = true;
                    break;
                case Contract when argument.StartsWith(SincePrefix, StringComparison.OrdinalIgnoreCase)
                                   && MigrationRelease.TryParse(argument[SincePrefix.Length..], out _):
                    kind = MigrationKind.Contract;
                    since = argument[SincePrefix.Length..].Trim();
                    break;
                case Contract:
                    return Invalid(migration, $"'-- {Contract}' needs 'since=vX.Y', the oldest release that no longer reads what it removes; found '{argument}'");
                case Requires when MigrationConstants.IdentifierPattern().IsMatch(argument):
                    requires.Add(argument);
                    break;
                case Requires:
                    return Invalid(migration, $"'-- {Requires}' needs one background migration name (a lowercase identifier); found '{argument}'");
                case LockTimeout when TryParseSeconds(argument, out var seconds):
                    lockTimeout = TimeSpan.FromSeconds(seconds);
                    break;
                case LockTimeout:
                    return Invalid(migration, $"'-- {LockTimeout}' needs a whole number of seconds from 1 to 3600; found '{argument}'");
                default:
                    return Invalid(migration,
                        $"unknown header directive '-- {comment}'; known: {NoTransaction}, {Contract} since=vX.Y, {Requires} <name>, {LockTimeout} <seconds>");
            }
        }

        return Result<MigrationHeader>.Ok(new MigrationHeader(noTransaction, kind, since, requires, lockTimeout));
    }

    private static bool TryParseSeconds(string argument, out int seconds)
    {
        var digits = argument.EndsWith('s') ? argument[..^1] : argument;
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out seconds) && seconds is >= 1 and <= 3600;
    }

    private static Result<MigrationHeader> Invalid(string migration, string reason) =>
        Result<MigrationHeader>.Fail(AppErrorFactory.Validation($"Migration '{migration}': {reason}."));
}
