using JobOfferMatcher.Application.Backup;
using JobOfferMatcher.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace JobOfferMatcher.Infrastructure.Tests.Backup;

/// <summary>
/// US1 real-Postgres backup test (003 T014, FR-001/002/004, SC-001): <c>GET /api/backup</c> produces a
/// complete archive — a manifest, a <c>db/*.copy</c> for all data tables, and every CV file with a
/// matching SHA-256. An empty data set still produces a valid archive. Plus the non-destructive
/// guarantee (T015 / SC-002): live data is byte-identical before and after a backup.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BackupCreateTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Backup_archive_contains_all_tables_and_every_cv_with_matching_hash()
    {
        await postgres.ResetAsync();
        var (factory, cvDir, _) = BackupTestSupport.NewFactory(postgres.ConnectionString);
        await using var _f = factory;
        var client = factory.CreateClient();

        BackupTestSupport.SeededData seeded;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            seeded = await BackupTestSupport.SeedAsync(db, cvDir);
        }

        var bytes = await client.GetByteArrayAsync("/api/backup");
        using var zip = BackupTestSupport.OpenZip(bytes);

        // Manifest present and lists all data tables (each with columns + row count).
        zip.GetEntry("manifest.json").ShouldNotBeNull();
        var manifest = ReadManifest(zip);
        manifest.Tables.Select(t => t.Name).ShouldBe(BackupTables.InsertOrder, ignoreOrder: true);
        manifest.Tables.ShouldAllBe(t => t.Columns.Count > 0);

        // A db/<table>.copy for every table.
        foreach (var table in BackupTables.InsertOrder)
        {
            zip.GetEntry($"db/{table}.copy").ShouldNotBeNull();
        }

        // The CV file is present with a matching SHA-256 (FR-002).
        var cvEntry = manifest.CvFiles.Single();
        cvEntry.Name.ShouldBe(seeded.CvFileName);
        manifest.CvFileCount.ShouldBe(1);
        var cvBytes = BackupTestSupport.EntryBytes(zip, $"cv-data/{seeded.CvFileName}");
        cvBytes.ShouldBe(seeded.CvBytes);
        BackupHashing.Sha256Hex(cvBytes).ShouldBe(cvEntry.Sha256);

        // The offers table really captured the seeded rows (per-table row count in the manifest, FR-005).
        manifest.Tables.Single(t => t.Name == "offers").RowCount.ShouldBe(2);
    }

    [Fact]
    public async Task Empty_data_set_still_produces_a_valid_archive()
    {
        await postgres.ResetAsync();
        var (factory, _, _) = BackupTestSupport.NewFactory(postgres.ConnectionString);
        await using var _f = factory;
        var client = factory.CreateClient();

        // No offers, no CV uploaded (startup only seeds the singleton/source defaults).
        var bytes = await client.GetByteArrayAsync("/api/backup");
        using var zip = BackupTestSupport.OpenZip(bytes);

        var manifest = ReadManifest(zip);
        manifest.Tables.Select(t => t.Name).ShouldBe(BackupTables.InsertOrder, ignoreOrder: true);
        manifest.CvFileCount.ShouldBe(0);
        manifest.Tables.Single(t => t.Name == "offers").RowCount.ShouldBe(0);
        zip.GetEntry("db/offers.copy").ShouldNotBeNull();
        zip.Entries.Count(e => e.FullName.StartsWith("cv-data/", StringComparison.Ordinal)).ShouldBe(0);
    }

    [Fact]
    public async Task Backup_is_non_destructive_data_is_identical_before_and_after()
    {
        await postgres.ResetAsync();
        var (factory, cvDir, _) = BackupTestSupport.NewFactory(postgres.ConnectionString);
        await using var _f = factory;
        var client = factory.CreateClient();

        IReadOnlyDictionary<string, (long, string)> before;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await BackupTestSupport.SeedAsync(db, cvDir);
            before = await BackupTestSupport.FingerprintAllAsync(db);
        }

        _ = await client.GetByteArrayAsync("/api/backup");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var after = await BackupTestSupport.FingerprintAllAsync(db);
            foreach (var table in BackupTables.InsertOrder)
            {
                after[table].ShouldBe(before[table], $"table '{table}' changed during a backup");
            }
        }
    }

    private static BackupManifest ReadManifest(System.IO.Compression.ZipArchive zip)
    {
        var bytes = BackupTestSupport.EntryBytes(zip, "manifest.json");
        return System.Text.Json.JsonSerializer.Deserialize<BackupManifest>(
            bytes,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })!;
    }
}
