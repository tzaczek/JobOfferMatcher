using System.Net;
using JobOfferMatcher.Application.Backup;
using JobOfferMatcher.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace JobOfferMatcher.Infrastructure.Tests.Backup;

/// <summary>
/// US2 real-Postgres validation test (003 T026, FR-011): a corrupt / non-backup / truncated / tampered
/// archive is refused <b>before any write</b> — the response is an error and live data is untouched.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RestoreValidationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Corrupt_and_tampered_uploads_are_refused_with_live_data_intact()
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

        var goodArchive = await client.GetByteArrayAsync("/api/backup");

        // 1. Not a zip at all.
        await ExpectRefused(client, [1, 2, 3, 4, 5, 6, 7, 8]);

        // 2. A zip with no manifest.json.
        await ExpectRefused(client, BackupTestSupport.Rebuild(goodArchive, dropEntries: new HashSet<string> { "manifest.json" }));

        // 3. A truncated archive (first half of a valid one).
        await ExpectRefused(client, goodArchive[..(goodArchive.Length / 2)]);

        // 4. A tampered CV file (SHA-256 mismatch).
        var tampered = BackupTestSupport.Rebuild(goodArchive, transformEntry: (name, bytes) =>
        {
            if (name.StartsWith("cv-data/", StringComparison.Ordinal) && bytes.Length > 0)
            {
                bytes[0] ^= 0xFF;
            }

            return bytes;
        });
        await ExpectRefused(client, tampered);

        // Live data is byte-identical after every rejected attempt.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var after = await BackupTestSupport.FingerprintAllAsync(db);
            foreach (var table in BackupTables.InsertOrder)
            {
                after[table].ShouldBe(before[table], $"table '{table}' changed despite a rejected restore");
            }
        }
    }

    private static async Task ExpectRefused(HttpClient client, byte[] payload)
    {
        using var content = BackupTestSupport.MultipartArchive(payload);
        var response = await client.PostAsync("/api/backup/restore", content);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
