using System.Data.Common;
using System.Globalization;
using Dapper;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

/// <summary>Appends one row per writing verb to a set's <c>{history}_events</c> table: verb, ordinals, who, job, when and detail.</summary>
/// <remarks>
///   - the table is append-only: the migrator inserts and never updates or deletes a row
///   - history stays the state every older image reads; the events explain how it got there
/// </remarks>
/// <param name="dialect">The set's dialect.</param>
/// <param name="options">The set's options: schema and history table.</param>
/// <param name="clock">The clock stamping <c>recorded_at</c>.</param>
/// <param name="audit">Who and which job runs the verb.</param>
internal sealed class MigrationEventStore(IMigrationDialect dialect, MigrationOptions options, TimeProvider clock, IOptions<MigrationAuditOptions>? audit)
{
    /// <summary>The longest <c>recorded_by</c> and <c>job</c> value the table holds.</summary>
    private const int ActorLength = 120;

    /// <summary>Creates the event table if missing and records one event.</summary>
    /// <param name="connection">The open connection to write to.</param>
    /// <param name="transaction">The verb's transaction, so the event commits with its effect, or null to write bare.</param>
    /// <param name="verb">The verb, one of <see cref="MigrationEventVerbs"/>.</param>
    /// <param name="ordinals">The ordinals the verb touched; empty for a verb such as <c>claim</c>.</param>
    /// <param name="detail">What the verb did beyond the ordinals, or null.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public async Task RecordAsync(
        DbConnection connection, DbTransaction? transaction, string verb, IEnumerable<int> ordinals, string? detail, CancellationToken ct)
    {
        await dialect.EnsureEventsTableAsync(connection, transaction, options.SchemaName, options.EventsTableName, ct).ConfigureAwait(false);

        var actor = audit?.Value ?? new MigrationAuditOptions();
        var sql = $"""
            INSERT INTO {dialect.QualifyHistoryTable(options.SchemaName, options.EventsTableName)} (verb, ordinals, recorded_by, job, recorded_at, detail)
            VALUES (@verb, @ordinals, @recordedBy, @job, @recordedAt, @detail)
            """;
        var row = new
        {
            verb,
            ordinals = string.Join(",", ordinals.Order().Select(ordinal => ordinal.ToString("D3", CultureInfo.InvariantCulture))),
            recordedBy = Cut(actor.By) ?? "unknown",
            job = Cut(actor.Job),
            recordedAt = clock.GetUtcNow(),
            detail,
        };
        await connection.ExecuteAsync(new CommandDefinition(sql, row, transaction, cancellationToken: ct)).ConfigureAwait(false);
    }

    private static string? Cut(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length > ActorLength ? value[..ActorLength] : value;
}
