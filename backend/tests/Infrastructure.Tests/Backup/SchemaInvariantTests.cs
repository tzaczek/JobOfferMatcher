using JobOfferMatcher.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Tests.Backup;

/// <summary>
/// Additivity guard (003 T028, data-model §3): the explicit-PK reload that restore relies on is only
/// sound while <b>no</b> table has a serial/identity column (no sequence to resync after a COPY with
/// explicit values). If a future migration adds one, this fails the build — not a user's restore.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SchemaInvariantTests(PostgresFixture postgres)
{
    [Fact]
    public async Task No_data_table_has_a_serial_or_identity_column()
    {
        await using var db = postgres.CreateContext();

        const string sql =
            "SELECT count(*)::int AS \"Value\" FROM information_schema.columns " +
            "WHERE table_schema = 'public' " +
            "AND table_name <> '__EFMigrationsHistory' " +
            "AND (is_identity = 'YES' OR column_default LIKE 'nextval(%')";

        var offenders = await db.Database.SqlQueryRaw<int>(sql).SingleAsync();
        offenders.ShouldBe(0, "a serial/identity column would break the explicit-PK restore invariant");
    }
}
