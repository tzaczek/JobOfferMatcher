using System.Net;
using System.Net.Http.Json;
using JobOfferMatcher.Application.Backup;
using JobOfferMatcher.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace JobOfferMatcher.Infrastructure.Tests.Backup;

/// <summary>
/// US3 real-Postgres inspect test (003 T041, SC-008): a valid backup → <c>POST /api/backup/inspect</c>
/// returns per-table counts + source version + compatibility with <b>no change to live data</b>; a
/// corrupt/non-backup upload is rejected as <c>InvalidArchive</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BackupInspectTests(PostgresFixture postgres)
{
    private sealed record InspectionDto(
        bool Valid,
        DateTimeOffset CreatedAtUtc,
        string AppProductVersion,
        string MigrationTip,
        string Compatibility,
        Dictionary<string, long> TableCounts,
        int CvFileCount,
        long TotalCvBytes,
        string[] Warnings);

    [Fact]
    public async Task Inspect_returns_a_summary_without_touching_live_data()
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

        var archive = await client.GetByteArrayAsync("/api/backup");

        using var content = BackupTestSupport.MultipartArchive(archive);
        var response = await client.PostAsync("/api/backup/inspect", content);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<InspectionDto>();
        dto!.Valid.ShouldBeTrue();
        dto.Compatibility.ShouldBe("Same");
        dto.TableCounts["offers"].ShouldBe(2);
        dto.CvFileCount.ShouldBe(1);
        dto.MigrationTip.ShouldNotBeNullOrWhiteSpace();

        // Read-only: live data is unchanged.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var after = await BackupTestSupport.FingerprintAllAsync(db);
            foreach (var table in BackupTables.InsertOrder)
            {
                after[table].ShouldBe(before[table], $"table '{table}' changed during inspect");
            }
        }
    }

    [Fact]
    public async Task Inspect_rejects_a_non_backup_upload()
    {
        await postgres.ResetAsync();
        var (factory, _, _) = BackupTestSupport.NewFactory(postgres.ConnectionString);
        await using var _f = factory;
        var client = factory.CreateClient();

        using var content = BackupTestSupport.MultipartArchive([9, 8, 7, 6, 5, 4, 3, 2, 1]);
        var response = await client.PostAsync("/api/backup/inspect", content);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
