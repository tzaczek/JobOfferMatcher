using System.Net;
using JobOfferMatcher.Infrastructure.Tests.Sources;

namespace JobOfferMatcher.Infrastructure.Tests.Backup;

/// <summary>
/// Loopback guard for the backup group (003 T046, Principle IV): the archive is the full DB + CV PII, so
/// every <c>/api/backup/*</c> route rejects a non-loopback caller with 403 <c>LoopbackOnly</c> (fail-closed).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BackupLoopbackTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Non_loopback_callers_are_rejected_on_every_backup_route()
    {
        await postgres.ResetAsync();
        await using var factory = new JobApiFactory(
            postgres.ConnectionString, new MutableJustJoinItClient(), simulateLoopback: false);
        var client = factory.CreateClient();

        (await client.GetAsync("/api/backup")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var inspect = BackupTestSupport.MultipartArchive([1, 2, 3]);
        (await client.PostAsync("/api/backup/inspect", inspect)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var restore = BackupTestSupport.MultipartArchive([1, 2, 3]);
        (await client.PostAsync("/api/backup/restore", restore)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
