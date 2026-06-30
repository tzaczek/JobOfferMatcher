using System.Data;
using System.Text;
using JobOfferMatcher.Application.Backup;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace JobOfferMatcher.Infrastructure.Backup;

/// <summary>
/// In-process Npgsql <c>COPY</c> logical snapshot/restore (003 ADR-1). Snapshot reads every table with
/// <c>COPY … TO STDOUT</c> (text) inside one <c>REPEATABLE READ READ ONLY</c> transaction — a single MVCC
/// point-in-time that copies each column as its raw on-disk text, sidestepping every EF value converter.
/// Restore wipes + reloads in one transaction with the <c>cv-data</c> swap performed just before
/// <c>COMMIT</c>. Uses its own connection (string from <c>ConnectionStrings:AppDb</c>) so it never
/// interferes with EF's transaction tracking; works identically in host and container modes.
/// </summary>
public sealed class PostgresSnapshotStore : IDatabaseSnapshotStore
{
    private readonly string _connectionString;

    public PostgresSnapshotStore(IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("AppDb")
            ?? throw new InvalidOperationException("Connection string 'AppDb' is not configured.");
    }

    public async Task<IReadOnlyList<TableSnapshot>> SnapshotAsync(IReadOnlyList<string> tables, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        // Advisory: the snapshot only reads. REPEATABLE READ gives the consistent cross-table point-in-time.
        await ExecuteAsync(conn, tx, "SET TRANSACTION READ ONLY", ct);

        var result = new List<TableSnapshot>(tables.Count);
        foreach (var table in tables)
        {
            var columns = await GetColumnsAsync(conn, tx, table, ct);
            var rowCount = await CountRowsAsync(conn, tx, table, ct);
            var data = await CopyOutAsync(conn, table, columns, ct);
            result.Add(new TableSnapshot(table, columns, rowCount, data));
        }

        await tx.CommitAsync(ct);
        return result;
    }

    public async Task RestoreAsync(
        IReadOnlyList<TableRestore> tables,
        Func<CancellationToken, Task> beforeCommit,
        CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Wipe every current data table (not just the backup's) so a table absent from an OLDER backup
        // ends up empty rather than stale. RESTART IDENTITY CASCADE; __EFMigrationsHistory is excluded.
        var truncateList = string.Join(", ", BackupTables.InsertOrder.Select(Quote));
        await ExecuteAsync(conn, tx, $"TRUNCATE {truncateList} RESTART IDENTITY CASCADE", ct);

        // Reload in dependency order using each table's recorded column list (newer columns absent from
        // an older backup are simply not listed, so Postgres applies their DDL defaults — FR-017).
        foreach (var table in tables)
        {
            var columnList = string.Join(", ", table.Columns.Select(Quote));
            var command = $"COPY {Quote(table.Name)} ({columnList}) FROM STDIN (FORMAT text)";
            await using var writer = await conn.BeginTextImportAsync(command, ct);
            await writer.WriteAsync(Encoding.UTF8.GetString(table.Data));
            // Disposing the writer (end of `await using` block) sends CopyDone and completes the COPY.
        }

        // The cv-data directory swap happens here — inside the transaction, before COMMIT — so a swap
        // failure rolls the database back too (all-or-nothing across both stores).
        await beforeCommit(ct);

        await tx.CommitAsync(ct);
    }

    private static async Task<byte[]> CopyOutAsync(NpgsqlConnection conn, string table, IReadOnlyList<string> columns, CancellationToken ct)
    {
        var columnList = string.Join(", ", columns.Select(Quote));
        var command = $"COPY {Quote(table)} ({columnList}) TO STDOUT (FORMAT text)";
        await using var reader = await conn.BeginTextExportAsync(command, ct);
        var text = await reader.ReadToEndAsync(ct);
        return Encoding.UTF8.GetBytes(text);
    }

    private static async Task<IReadOnlyList<string>> GetColumnsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string table, CancellationToken ct)
    {
        const string sql =
            "SELECT column_name FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND table_name = @t ORDER BY ordinal_position";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("@t", table);
        var columns = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            columns.Add(reader.GetString(0));
        }

        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"Table '{table}' has no columns (does it exist?).");
        }

        return columns;
    }

    private static async Task<long> CountRowsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string table, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {Quote(table)}", conn, tx);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        return scalar is long l ? l : 0L;
    }

    private static async Task ExecuteAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Quote a Postgres identifier. Names come from the fixed <see cref="BackupTables"/> inventory + the
    /// DB catalog (not user input), but quoting keeps the COPY commands well-formed regardless.</summary>
    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
