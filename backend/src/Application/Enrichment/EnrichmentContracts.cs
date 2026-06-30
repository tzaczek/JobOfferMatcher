using JobOfferMatcher.Application.Offers;

namespace JobOfferMatcher.Application.Enrichment;

// Wire contracts for /api/enrichment (contracts/enrichment-api.md). camelCase JSON at the boundary.
// Work items are returned as a heterogeneous `object` list so System.Text.Json serializes each by
// its runtime type (discriminated by the get-only `Kind`).

/// <summary>Soft caps echoed to the worker (from <c>EnrichmentSettings</c>).</summary>
public sealed record WorkGuidance(int OfferSummaryWords, int CvSummaryWords, int MaxKeySkills, int RationaleWords);

public sealed record PendingMeta(
    int PendingTotal,
    int PendingProfiles,
    int PendingSummaries,
    int PendingFits,
    int FailedTotal,
    int Returned,
    bool HasProducedProfile,
    WorkGuidance Guidance,
    int RetryLimit);

public sealed record PendingWork(PendingMeta Meta, IReadOnlyList<object> Items);

// ---- Work items (discriminated by `Kind`) -----------------------------------------------------

public sealed record CvDocumentView(string Path, string FileName, bool Readable, string? FallbackText);

public sealed record CvProfileWorkItem(Guid CvId, string InputsHash, int Attempt, CvDocumentView Document, object Guidance)
{
    public string Kind => "cvProfile";
    public string WorkItemId => $"cv:{CvId}:profile";
}

public sealed record SummaryOfferView(
    string Title,
    string Company,
    string? Location,
    string? WorkMode,
    string? EmploymentType,
    string? Seniority,
    IReadOnlyList<SalaryBandView> SalaryBands,
    IReadOnlyList<string> RequiredSkills,
    IReadOnlyList<string> NiceToHaveSkills,
    string DescriptionText);

public sealed record OfferSummaryWorkItem(Guid OfferId, string InputsHash, int Attempt, SummaryOfferView Offer, object Guidance)
{
    public string Kind => "offerSummary";
    public string WorkItemId => $"offer:{OfferId}:summary";
}

public sealed record FitOfferView(
    string Title,
    IReadOnlyList<string> RequiredSkills,
    IReadOnlyList<string> NiceToHaveSkills,
    string? Seniority,
    string? WorkMode,
    string? EmploymentType,
    decimal? NormalizedMonthlySalary);

public sealed record FitProfileView(IReadOnlyList<string> Skills, string Seniority, string Summary);

public sealed record FitWeightsView(int Skills, int Seniority, int WorkMode, int Employment, int Salary);

public sealed record FitPreferencesView(
    decimal? SalaryFloor,
    decimal? SalaryTarget,
    IReadOnlyList<string> PreferredWorkModes,
    IReadOnlyList<string> PreferredEmployment);

public sealed record OfferFitWorkItem(
    Guid OfferId,
    string InputsHash,
    int Attempt,
    FitOfferView Offer,
    FitProfileView Profile,
    FitWeightsView Weights,
    FitPreferencesView Preferences,
    object Guidance)
{
    public string Kind => "offerFit";
    public string WorkItemId => $"offer:{OfferId}:fit";
}

// ---- Result write-back (POST /results) --------------------------------------------------------

public sealed record EnrichmentResultItem(
    string? WorkItemId,
    string? Kind,
    string? InputsHash,
    string? Status,
    // offerSummary
    string? Summary = null,
    IReadOnlyList<string>? KeySkills = null,
    // offerFit
    int? Score = null,
    IReadOnlyList<string>? Matched = null,
    IReadOnlyList<string>? Missing = null,
    string? Rationale = null,
    // cvProfile (Summary reused)
    IReadOnlyList<string>? Skills = null,
    string? Seniority = null,
    // failed
    string? Reason = null);

public sealed record SubmitResultsRequest(IReadOnlyList<EnrichmentResultItem>? Results);

public sealed record ResultOutcomeView(string WorkItemId, string Outcome, int Attempt, string State);

public sealed record SubmitResultsResponse(int Accepted, int Rejected, IReadOnlyList<ResultOutcomeView> Results);

// ---- Status (/status) -------------------------------------------------------------------------

public sealed record EnrichmentStatusView(
    int PendingTotal,
    int PendingProfiles,
    int PendingSummaries,
    int PendingFits,
    int FailedTotal,
    bool HasProducedProfile,
    DateTimeOffset? LastResultAt);
