using JobOfferMatcher.Application.TailoredCvs;
using JobOfferMatcher.Domain.Cv;
using JobOfferMatcher.Infrastructure.Persistence;
using JobOfferMatcher.Infrastructure.Tests.Sources;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobOfferMatcher.Infrastructure.Tests.TailoredCvs;

/// <summary>
/// Scaffolding for the 004 tailored-CV real-Postgres flow tests: a host factory with an isolated
/// <c>cv-data</c> dir and the <b>real Playwright renderer swapped for a deterministic fake</b> (so the DB
/// suite stays offline — the real adapter is covered by <c>PdfRendererTests</c>), plus the wire DTOs.
/// </summary>
internal static class TailoredCvFlow
{
    /// <summary>A deterministic stand-in for <see cref="IPdfRenderer"/> — returns a minimal valid PDF byte string.</summary>
    public sealed class FakePdfRenderer : IPdfRenderer
    {
        public Task<byte[]> RenderA4Async(string html, CancellationToken ct = default) =>
            Task.FromResult(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 fake-rendered"));
    }

    public static (JobApiFactory Factory, string CvDir) NewFactory(string connectionString)
    {
        var cvDir = Path.Combine(Path.GetTempPath(), $"jobs-tcv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cvDir);
        var settings = new Dictionary<string, string?> { ["Cv:StoragePath"] = cvDir };
        return (new JobApiFactory(connectionString, new MutableJustJoinItClient(), settings: settings), cvDir);
    }

    /// <summary>A host whose real Playwright renderer is replaced by <see cref="FakePdfRenderer"/> (seed + call via the same provider).</summary>
    public static WebApplicationFactory<Program> WithFakeRenderer(JobApiFactory factory) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPdfRenderer>();
            services.AddSingleton<IPdfRenderer, FakePdfRenderer>();
        }));

    /// <summary>A readable, produced CV row + its PDF file on disk (so the source-CV path resolves).</summary>
    public static CandidateCv AddReadableCvWithFile(AppDbContext db, string cvDir)
    {
        var cv = EnrichmentFlow.AddProducedCv(db); // FileName "cv.pdf", readable, produced profile
        File.WriteAllBytes(Path.Combine(cvDir, cv.FileName), [0x25, 0x50, 0x44, 0x46, 1, 2, 3, 4]); // "%PDF" + bytes
        return cv;
    }

    // ---- Wire DTOs (PascalCase binds the camelCase contract via the host's case-insensitive JSON) ----

    public sealed record SourceCvDto(Guid Id, string FileName);

    public sealed record DraftDto(
        Guid OfferId,
        string OfferTitle,
        string Company,
        string Prompt,
        List<string> EmphasisedSkills,
        List<string> AllOfferSkills,
        SourceCvDto? SourceCv);

    public sealed record ViewDto(
        Guid OfferId,
        string OfferTitle,
        string Company,
        Guid SourceCvId,
        string State,
        int GenerationVersion,
        List<string> EmphasisedSkills,
        string Prompt,
        bool HasPdf,
        DateTimeOffset? GeneratedAt,
        string? LastError);

    public sealed record ListEnvelope(List<ViewDto> Data);

    public sealed record PendingMetaDto(int PendingTotal, int FailedTotal, int Returned, int RetryLimit);

    public sealed record SourceCvWireDto(string Path, string FileName, bool Readable, string? FallbackText);

    public sealed record OfferWireDto(string Title, string Company, string? Seniority, List<string> RequiredSkills, List<string> NiceToHaveSkills);

    public sealed record PendingItemDto(
        string WorkItemId,
        Guid OfferId,
        int GenerationVersion,
        string Prompt,
        List<string> EmphasisedSkills,
        OfferWireDto Offer,
        SourceCvWireDto SourceCv);

    public sealed record PendingEnvelope(PendingMetaDto Meta, List<PendingItemDto> Items);

    public sealed record OutcomeDto(string WorkItemId, string Outcome, int Attempt, string State);

    public sealed record SubmitEnvelope(int Accepted, int Rejected, List<OutcomeDto> Results);
}
