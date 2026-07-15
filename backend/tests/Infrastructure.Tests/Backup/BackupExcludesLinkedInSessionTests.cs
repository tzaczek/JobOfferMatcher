using System.Net;
using System.Net.Http.Json;
using JobOfferMatcher.Infrastructure.Persistence.Seed;
using JobOfferMatcher.Infrastructure.Tests.Backup;
using JobOfferMatcher.Infrastructure.Tests.Sources;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Feature 008, US3 (T032, real Postgres): a backup covers the LinkedIn source <b>config</b> (a row in
/// the already-backed-up <c>job_source</c> table — restored 1:1) but the archive <b>never</b> contains the
/// on-disk browser session (cookies), which lives outside <c>cv-data/</c> and the DB (FR-012a). The user
/// re-logs-in after a restore.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BackupExcludesLinkedInSessionTests(PostgresFixture postgres)
{
    private static readonly string LinkedInSourceId = DatabaseSeeder.DefaultLinkedInSourceId.Value.ToString();

    [Fact]
    public async Task Backup_restores_the_config_but_the_archive_excludes_the_browser_session()
    {
        await postgres.ResetAsync();

        var cvDir = BackupTestSupport.NewTempDir("cv");
        var backupDir = BackupTestSupport.NewTempDir("backup");
        var profileDir = BackupTestSupport.NewTempDir("linkedin-profile");
        // A session cookie the backup must NEVER capture (Principle IV / FR-012a).
        await File.WriteAllTextAsync(Path.Combine(profileDir, "Cookies"), "li_at=super-secret-session");

        var settings = new Dictionary<string, string?>
        {
            ["Cv:StoragePath"] = cvDir,
            ["Backup:StoragePath"] = backupDir,
            ["Sources:LinkedIn:ProfilePath"] = profileDir,
        };
        await using var factory = new JobApiFactory(postgres.ConnectionString, new MutableJustJoinItClient(), settings: settings);
        var client = factory.CreateClient();

        var archive = await client.GetByteArrayAsync("/api/backup");

        // The session profile dir / cookie file appear in NO archive entry.
        using (var zip = BackupTestSupport.OpenZip(archive))
        {
            zip.GetEntry("db/job_source.copy").ShouldNotBeNull(); // config IS captured
            var names = zip.Entries.Select(e => e.FullName).ToList();
            names.ShouldNotContain(n => n.Contains("browser-profiles", StringComparison.OrdinalIgnoreCase));
            names.ShouldNotContain(n => n.Contains("Cookies", StringComparison.Ordinal));
            names.ShouldNotContain(n => n.Contains("linkedin-profile", StringComparison.Ordinal));
        }
        // The session on disk is left untouched by the backup.
        File.Exists(Path.Combine(profileDir, "Cookies")).ShouldBeTrue();

        // Drift the LinkedIn config, then restore → the backed-up config comes back (1:1 via job_source).
        (await client.PutAsJsonAsync($"/api/sources/{LinkedInSourceId}", new
        {
            name = "LinkedIn (edited)",
            searchCriteria = new { includeRecommended = false, linkedInSearches = Array.Empty<object>() },
            requiresLogin = false,
        })).EnsureSuccessStatusCode();

        using (var content = BackupTestSupport.MultipartArchive(archive))
        {
            (await client.PostAsync("/api/backup/restore", content)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var sources = await client.GetFromJsonAsync<SourcesEnvelope>("/api/sources");
        var linkedIn = sources!.Data.Single(s => s.Id == LinkedInSourceId);
        linkedIn.Name.ShouldBe("LinkedIn"); // the pre-drift name restored
        linkedIn.RequiresLogin.ShouldBeTrue();
        linkedIn.SearchCriteria.IncludeRecommended.ShouldBeTrue();
        linkedIn.SearchCriteria.LinkedInSearches.ShouldContain(s => s.Keywords == "Senior .NET Software Engineer");
    }

    private sealed record SourcesEnvelope(List<SourceItem> Data);
    private sealed record SourceItem(string Id, string Name, bool RequiresLogin, CriteriaItem SearchCriteria);
    private sealed record CriteriaItem(bool IncludeRecommended, List<LinkedInSearchItem> LinkedInSearches);
    private sealed record LinkedInSearchItem(string Keywords);
}
