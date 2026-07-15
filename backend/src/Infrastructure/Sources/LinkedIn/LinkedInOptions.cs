namespace JobOfferMatcher.Infrastructure.Sources.LinkedIn;

/// <summary>
/// Editable LinkedIn adapter config (feature 008, data-model §7), mirroring <c>TheProtocolOptions</c>.
/// Not persisted (appsettings / defaults). LinkedIn collection runs through a <b>headed, persistent</b>
/// Playwright context so the user can log in once and the session is reused across scans (ADR-2).
/// The password is never read/stored/transmitted/logged — only typed by the user into the browser.
/// </summary>
public sealed class LinkedInOptions
{
    public const string SectionName = "Sources:LinkedIn";

    /// <summary>Swap the real <c>PlaywrightLinkedInClient</c> (true) ↔ <c>NotConfiguredLinkedInClient</c> (false).</summary>
    public bool UseBrowser { get; set; } = true;

    /// <summary>Must be <b>headed</b> — the user types credentials / clears 2FA in the visible window (ADR-2).</summary>
    public bool Headless { get; set; }

    /// <summary>
    /// Persistent-context profile directory (cookies + localStorage). Default resolved at DI time to
    /// <c>{LocalApplicationData}/JobOfferMatcher/browser-profiles/linkedin</c> — OS-app-data-rooted so it
    /// survives <c>dotnet clean</c>, gitignored, and <b>outside <c>cv-data/</c></b> so the 003 backup can't
    /// sweep the session (FR-012a). Never under <c>bin/</c>.
    /// </summary>
    public string? ProfilePath { get; set; }

    /// <summary>Per-navigation budget (ms).</summary>
    public int NavigationTimeoutMs { get; set; } = 45000;

    /// <summary>Bounds the mid-scan login wait so an attended scan can't hang forever (ms, default 3 min).</summary>
    public int LoginTimeoutMs { get; set; } = 180000;

    /// <summary>Polite pacing between list/body reads (~&lt;1 req/s per the 001 ADR-2 source-access risk).</summary>
    public int RequestDelayMs { get; set; } = 1500;

    /// <summary>Bounded collection per pass (FR-013).</summary>
    public int MaxResultsPerSearch { get; set; } = 50;

    /// <summary>Personalized recommended feed (JYMBII) — the US1 pass.</summary>
    public string RecommendedUrl { get; set; } = "https://www.linkedin.com/jobs/collections/recommended/";

    /// <summary>Keyword-search results — the US2 pass. Placeholders filled per saved search.</summary>
    public string SearchUrlTemplate { get; set; } =
        "https://www.linkedin.com/jobs/search-results/?keywords={keywords}&geoId={geoId}&distance={distance}&f_TPR={recency}";

    /// <summary>Realistic desktop UA (no PII — no name/email; Principle IV).</summary>
    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    /// <summary>Bias localized labels toward English.</summary>
    public string Locale { get; set; } = "en";
}
