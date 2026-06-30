using System.IO.Compression;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Cv;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.RoleGroups;
using JobOfferMatcher.Domain.Salary;
using JobOfferMatcher.Infrastructure.Persistence;
using JobOfferMatcher.Infrastructure.Tests.Sources;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Tests.Backup;

/// <summary>
/// Shared scaffolding for the 003 backup/restore real-Postgres tests: a host factory with isolated
/// on-disk CV + backup dirs, a seed that exercises every tricky column (salary_bands Currency,
/// member_offer_ids jsonb, enum/owned/jsonb columns + a real CV file), per-table fingerprints for the
/// non-destructive / round-trip asserts, and a zip reader.
/// </summary>
internal static class BackupTestSupport
{
    public sealed record SeededData(CvId CvId, string CvFileName, byte[] CvBytes, OfferId OfferA, OfferId OfferB);

    public static (JobApiFactory Factory, string CvDir, string BackupDir) NewFactory(string connectionString)
    {
        var cvDir = NewTempDir("cv");
        var backupDir = NewTempDir("backup");
        var settings = new Dictionary<string, string?>
        {
            ["Cv:StoragePath"] = cvDir,
            ["Backup:StoragePath"] = backupDir,
        };
        var factory = new JobApiFactory(connectionString, new MutableJustJoinItClient(), settings: settings);
        return (factory, cvDir, backupDir);
    }

    public static string NewTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"jobs-{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Seed two offers (one with a salary band carrying a <see cref="Currency"/>, produced
    /// enrichment + fit), a role group grouping them (member_offer_ids jsonb), and a real CV file on
    /// disk + its candidate_cv row — covering the converter/jsonb/owned-type hazards a COPY must survive.
    /// </summary>
    public static async Task<SeededData> SeedAsync(AppDbContext db, string cvDir)
    {
        var offerA = OfferWithSalary("backup-a", Currency.Pln, 18000m, 24000m);
        var offerB = OfferWithSalary("backup-b", Currency.Eur, 5000m, 7000m);
        db.Offers.Add(offerA);
        db.Offers.Add(offerB);

        var enrichA = OfferEnrichment.CreatePending(offerA.Id);
        enrichA.MarkProduced("A concise summary.", ["C#", "EF Core", ".NET"], "SHA256:1:abc", DateTimeOffset.UtcNow);
        db.OfferEnrichments.Add(enrichA);
        db.OfferEnrichments.Add(OfferEnrichment.CreatePending(offerB.Id));

        var fitA = OfferFit.CreatePending(offerA.Id);
        fitA.MarkProduced(82, ["C#", "EF Core"], ["Kafka"], "Strong backend match.", "SHA256:1:def", DateTimeOffset.UtcNow);
        db.OfferFits.Add(fitA);
        db.OfferFits.Add(OfferFit.CreatePending(offerB.Id));

        var group = RoleGroup.Create(RoleGroupId.New(), offerA.Id, new MatchConfidence(0.9));
        group.AddMember(offerB.Id, new MatchConfidence(0.8));
        db.RoleGroups.Add(group);

        var cvId = CvId.New();
        var cvFileName = $"{cvId.Value:N}.pdf";
        var cvBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 1, 2, 3, 4, 5, 6, 7, 8 }; // "%PDF" + bytes
        await File.WriteAllBytesAsync(Path.Combine(cvDir, cvFileName), cvBytes);

        var cv = CandidateCv.Create(cvId, cvFileName);
        cv.SetExtractionGauge(true, "SHA256:1:bytes", DateTimeOffset.UtcNow);
        cv.ApplyProfile(new CvProfile(["C#", "Azure"], "Senior", "Backend engineer."), "SHA256:1:bytes", DateTimeOffset.UtcNow);
        db.CandidateCvs.Add(cv);

        await db.SaveChangesAsync();
        return new SeededData(cvId, cvFileName, cvBytes, offerA.Id, offerB.Id);
    }

    public static Offer OfferWithSalary(string nativeKey, Currency currency, decimal min, decimal max)
    {
        var content = new OfferContent
        {
            Title = $"Backend Engineer {nativeKey}",
            Company = "Acme",
            Location = "Kraków",
            CanonicalUrl = $"https://example.test/o/{nativeKey}",
            WorkMode = WorkMode.Hybrid,
            RequiredSkills = ["C#", ".NET"],
            NiceToHaveSkills = ["Kafka"],
            SalaryBands =
            [
                new SalaryBand
                {
                    AmountMin = min,
                    AmountMax = max,
                    Currency = currency,
                    Period = SalaryPeriod.Monthly,
                    Basis = EmploymentBasis.Permanent,
                    Tax = TaxTreatment.Gross,
                },
            ],
            DescriptionHtml = "<p>Build things with C# &amp; PostgreSQL.</p>",
            PublishedAt = new DateTimeOffset(2026, 6, 20, 8, 0, 0, TimeSpan.Zero),
        };
        var externalRef = ExternalRef.Create(SourceId.New(), nativeKey, IdentityKind.NativeId).Value;
        return Offer.Create(OfferId.New(), externalRef, content, ContentFingerprint.Compute(content), DateTimeOffset.UtcNow);
    }

    /// <summary>An order-independent per-table fingerprint (count + hash of sorted row hashes) for equality asserts.</summary>
    public static async Task<(long Count, string Hash)> FingerprintAsync(AppDbContext db, string table)
    {
        var sql =
            $"SELECT count(*)::bigint AS \"Count\", " +
            $"coalesce(md5(string_agg(rowhash, '' ORDER BY rowhash)), '') AS \"Hash\" " +
            $"FROM (SELECT md5(t::text) AS rowhash FROM \"{table}\" t) s";
        var row = await db.Database.SqlQueryRaw<TableFingerprint>(sql).SingleAsync();
        return (row.Count, row.Hash);
    }

    public static async Task<IReadOnlyDictionary<string, (long Count, string Hash)>> FingerprintAllAsync(AppDbContext db)
    {
        var map = new Dictionary<string, (long, string)>();
        foreach (var table in JobOfferMatcher.Application.Backup.BackupTables.InsertOrder)
        {
            map[table] = await FingerprintAsync(db, table);
        }

        return map;
    }

    public static MultipartFormDataContent MultipartArchive(byte[] archive, string fileName = "backup.zip")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(archive);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(file, "file", fileName);
        return content;
    }

    public static IConfiguration Config(string connectionString, string cvDir, string backupDir) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AppDb"] = connectionString,
                ["Cv:StoragePath"] = cvDir,
                ["Backup:StoragePath"] = backupDir,
            })
            .Build();

    public static ZipArchive OpenZip(byte[] bytes) => new(new MemoryStream(bytes), ZipArchiveMode.Read);

    public static byte[] EntryBytes(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName) ?? throw new InvalidOperationException($"Zip entry '{entryName}' not found.");
        return ReadEntry(entry);
    }

    private static readonly System.Text.Json.JsonSerializerOptions CamelCase =
        new() { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Rebuild an archive, optionally editing the manifest, dropping entries, or transforming entry bytes —
    /// used to synthesise corrupt / older / newer archives for the validation + cross-version tests.
    /// </summary>
    public static byte[] Rebuild(
        byte[] archive,
        Func<JobOfferMatcher.Application.Backup.BackupManifest, JobOfferMatcher.Application.Backup.BackupManifest>? editManifest = null,
        ISet<string>? dropEntries = null,
        Func<string, byte[], byte[]>? transformEntry = null)
    {
        using var src = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        var output = new MemoryStream();
        using (var dest = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in src.Entries)
            {
                if (dropEntries?.Contains(entry.FullName) == true)
                {
                    continue;
                }

                byte[] bytes;
                if (entry.FullName == "manifest.json" && editManifest is not null)
                {
                    var manifest = System.Text.Json.JsonSerializer.Deserialize<JobOfferMatcher.Application.Backup.BackupManifest>(ReadEntry(entry), CamelCase)!;
                    bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(editManifest(manifest), CamelCase);
                }
                else
                {
                    bytes = ReadEntry(entry);
                    if (transformEntry is not null)
                    {
                        bytes = transformEntry(entry.FullName, bytes);
                    }
                }

                var newEntry = dest.CreateEntry(entry.FullName);
                using var s = newEntry.Open();
                s.Write(bytes);
            }
        }

        return output.ToArray();
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private sealed record TableFingerprint(long Count, string Hash);
}
