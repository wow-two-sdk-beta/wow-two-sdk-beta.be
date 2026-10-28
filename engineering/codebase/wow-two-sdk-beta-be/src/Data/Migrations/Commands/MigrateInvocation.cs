using System.CommandLine;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands;

/// <summary>Represents one parsed verb invocation the tree runs.</summary>
/// <param name="Verb">The verb, one of the <c>MigrateVerbs</c> names.</param>
/// <param name="Set">The <c>--set</c> value, or null for every set.</param>
/// <param name="Json">Whether <c>--json</c> asked for the JSON report on stdout.</param>
/// <param name="Parse">The parse result, from which a host reads its own options.</param>
internal sealed record MigrateInvocation(string Verb, string? Set, bool Json, ParseResult Parse);
