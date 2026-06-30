namespace JobOfferMatcher.Application.Backup;

/// <summary>
/// Port over the database half of a backup (003 ADR-1): an in-process Npgsql <c>COPY</c> logical
/// snapshot/restore that copies each column as its raw on-disk text — sidestepping every EF value
/// converter — and works identically in host and container run modes (no <c>pg_dump</c>/docker).
/// </summary>
public interface IDatabaseSnapshotStore
{
    /// <summary>
    /// Open one <c>REPEATABLE READ READ ONLY</c> transaction and <c>COPY &lt;table&gt; (&lt;cols&gt;) TO
    /// STDOUT</c> (text) for each requested table, returning the column list, row count, and payload per
    /// table — a single MVCC point-in-time across all tables (non-destructive: live data is only read).
    /// </summary>
    Task<IReadOnlyList<TableSnapshot>> SnapshotAsync(IReadOnlyList<string> tables, CancellationToken ct = default);

    /// <summary>
    /// In one transaction: <c>TRUNCATE</c> all data tables (RESTART IDENTITY CASCADE, excluding
    /// <c>__EFMigrationsHistory</c>) then <c>COPY &lt;table&gt; (&lt;cols&gt;) FROM STDIN</c> per table in
    /// the given order using each table's recorded column list. <paramref name="beforeCommit"/> runs
    /// inside the transaction immediately before <c>COMMIT</c> (the caller performs the atomic
    /// <c>cv-data</c> swap there) so a swap failure rolls the database back. Any exception rolls back.
    /// </summary>
    Task RestoreAsync(
        IReadOnlyList<TableRestore> tables,
        Func<CancellationToken, Task> beforeCommit,
        CancellationToken ct = default);
}

/// <summary>A single table's snapshot: name, explicit ordered columns, row count, and the raw COPY-text payload.</summary>
public sealed record TableSnapshot(string Name, IReadOnlyList<string> Columns, long RowCount, byte[] Data);

/// <summary>A single table to reload: name, the backup's recorded column list, and the raw COPY-text payload.</summary>
public sealed record TableRestore(string Name, IReadOnlyList<string> Columns, byte[] Data);
