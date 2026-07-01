using System.Net;
using System.Text;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Salary;
using JobOfferMatcher.Domain.TailoredCvs;
using JobOfferMatcher.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobOfferMatcher.Infrastructure.Tests.Backup;

/// <summary>
/// 004 backup/restore round-trip (T048, FR-017): a <c>tailored_cv</c> row <b>and</b> its flat
/// <c>cv-data/tailored-*.html/.pdf</c> files survive a full backup → wipe → restore, byte-identical —
/// proving the new table (added to <c>BackupTables.InsertOrder</c>) and the flat-file storage (ADR-3)
/// are covered by 003's existing archive + atomic swap with no changes to its file handling.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BackupRestoreTailoredCvTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Backup_restore_round_trips_the_tailored_cv_row_and_its_flat_files()
    {
        await postgres.ResetAsync();
        var (factory, cvDir, _) = BackupTestSupport.NewFactory(postgres.ConnectionString);
        await using var _f = factory;
        var client = factory.CreateClient();

        OfferId offerId;
        var htmlBytes = Encoding.UTF8.GetBytes("<!doctype html><html><body>Tailored CV</body></html>");
        byte[] pdfBytes = [0x25, 0x50, 0x44, 0x46, 9, 8, 7, 6]; // "%PDF" + bytes
        string htmlName, pdfName;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var offer = BackupTestSupport.OfferWithSalary("tcv-backup", Currency.Pln, 18000m, 24000m);
            db.Offers.Add(offer);
            offerId = offer.Id;

            htmlName = $"tailored-{offerId.Value:N}.html";
            pdfName = $"tailored-{offerId.Value:N}.pdf";

            var row = TailoredCv.CreateRequest(offerId, CvId.New(), "tailor it", ["C#", "PostgreSQL"], DateTimeOffset.UtcNow);
            row.MarkProduced(1, htmlName, pdfName, DateTimeOffset.UtcNow);
            db.TailoredCvs.Add(row);
            await db.SaveChangesAsync();

            await File.WriteAllBytesAsync(Path.Combine(cvDir, htmlName), htmlBytes);
            await File.WriteAllBytesAsync(Path.Combine(cvDir, pdfName), pdfBytes);
        }

        var archive = await client.GetByteArrayAsync("/api/backup");

        // Wipe both stores: drop the tailored row + delete its files.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TailoredCvs.Remove(await db.TailoredCvs.SingleAsync(t => t.OfferId == offerId));
            await db.SaveChangesAsync();
        }

        File.Delete(Path.Combine(cvDir, htmlName));
        File.Delete(Path.Combine(cvDir, pdfName));

        // Restore.
        using var content = BackupTestSupport.MultipartArchive(archive);
        var response = await client.PostAsync("/api/backup/restore", content);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The row + both files come back exactly.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.TailoredCvs.AsNoTracking().SingleAsync(t => t.OfferId == offerId);
            row.State.ShouldBe(TailoredCvState.Produced);
            row.EmphasisedSkills.ShouldBe(["C#", "PostgreSQL"]);
            row.HtmlFileName.ShouldBe(htmlName);
            row.PdfFileName.ShouldBe(pdfName);
        }

        (await File.ReadAllBytesAsync(Path.Combine(cvDir, htmlName))).ShouldBe(htmlBytes);
        (await File.ReadAllBytesAsync(Path.Combine(cvDir, pdfName))).ShouldBe(pdfBytes);
    }
}
