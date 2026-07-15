namespace JobOfferMatcher.Domain.Sources;

/// <summary>
/// The user's editable search/filter criteria for a source (FR-002), stored as JSON on the
/// <see cref="JobSource"/> and passed to the <c>IJobSource</c> port. Editing it changes what the
/// next scan collects with NO code change. Fields are deliberately source-neutral strings so a
/// second source can reuse the shape; an adapter maps them to its own query params
/// (e.g. justjoin.it <c>categories[]=7</c> — see contracts/justjoinit-payload.md).
/// </summary>
public sealed record JobSourceSearch
{
    /// <summary>Source category keys/ids (justjoin.it: "7" = .NET). Adapter owns the id↔key map.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>e.g. "mid", "senior".</summary>
    public IReadOnlyList<string> ExperienceLevels { get; init; } = [];

    /// <summary>e.g. "b2b", "permanent".</summary>
    public IReadOnlyList<string> EmploymentTypes { get; init; } = [];

    /// <summary>e.g. "full_time".</summary>
    public IReadOnlyList<string> WorkingTimes { get; init; } = [];

    /// <summary>Only collect offers that disclose a salary.</summary>
    public bool WithSalary { get; init; }

    /// <summary>Server sort field (e.g. "salary"); ranking is still re-sorted locally.</summary>
    public string? SortBy { get; init; }

    /// <summary>Server sort direction (e.g. "DESC").</summary>
    public string? OrderBy { get; init; }

    /// <summary>
    /// Client-side workplace keep-set (e.g. "remote", "hybrid"). The justjoin.it server ignores
    /// workplace filters, so the adapter filters on the per-offer value (justjoinit-payload.md).
    /// </summary>
    public IReadOnlyList<string> WorkplaceKeep { get; init; } = [];

    // --- LinkedIn source (feature 008). Additive jsonb fields on the existing search_criteria column —
    // no migration (data-model §1, ADR-5). Old rows lacking these keys deserialize to the defaults
    // below (System.Text.Json ignores absent members), so every pre-008 source row keeps working.
    // Used only by the LinkedIn (InteractiveBrowser) adapter; the DirectApi adapters ignore them.

    /// <summary>Collect the user's personalized LinkedIn <em>Recommended</em> feed (feature 008, US1).</summary>
    public bool IncludeRecommended { get; init; }

    /// <summary>Saved LinkedIn keyword searches, each an independent collection pass (feature 008, US2).</summary>
    public IReadOnlyList<LinkedInSearch> LinkedInSearches { get; init; } = [];
}
