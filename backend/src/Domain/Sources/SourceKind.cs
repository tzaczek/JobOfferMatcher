namespace JobOfferMatcher.Domain.Sources;

/// <summary>
/// Selects which <c>IJobSource</c> adapter collects a source (contracts/ijobsource-port.md).
/// <see cref="DirectApi"/> is the lightest-reliable path; <see cref="InteractiveBrowser"/> is the
/// deferred manual-login fallback (research §1–§2 / FR-040).
/// </summary>
public enum SourceKind
{
    DirectApi,
    InteractiveBrowser,
}
