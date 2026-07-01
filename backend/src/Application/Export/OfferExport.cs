namespace JobOfferMatcher.Application.Export;

/// <summary>The two portable, human-readable export formats (FR-037 / SC-007).</summary>
public enum ExportFormat
{
    Json,
    Csv,
}

/// <summary>One raw salary band, verbatim, for export (FR-008/010).</summary>
public sealed record SalaryBandExport(
    decimal? Min,
    decimal? Max,
    string? Currency,
    string? Period,
    string Basis,
    string Tax);

/// <summary>One interview from the application, for export (FR-018) — captured facts only.</summary>
public sealed record InterviewEventExport(string Kind, DateTimeOffset? ScheduledAt, string? Outcome);

/// <summary>
/// A flat, portable snapshot of one collected offer + its status and key timestamps (FR-037,
/// Principle IX — the user's data is recoverable outside the app). Derived figures (fit/normalized
/// salary) are intentionally excluded — only captured facts are exported.
/// </summary>
public sealed record OfferExport(
    Guid OfferId,
    string Source,
    string Title,
    string Company,
    string? Location,
    string WorkMode,
    string? EmploymentType,
    string? Seniority,
    IReadOnlyList<string> RequiredSkills,
    IReadOnlyList<string> NiceToHaveSkills,
    IReadOnlyList<SalaryBandExport> SalaryBands,
    // 006 — the captured offer body (a fact) + the current produced affinity score (nullable). Fit stays excluded.
    string? Description,
    int? AffinityScore,
    string CanonicalUrl,
    string Availability,
    string UserStatus,
    bool Applied,
    DateTimeOffset? AppliedAt,
    string? ApplicationNote,
    Guid? RoleGroupId,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset FirstSuggestedAt,
    DateTimeOffset LastSeenAt,
    // Application tracking (005, FR-018) — the current pipeline stage/status/outcome + interview history.
    string? ApplicationStage,
    string? ApplicationStatus,
    string? ApplicationOutcome,
    IReadOnlyList<InterviewEventExport> Interviews);

/// <summary>A ready-to-stream export file (bytes + content type + download name).</summary>
public sealed record ExportFile(string FileName, string ContentType, byte[] Content);
