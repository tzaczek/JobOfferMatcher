using JobOfferMatcher.Application.Enrichment;

namespace JobOfferMatcher.Application.TailoredCvs;

// Wire contracts for /api/tailored-cv (contracts/tailored-cv-api.md + worker-protocol.md). camelCase
// JSON at the boundary; `state`/`outcome` are lowercase strings. The source CV reuses 002's
// CvDocumentView (path + fallback) — the worker reads the binary off disk, never over HTTP.

// ---- UI views --------------------------------------------------------------------------------

public sealed record TailoredCvView(
    Guid OfferId,
    string OfferTitle,
    string Company,
    Guid SourceCvId,
    string State,
    int GenerationVersion,
    IReadOnlyList<string> EmphasisedSkills,
    string Prompt,
    bool HasPdf,
    DateTimeOffset? GeneratedAt,
    string? LastError);

public sealed record TailoredCvSourceCvView(Guid Id, string FileName);

/// <summary>The prefilled, non-persisted modal contents (FR-013/FR-003). <c>SourceCv</c> null ⇒ NoCvOnFile path.</summary>
public sealed record TailoredCvDraftView(
    Guid OfferId,
    string OfferTitle,
    string Company,
    string Prompt,
    IReadOnlyList<string> EmphasisedSkills,
    IReadOnlyList<string> AllOfferSkills,
    TailoredCvSourceCvView? SourceCv);

/// <summary>POST body for create/regenerate. Empty <c>Prompt</c> ⇒ <c>InvalidTailoredCvRequest</c>.</summary>
public sealed record GenerateTailoredCvRequest(string? Prompt, IReadOnlyList<string>? EmphasisedSkills, Guid? SourceCvId);

// ---- Worker queue (GET /pending) -------------------------------------------------------------

public sealed record TailoredCvPendingMeta(int PendingTotal, int FailedTotal, int Returned, int RetryLimit);

public sealed record TailoredCvOfferWire(
    string Title,
    string Company,
    string? Seniority,
    IReadOnlyList<string> RequiredSkills,
    IReadOnlyList<string> NiceToHaveSkills);

public sealed record TailoredCvWorkItem(
    string WorkItemId,
    Guid OfferId,
    int GenerationVersion,
    string Prompt,
    IReadOnlyList<string> EmphasisedSkills,
    TailoredCvOfferWire Offer,
    CvDocumentView SourceCv);

public sealed record TailoredCvPendingWork(TailoredCvPendingMeta Meta, IReadOnlyList<TailoredCvWorkItem> Items);

// ---- Write-back (POST /results) --------------------------------------------------------------

public sealed record TailoredCvResultItem(string? WorkItemId, int? GenerationVersion, string? Status, string? Html, string? Reason);

public sealed record TailoredCvSubmitRequest(IReadOnlyList<TailoredCvResultItem>? Results);

public sealed record TailoredCvResultOutcome(string WorkItemId, string Outcome, int Attempt, string State);

public sealed record TailoredCvSubmitResponse(int Accepted, int Rejected, IReadOnlyList<TailoredCvResultOutcome> Results);

// ---- Download (app-internal; the Web layer streams the file) ----------------------------------

/// <summary>The produced PDF's absolute path + a human-friendly download name (<c>CV - {company} - {title}.pdf</c>).</summary>
public sealed record TailoredCvDownload(string AbsolutePath, string DownloadFileName);
