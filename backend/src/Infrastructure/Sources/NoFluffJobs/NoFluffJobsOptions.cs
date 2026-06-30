namespace JobOfferMatcher.Infrastructure.Sources.NoFluffJobs;

/// <summary>
/// Editable nofluffjobs.com adapter config. Kept in config (not code) per FR-002. The LIST endpoint
/// is the site's public <c>infiniteSearch</c> posting API; its query is built from the saved search
/// (<c>requirement=&lt;categories&gt;</c>), falling back to <see cref="DefaultRawSearch"/>.
/// </summary>
public sealed class NoFluffJobsOptions
{
    public const string SectionName = "Sources:NoFluffJobs";

    public string ApiBaseUrl { get; set; } = "https://nofluffjobs.com";
    public string ListPath { get; set; } = "/api/search/posting";
    public string SiteOfferUrlTemplate { get; set; } = "https://nofluffjobs.com/pl/job/{url}";

    /// <summary>The posting search is POSTed with this vendor media type (required — a plain json type 400s).</summary>
    public string SearchContentType { get; set; } = "application/infiniteSearch+json";

    /// <summary>Region / salary normalization query params (the API returns figures in this period/currency).</summary>
    public string Region { get; set; } = "pl";
    public string SalaryCurrency { get; set; } = "PLN";
    public string SalaryPeriod { get; set; } = "month";

    /// <summary>Jobs per page (the API multilocation-expands these into many postings). 5 pages ≈ 86 jobs.</summary>
    public int PageSize { get; set; } = 20;

    /// <summary>Absolute safety cap so a misreported <c>totalPages</c> can't loop forever (research §1).</summary>
    public int MaxPages { get; set; } = 50;

    /// <summary>Used when the saved search carries no categories. Matches the user's C#/.NET filter.</summary>
    public string DefaultRawSearch { get; set; } = "requirement=C#,.NET";

    /// <summary>Generic, non-PII user agent (politeness; no name/email — Principle IV).</summary>
    public string UserAgent { get; set; } = "JobOfferMatcher/1.0 (+local personal job tracker)";

    /// <summary>Polite pacing — ~1 req/s sequential (FR-007).</summary>
    public int RequestDelayMs { get; set; } = 1000;
}
