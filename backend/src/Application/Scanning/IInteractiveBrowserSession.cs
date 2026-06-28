using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Application.Scanning;

/// <summary>
/// Port for the manual-login fallback (contracts/ijobsource-port.md, research §2). The Playwright
/// adapter is DEFERRED (FR-040 / Principle X) — only the port + escalation trigger exist now. The
/// user's "Done/Continue" click is the authoritative login-complete signal.
/// </summary>
public interface IInteractiveBrowserSession
{
    Task<Result<SessionReady>> EnsureLoggedInAsync(SourceId source, CancellationToken ct);
}

/// <summary>A confirmed, reusable logged-in session for a source.</summary>
public sealed record SessionReady(SourceId Source, DateTimeOffset ReadyAt);
