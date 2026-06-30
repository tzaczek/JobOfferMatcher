namespace JobOfferMatcher.Infrastructure.Sources.JustJoinIt;

/// <summary>
/// Editable justjoin.it adapter config (contracts/justjoinit-payload.md). Kept in config (not
/// code) per FR-002, including the category id↔key map the contract test guards (7 ↔ "net").
/// </summary>
public sealed class JustJoinItOptions
{
    public const string SectionName = "Sources:JustJoinIt";

    public string ApiBaseUrl { get; set; } = "https://api.justjoin.it";
    public string ListPath { get; set; } = "/v2/user-panel/offers/by-cursor";
    public string DetailPath { get; set; } = "/v1/offers/{slug}";
    public string SiteOfferUrlTemplate { get; set; } = "https://justjoin.it/job-offer/{slug}";

    public int PageSize { get; set; } = 20;

    /// <summary>Generic, non-PII user agent (politeness; no name/email — Principle IV).</summary>
    public string UserAgent { get; set; } = "JobOfferMatcher/1.0 (+local personal job tracker)";

    /// <summary>Polite pacing — ~1 req/s sequential (FR-007).</summary>
    public int RequestDelayMs { get; set; } = 1000;

    /// <summary>Category id → key map (guards an upstream remap). Default: 7 ↔ "net" (.NET).</summary>
    public Dictionary<string, string> CategoryMap { get; set; } = new()
    {
        ["7"] = "net",
    };
}
