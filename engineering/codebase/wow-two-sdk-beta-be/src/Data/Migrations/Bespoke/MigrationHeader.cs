namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Represents the directives an Apply script declares in its leading comment header.</summary>
/// <param name="NoTransaction">Whether <c>-- @no-transaction</c> runs the script outside a transaction.</param>
/// <param name="Kind">The kind: a contract when <c>-- @contract</c> is present, else an expand.</param>
/// <param name="ContractSince">The oldest owner release that no longer reads what the contract removes, as written.</param>
/// <param name="Requires">The background migrations <c>-- @requires</c> names, which must finish first.</param>
/// <param name="LockTimeout">The <c>-- @lock-timeout</c> override, or null for the set's default.</param>
internal sealed record MigrationHeader(
    bool NoTransaction,
    MigrationKind Kind,
    string? ContractSince,
    IReadOnlyList<string> Requires,
    TimeSpan? LockTimeout);
