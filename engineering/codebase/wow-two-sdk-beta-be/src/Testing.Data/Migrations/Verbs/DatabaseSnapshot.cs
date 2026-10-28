using System.Data.Common;
using System.Text;
using Dapper;

namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Verbs;

/// <summary>Captures what a database holds — schemas, relations, columns, every table's rows and advisory locks — so two captures tell whether a run wrote anything.</summary>
/// <remarks>Reads every row of every user table; meant for test databases.</remarks>
internal static class DatabaseSnapshot
{
    /// <summary>Captures the database a connection reaches, as one comparable text.</summary>
    /// <param name="connection">An open connection.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <exception cref="NotSupportedException">The provider is neither PostgreSQL nor SQLite.</exception>
    public static Task<string> TakeAsync(DbConnection connection, CancellationToken ct) =>
        connection.GetType().Name switch
        {
            "NpgsqlConnection" => TakePostgresAsync(connection, ct),
            "SqliteConnection" => TakeSqliteAsync(connection, ct),
            var name => throw new NotSupportedException($"A database snapshot reads PostgreSQL or SQLite, not {name}."),
        };

    private static async Task<string> TakePostgresAsync(DbConnection connection, CancellationToken ct)
    {
        const string userSchemas = "n.nspname not in ('pg_catalog', 'information_schema') and n.nspname not like 'pg\\_toast%' and n.nspname not like 'pg\\_temp%'";
        var snapshot = new StringBuilder();
        foreach (var schema in await connection.QueryAsync<string>(new CommandDefinition(
                     $"select n.nspname from pg_namespace n where {userSchemas} order by 1", cancellationToken: ct)).ConfigureAwait(false))
            snapshot.Append("schema ").AppendLine(schema);

        var relations = (await connection.QueryAsync<(string Schema, string Name, string Kind)>(new CommandDefinition(
            $"select n.nspname, c.relname, c.relkind::text from pg_class c join pg_namespace n on n.oid = c.relnamespace where {userSchemas} order by 1, 2",
            cancellationToken: ct)).ConfigureAwait(false)).ToList();
        foreach (var (schema, name, kind) in relations)
            snapshot.Append("relation ").Append(schema).Append('.').Append(name).Append(' ').AppendLine(kind);

        foreach (var column in await connection.QueryAsync<string>(new CommandDefinition(
                     "select table_schema || '.' || table_name || '.' || column_name || ' ' || data_type || ' ' || is_nullable from information_schema.columns " +
                     "where table_schema not in ('pg_catalog', 'information_schema') order by table_schema, table_name, ordinal_position",
                     cancellationToken: ct)).ConfigureAwait(false))
            snapshot.Append("column ").AppendLine(column);

        foreach (var (schema, name, _) in relations.Where(relation => relation.Kind is "r" or "p"))
        {
            var rows = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                $"select coalesce(md5(string_agg(t::text, '|' order by t::text)), '') from \"{schema}\".\"{name}\" t", cancellationToken: ct)).ConfigureAwait(false);
            snapshot.Append("rows ").Append(schema).Append('.').Append(name).Append(' ').AppendLine(rows);
        }

        // This database's locks only: other tests' databases in the same server take their own.
        var locks = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "select count(*) from pg_locks where locktype = 'advisory' and granted"
            + " and database = (select oid from pg_database where datname = current_database())", cancellationToken: ct)).ConfigureAwait(false);
        return snapshot.Append("advisory locks ").Append(locks).ToString();
    }

    private static async Task<string> TakeSqliteAsync(DbConnection connection, CancellationToken ct)
    {
        var snapshot = new StringBuilder();
        var objects = (await connection.QueryAsync<(string Type, string Name, string? Sql)>(new CommandDefinition(
            "select type, name, sql from sqlite_master order by type, name", cancellationToken: ct)).ConfigureAwait(false)).ToList();
        foreach (var (type, name, sql) in objects)
            snapshot.Append(type).Append(' ').Append(name).Append(' ').AppendLine(sql);

        foreach (var (_, name, _) in objects.Where(item => item.Type == "table" && !item.Name.StartsWith("sqlite_", StringComparison.Ordinal)))
        {
            var rows = await connection.QueryAsync(new CommandDefinition($"select * from \"{name}\"", cancellationToken: ct)).ConfigureAwait(false);
            var text = rows.Select(row => string.Join(",", ((IDictionary<string, object?>)row).Select(cell => $"{cell.Key}={cell.Value}")))
                .Order(StringComparer.Ordinal);
            snapshot.Append("rows ").Append(name).Append(' ').AppendLine(string.Join("|", text));
        }

        return snapshot.ToString();
    }
}
