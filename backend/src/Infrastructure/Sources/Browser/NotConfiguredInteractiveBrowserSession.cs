using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Infrastructure.Sources.Browser;

/// <summary>
/// Deferred manual-login adapter (research §2 / Principle X). The Playwright implementation is
/// intentionally NOT built — no current source needs login. The port + escalation trigger exist so
/// adding it later is purely additive (FR-040). Until then it reports "not configured".
/// </summary>
public sealed class NotConfiguredInteractiveBrowserSession : IInteractiveBrowserSession
{
    public static readonly Error NotConfigured = new(
        "InteractiveBrowserNotConfigured",
        "Manual-login browser collection is not configured. The Playwright adapter is deferred (FR-040).");

    public Task<Result<SessionReady>> EnsureLoggedInAsync(SourceId source, bool interactive, CancellationToken ct) =>
        Task.FromResult(Result<SessionReady>.Failure(NotConfigured));
}
