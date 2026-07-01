using System.Text;
using JobOfferMatcher.Application.Offers;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Application.Settings;
using JobOfferMatcher.Application.TailoredCvs;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Cv;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Settings;
using JobOfferMatcher.Domain.TailoredCvs;
using Microsoft.Extensions.Time.Testing;

namespace JobOfferMatcher.Application.Tests;

/// <summary>Builders + a harness for TailoredCvService unit tests (T026/T034). Reuses the Enrichment fakes for the shared CV/settings/UoW ports.</summary>
internal static class TailoredCvDoubles
{
    public static readonly DateTimeOffset Now = new(2026, 6, 30, 12, 0, 0, TimeSpan.Zero);

    public static Offer MakeOffer(
        string key = "o1",
        string? seniority = "Senior",
        IReadOnlyList<string>? required = null,
        IReadOnlyList<string>? nice = null)
    {
        var content = new OfferContent
        {
            Title = $"Role {key}",
            Company = "Acme",
            CanonicalUrl = $"https://example.test/{key}",
            Seniority = seniority,
            RequiredSkills = required is null ? ["C#", ".NET"] : [.. required],
            NiceToHaveSkills = nice is null ? ["Docker"] : [.. nice],
            DescriptionHtml = "<p>Build.</p>",
            PublishedAt = Now,
        };
        var externalRef = ExternalRef.Create(SourceId.New(), key, IdentityKind.NativeId).Value;
        return Offer.Create(OfferId.New(), externalRef, content, ContentFingerprint.Compute(content), Now);
    }

    public static CandidateCv MakeCv(bool readable = true, string fileName = "cv.pdf", DateTimeOffset? uploadedAt = null)
    {
        var cv = CandidateCv.Create(CvId.New(), fileName);
        cv.SetExtractionGauge(readable, "SHA256:1:bytes", uploadedAt ?? Now);
        return cv;
    }

    public static OfferListItem ListItem(Offer offer, string enrichmentState, IReadOnlyList<string>? keySkills, FitView? fit) => new(
        OfferId: offer.Id.Value,
        RoleGroupId: null,
        Title: offer.Title,
        Company: offer.Company,
        Location: offer.Location,
        WorkMode: null,
        EmploymentType: offer.EmploymentType,
        Seniority: offer.Seniority,
        RequiredSkills: [.. offer.RequiredSkills],
        NiceToHaveSkills: [.. offer.NiceToHaveSkills],
        SalaryBands: [],
        NormalizedSalary: null,
        Summary: null,
        KeySkills: keySkills ?? [],
        EnrichmentState: enrichmentState,
        Fit: fit,
        FitState: fit?.State,
        Affinity: null,
        AffinityState: "insufficient",
        CanonicalUrl: offer.CanonicalUrl,
        IsNew: false,
        IsUpdated: false,
        Availability: "available",
        FirstSeenAt: Now,
        FirstSuggestedAt: Now,
        LastSeenAt: Now,
        PublishedAt: offer.PublishedAt,
        UserStatus: "new",
        Applied: false,
        AppliedAt: null,
        ApplicationNote: null,
        GroupMembers: []);

    public static OfferDetail Detail(Offer offer, string enrichmentState, IReadOnlyList<string>? keySkills, FitView? fit) =>
        new(ListItem(offer, enrichmentState, keySkills, fit), offer.DescriptionHtml, [], []);
}

internal sealed class TailoredCvHarness
{
    public FakeTailoredCvRepo Tailored { get; } = new();
    public FakeTailoredCvFileStore Files { get; } = new();
    public FakePdfRenderer Renderer { get; }
    public FakeOfferReads Reads { get; } = new();
    public FakeOfferRepo OfferRepo { get; } = new();
    public TailoredCvService Service { get; }

    public TailoredCvHarness(IReadOnlyList<CandidateCv>? cvs = null, bool renderThrows = false, int retryLimit = 3)
    {
        Renderer = new FakePdfRenderer(renderThrows);
        var settings = AppSettings.CreateDefault();
        settings.UpdateEnrichment(new EnrichmentSettings { RetryLimit = retryLimit });

        Service = new TailoredCvService(
            Tailored,
            Files,
            Renderer,
            Reads,
            OfferRepo,
            new FakeCvRepo([.. (cvs ?? [])]),
            new FakeFileStore(),
            new FakeExtractor(),
            new FakeSettingsRepo(settings),
            new FakeUnitOfWork(),
            new MaintenanceGate(new ScanConcurrencyGuard()),
            new FakeTimeProvider(TailoredCvDoubles.Now));
    }

    public Offer AddOffer(Offer offer, string enrichmentState = "pending", IReadOnlyList<string>? keySkills = null, FitView? fit = null)
    {
        OfferRepo.Offers[offer.Id] = offer;
        Reads.Details[offer.Id] = TailoredCvDoubles.Detail(offer, enrichmentState, keySkills, fit);
        return offer;
    }
}

internal sealed class FakeTailoredCvRepo : ITailoredCvRepository
{
    public Dictionary<OfferId, TailoredCv> Rows { get; } = [];

    public Task<TailoredCv?> GetByOfferAsync(OfferId offerId, CancellationToken ct = default) =>
        Task.FromResult(Rows.GetValueOrDefault(offerId));

    public Task<IReadOnlyList<TailoredCv>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TailoredCv>>(Rows.Values.ToList());

    public Task AddAsync(TailoredCv tailoredCv, CancellationToken ct = default)
    {
        Rows[tailoredCv.OfferId] = tailoredCv;
        return Task.CompletedTask;
    }

    public void Remove(TailoredCv tailoredCv) => Rows.Remove(tailoredCv.OfferId);

    public Task<IReadOnlyList<TailoredCv>> GetPendingAsync(int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TailoredCv>>(
            Rows.Values.Where(r => r.State == TailoredCvState.Pending).OrderBy(r => r.CreatedAt).Take(limit).ToList());

    public Task<TailoredCvCounts> GetCountsAsync(CancellationToken ct = default) =>
        Task.FromResult(new TailoredCvCounts(
            Rows.Values.Count(r => r.State == TailoredCvState.Pending),
            Rows.Values.Count(r => r.State == TailoredCvState.Failed),
            Rows.Values.Count(r => r.State == TailoredCvState.Produced)));
}

internal sealed class FakeTailoredCvFileStore : ITailoredCvFileStore
{
    public Dictionary<OfferId, (string Html, byte[] Pdf)> Saved { get; } = [];

    public Task<TailoredCvFiles> SaveAsync(OfferId offerId, string html, byte[] pdf, CancellationToken ct = default)
    {
        Saved[offerId] = (html, pdf);
        return Task.FromResult(new TailoredCvFiles($"tailored-{offerId.Value:N}.html", $"tailored-{offerId.Value:N}.pdf"));
    }

    public string GetPdfAbsolutePath(OfferId offerId) => $"/cv-data/tailored-{offerId.Value:N}.pdf";

    public string? GetHtml(OfferId offerId) => Saved.TryGetValue(offerId, out var files) ? files.Html : null;

    public void Delete(OfferId offerId) => Saved.Remove(offerId);
}

internal sealed class FakePdfRenderer(bool throws = false) : IPdfRenderer
{
    public int Calls { get; private set; }

    public Task<byte[]> RenderA4Async(string html, CancellationToken ct = default)
    {
        Calls++;
        return throws
            ? throw new InvalidOperationException("render boom")
            : Task.FromResult(Encoding.ASCII.GetBytes("%PDF-1.4 fake"));
    }
}

internal sealed class FakeOfferReads : IOfferReadService
{
    public Dictionary<OfferId, OfferDetail> Details { get; } = [];

    public Task<OfferListResult> ListAsync(OfferListFilter filter, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<OfferDetail?> GetAsync(OfferId id, CancellationToken ct = default) =>
        Task.FromResult(Details.GetValueOrDefault(id));
}

internal sealed class FakeOfferRepo : IOfferRepository
{
    public Dictionary<OfferId, Offer> Offers { get; } = [];

    public Task<Offer?> GetByIdAsync(OfferId id, CancellationToken ct = default) =>
        Task.FromResult(Offers.GetValueOrDefault(id));

    public Task<Offer?> GetByExternalRefAsync(SourceId sourceId, string nativeKey, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<Offer>> GetActiveBySourceAsync(SourceId sourceId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<Offer>> GetAllActiveAsync(CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task AddAsync(Offer offer, CancellationToken ct = default) => throw new NotSupportedException();

    public Task AddObservationAsync(OfferObservation observation, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task AddVersionAsync(OfferVersion version, CancellationToken ct = default) => throw new NotSupportedException();

    public Task AddEventAsync(OfferEvent offerEvent, CancellationToken ct = default) => throw new NotSupportedException();
}
