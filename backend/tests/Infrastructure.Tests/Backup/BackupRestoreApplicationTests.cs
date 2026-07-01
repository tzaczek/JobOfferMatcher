using System.Net;
using System.Text;
using JobOfferMatcher.Domain.Applications;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Salary;
using JobOfferMatcher.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobOfferMatcher.Infrastructure.Tests.Backup;

/// <summary>
/// 005 backup/restore round-trip (T061, FR-014): a <c>JobApplication</c> row + its child rows (a note) and
/// its flat <c>cv-data/appdoc-*</c> document file all survive a full backup → wipe → restore, byte-identical
/// — proving the 7 new tables (added to <c>BackupTables.InsertOrder</c> in FK order) and the flat-file
/// storage (ADR-4) are covered by 003's existing archive + atomic swap with no changes to its file handling.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BackupRestoreApplicationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Backup_restore_round_trips_the_application_subtree_and_its_document_files()
    {
        await postgres.ResetAsync();
        var (factory, cvDir, _) = BackupTestSupport.NewFactory(postgres.ConnectionString);
        await using var _f = factory;
        var client = factory.CreateClient();

        OfferId offerId;
        var docBytes = Encoding.UTF8.GetBytes("resume bytes — must survive byte-identical");
        var docId = ApplicationDocumentId.New();
        var storedName = $"appdoc-{docId.Value:N}.pdf";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var offer = BackupTestSupport.OfferWithSalary("app-backup", Currency.Pln, 18000m, 24000m);
            db.Offers.Add(offer);
            offerId = offer.Id;

            var firstStage = await db.PipelineStages.OrderBy(s => s.Position).FirstAsync();
            var now = DateTimeOffset.UtcNow;
            db.Applications.Add(JobApplication.Create(offerId, firstStage.Id, now, now));
            db.ApplicationNotes.Add(ApplicationNote.Create(offerId, "applied via referral", now).Value);
            db.ApplicationDocuments.Add(
                ApplicationDocument.Create(docId, offerId, storedName, "resume.pdf", "application/pdf", docBytes.Length, now));
            await db.SaveChangesAsync();

            await File.WriteAllBytesAsync(Path.Combine(cvDir, storedName), docBytes);
        }

        var archive = await client.GetByteArrayAsync("/api/backup");

        // Wipe both stores: drop the application (cascades to note + document rows) + delete the file.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Applications.Remove(await db.Applications.SingleAsync(a => a.OfferId == offerId));
            await db.SaveChangesAsync();
            (await db.ApplicationNotes.CountAsync()).ShouldBe(0);
            (await db.ApplicationDocuments.CountAsync()).ShouldBe(0);
        }

        File.Delete(Path.Combine(cvDir, storedName));

        // Restore.
        using var content = BackupTestSupport.MultipartArchive(archive);
        (await client.PostAsync("/api/backup/restore", content)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The application subtree + the flat document file come back exactly.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Applications.AsNoTracking().SingleAsync(a => a.OfferId == offerId)).Status.ShouldBe(ApplicationStatus.Active);
            (await db.ApplicationNotes.AsNoTracking().SingleAsync(n => n.OfferId == offerId)).Body.ShouldBe("applied via referral");
            var doc = await db.ApplicationDocuments.AsNoTracking().SingleAsync(d => d.OfferId == offerId);
            doc.StoredFileName.ShouldBe(storedName);
            doc.OriginalFileName.ShouldBe("resume.pdf");
        }

        (await File.ReadAllBytesAsync(Path.Combine(cvDir, storedName))).ShouldBe(docBytes);
    }
}
