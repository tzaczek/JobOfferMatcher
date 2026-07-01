using JobOfferMatcher.Application.Backup;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Tests.Backup;

/// <summary>
/// Backup completeness guard (004 T019, FR-017): the set of mapped data-table names in the EF model
/// must equal <see cref="BackupTables.InsertOrder"/> (modulo <c>__EFMigrationsHistory</c>). Today
/// nothing else cross-checks the list, so a new table added without editing it would be <b>silently
/// omitted from every backup</b> with no failing test. This permanently protects FR-017 for this and
/// future tables — including the new <c>tailored_cv</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BackupTablesCompletenessTests(PostgresFixture postgres)
{
    [Fact]
    public void Every_mapped_data_table_is_in_the_backup_insert_order()
    {
        using var db = postgres.CreateContext();

        var modelTables = db.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .Where(name => name is not null && name != BackupTables.MigrationsHistoryTable)
            .Select(name => name!)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var backupTables = BackupTables.InsertOrder
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        modelTables.ShouldBe(
            backupTables,
            "every mapped data table must be in BackupTables.InsertOrder or it is silently dropped from backups (FR-017)");
    }
}
