namespace JobOfferMatcher.Infrastructure.Sources.TheProtocol;

/// <summary>
/// Editable theprotocol.it adapter config. Kept in config (not code) per FR-002. theprotocol is a
/// Next.js app whose offer list is server-rendered into the page's <c>__NEXT_DATA__</c> JSON; the
/// adapter requests the public <c>/filtry/&lt;path&gt;</c> listing and reads that embedded JSON. The
/// search path is built from the saved search's categories (e.g. <c>c%23;t</c>), falling back to
/// <see cref="DefaultSearchPath"/>.
/// </summary>
public sealed class TheProtocolOptions
{
    public const string SectionName = "Sources:TheProtocol";

    public string SiteBaseUrl { get; set; } = "https://theprotocol.it";
    public string ListPathPrefix { get; set; } = "/filtry/";
    public string OfferUrlTemplate { get; set; } = "https://theprotocol.it/szczegoly/praca/{offerUrlName}";

    /// <summary>Server sort field. The feed re-ranks locally; this only biases collection order.</summary>
    public string Sort { get; set; } = "salary";

    /// <summary>Used when the saved search carries no categories. Matches the user's C# (technology) filter.</summary>
    public string DefaultSearchPath { get; set; } = "c%23;t";

    /// <summary>Absolute safety cap so a misreported page count can't loop forever (research §1).</summary>
    public int MaxPages { get; set; } = 50;

    /// <summary>
    /// theprotocol fronts the page with a Cloudflare JS challenge that a plain HTTP client can't pass,
    /// so collection runs through a real (headless) browser that clears the challenge, then reads the
    /// same embedded JSON. Set false to force the lighter HttpClient transport (e.g. if the block lifts).
    /// </summary>
    public bool UseBrowser { get; set; } = true;

    /// <summary>Run the browser headless (no visible window). Flip to false only for debugging.</summary>
    public bool Headless { get; set; } = true;

    /// <summary>Per-page navigation budget, including time for the challenge to clear (ms).</summary>
    public int NavigationTimeoutMs { get; set; } = 45000;

    /// <summary>
    /// theprotocol's edge rejects non-browser agents (the generic bot UA gets a 403/challenge), so this
    /// source needs a realistic, still-generic browser UA. No PII — no name/email (Principle IV).
    /// </summary>
    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    /// <summary>HTML accept header — theprotocol serves the offer-bearing page only to browser-like requests.</summary>
    public string Accept { get; set; } = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

    /// <summary>Bias localized labels toward English where the offer allows (mapper handles PL + EN regardless).</summary>
    public string AcceptLanguage { get; set; } = "en";

    /// <summary>Polite pacing — ~1 req/s sequential (FR-007).</summary>
    public int RequestDelayMs { get; set; } = 1000;
}
