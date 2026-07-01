using JobOfferMatcher.Application.Abstractions;
using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Application.Enrichment;
using JobOfferMatcher.Application.Settings;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Cv;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Settings;
using Microsoft.Extensions.Time.Testing;

namespace JobOfferMatcher.Application.Tests;

/// <summary>Shared in-memory test doubles + a builder for EnrichmentService unit tests (T020/T032/T041/T050).</summary>
internal static class EnrichmentDoubles
{
    public static readonly DateTimeOffset Now = new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

    public static string KindOf(object item) => item switch
    {
        CvProfileWorkItem => "cvProfile",
        OfferSummaryWorkItem => "offerSummary",
        OfferFitWorkItem => "offerFit",
        OfferAffinityWorkItem => "offerAffinity",
        _ => "?",
    };

    public static CandidateCv PendingCv()
    {
        var cv = CandidateCv.Create(CvId.New(), "cv.pdf");
        cv.SetExtractionGauge(true, "SHA256:1:bytes", Now);
        return cv;
    }

    public static CandidateCv ProducedCv()
    {
        var cv = PendingCv();
        cv.ApplyProfile(new CvProfile(["C#"], "Senior", "Backend dev."), "SHA256:1:bytes", Now);
        return cv;
    }

    public static Offer AvailableOffer(string nativeKey, DateTimeOffset? published = null, string description = "<p>Build.</p>")
    {
        var content = new OfferContent
        {
            Title = $"Role {nativeKey}",
            Company = "Acme",
            CanonicalUrl = $"https://example.test/{nativeKey}",
            RequiredSkills = ["C#"],
            DescriptionHtml = description,
            PublishedAt = published,
        };
        var externalRef = ExternalRef.Create(SourceId.New(), nativeKey, IdentityKind.NativeId).Value;
        return Offer.Create(OfferId.New(), externalRef, content, ContentFingerprint.Compute(content), Now);
    }

    public static string SummaryHash(Offer offer) =>
        OfferEnrichmentInputs.Hash(offer.CurrentFingerprint.Hash, offer.Company, offer.Location, offer.DescriptionHtml).Serialized;

    /// <summary>The current affinity input hash for a candidate against a set of applied offers (self excluded from the version).</summary>
    public static string AffinityHash(Offer candidate, IReadOnlyList<Offer> applied)
    {
        var basisVersion = AppliedBasisInputs.Version([.. applied.Select(o => (o.Id, o.CurrentFingerprint.Hash))]);
        var offerEnrichHash = OfferEnrichmentInputs.Hash(candidate.CurrentFingerprint.Hash, candidate.Company, candidate.Location, candidate.DescriptionHtml);
        return OfferAffinityInputs.Hash(offerEnrichHash, basisVersion!).Serialized;
    }
}

internal sealed class EnrichmentHarness
{
    public FakeEnrichmentRepo Enrichment { get; } = new();
    public EnrichmentService Service { get; }
    public AppSettings Settings { get; }

    public EnrichmentHarness(IReadOnlyList<CandidateCv> cvs, IReadOnlyList<Offer> offers, int retryLimit = 3, int maxKeySkills = 10)
    {
        foreach (var o in offers)
        {
            Enrichment.Offers.Add(o);
            Enrichment.Enrichments[o.Id] = OfferEnrichment.CreatePending(o.Id);
            Enrichment.Fits[o.Id] = OfferFit.CreatePending(o.Id);
            Enrichment.Affinities[o.Id] = OfferAffinity.CreatePending(o.Id);
        }

        Settings = AppSettings.CreateDefault();
        Settings.UpdateEnrichment(new EnrichmentSettings { RetryLimit = retryLimit, MaxKeySkills = maxKeySkills });

        Service = new EnrichmentService(
            new FakeCvRepo([.. cvs]),
            Enrichment,
            new FakeSettingsRepo(Settings),
            new FakeFileStore(),
            new FakeExtractor(),
            new FakeUnitOfWork(),
            new JobOfferMatcher.Application.Scanning.MaintenanceGate(new JobOfferMatcher.Application.Scanning.ScanConcurrencyGuard()),
            new FakeTimeProvider(EnrichmentDoubles.Now));
    }
}

internal sealed class FakeEnrichmentRepo : IEnrichmentRepository
{
    public List<Offer> Offers { get; } = [];
    public Dictionary<OfferId, OfferEnrichment> Enrichments { get; } = [];
    public Dictionary<OfferId, OfferFit> Fits { get; } = [];
    public Dictionary<OfferId, OfferAffinity> Affinities { get; } = [];

    public Task<OfferEnrichment?> GetEnrichmentAsync(OfferId id, CancellationToken ct = default) =>
        Task.FromResult(Enrichments.GetValueOrDefault(id));

    public Task<OfferFit?> GetFitAsync(OfferId id, CancellationToken ct = default) =>
        Task.FromResult(Fits.GetValueOrDefault(id));

    public Task<OfferAffinity?> GetAffinityAsync(OfferId id, CancellationToken ct = default) =>
        Task.FromResult(Affinities.GetValueOrDefault(id));

    public Task AddEnrichmentAsync(OfferEnrichment e, CancellationToken ct = default)
    {
        Enrichments[e.OfferId] = e;
        return Task.CompletedTask;
    }

    public Task AddFitAsync(OfferFit f, CancellationToken ct = default)
    {
        Fits[f.OfferId] = f;
        return Task.CompletedTask;
    }

    public Task AddAffinityAsync(OfferAffinity a, CancellationToken ct = default)
    {
        Affinities[a.OfferId] = a;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OfferWorkRow>> GetOfferWorkRowsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OfferWorkRow>>(
            Offers.Where(o => Enrichments.ContainsKey(o.Id) && Fits.ContainsKey(o.Id) && Affinities.ContainsKey(o.Id))
                .Select(o => new OfferWorkRow(o, Enrichments[o.Id], Fits[o.Id], Affinities[o.Id]))
                .ToList());

    public Task<SatelliteCounts> GetCountsAsync(bool countFits, bool countAffinity, CancellationToken ct = default) =>
        Task.FromResult(new SatelliteCounts(
            Enrichments.Values.Count(e => e.State == EnrichmentState.Pending),
            Enrichments.Values.Count(e => e.State == EnrichmentState.Failed),
            countFits ? Fits.Values.Count(f => f.State == EnrichmentState.Pending) : 0,
            countFits ? Fits.Values.Count(f => f.State == EnrichmentState.Failed) : 0,
            countAffinity ? Affinities.Values.Count(a => a.State == EnrichmentState.Pending) : 0,
            countAffinity ? Affinities.Values.Count(a => a.State == EnrichmentState.Failed) : 0));

    public Task<DateTimeOffset?> GetLastResultAtAsync(CancellationToken ct = default) =>
        Task.FromResult<DateTimeOffset?>(null);

    public Task<int> GetAppliedCountAsync(CancellationToken ct = default) =>
        Task.FromResult(Offers.Count(o => o.Applied));

    public Task InvalidateAllFitsAsync(CancellationToken ct = default)
    {
        foreach (var f in Fits.Values)
        {
            f.Invalidate();
        }

        return Task.CompletedTask;
    }

    public Task InvalidateAllAffinityAsync(CancellationToken ct = default)
    {
        foreach (var a in Affinities.Values)
        {
            a.Invalidate();
        }

        return Task.CompletedTask;
    }

    public Task RearmFailedAsync(CancellationToken ct = default)
    {
        foreach (var e in Enrichments.Values)
        {
            e.Rearm();
        }

        foreach (var f in Fits.Values)
        {
            f.Rearm();
        }

        foreach (var a in Affinities.Values)
        {
            a.Rearm();
        }

        return Task.CompletedTask;
    }

    public Task ForceAllPendingAsync(CancellationToken ct = default)
    {
        foreach (var e in Enrichments.Values)
        {
            e.ForcePending();
        }

        foreach (var f in Fits.Values)
        {
            f.ForcePending();
        }

        foreach (var a in Affinities.Values)
        {
            a.ForcePending();
        }

        return Task.CompletedTask;
    }
}

internal sealed class FakeCvRepo(List<CandidateCv> cvs) : ICvRepository
{
    public Task<IReadOnlyList<CandidateCv>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CandidateCv>>(cvs);

    public Task<IReadOnlyList<CandidateCv>> GetReadableAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CandidateCv>>(cvs.Where(c => c.IsReadable).ToList());

    public Task<CandidateCv?> GetByIdAsync(CvId id, CancellationToken ct = default) =>
        Task.FromResult(cvs.FirstOrDefault(c => c.Id == id));

    public Task AddAsync(CandidateCv cv, CancellationToken ct = default)
    {
        cvs.Add(cv);
        return Task.CompletedTask;
    }

    public void Remove(CandidateCv cv) => cvs.Remove(cv);
}

internal sealed class FakeSettingsRepo(AppSettings settings) : ISettingsRepository
{
    public Task<AppSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(settings);
}

internal sealed class FakeFileStore : ICvFileStore
{
    public Task<string> SaveAsync(CvId id, string originalFileName, Stream content, CancellationToken ct = default) =>
        Task.FromResult(originalFileName);

    public void Delete(string storedFileName) { }

    public string GetAbsolutePath(string storedFileName) => storedFileName;

    public IReadOnlyList<StoredCvFile> EnumerateAll() => [];

    public ICvDirectorySwap StageSwap(IReadOnlyList<CvFilePayload> files) => throw new NotSupportedException();
}

internal sealed class FakeExtractor : ICvTextExtractor
{
    public Result<string> ExtractText(Stream pdf) => "text";
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
}
