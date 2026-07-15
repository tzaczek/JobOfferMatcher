using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Application.Scanning;

/// <summary>
/// Port for the manual-login fallback (contracts/ijobsource-port.md, research §2). First exercised by
/// the LinkedIn source (feature 008) — the deferred FR-040 path is now activated. The user's own login
/// in an app-controlled headed browser is the authoritative login-complete signal.
/// </summary>
public interface IInteractiveBrowserSession
{
    /// <summary>
    /// Ensure a reusable logged-in session exists for the source.
    /// <para>
    /// <paramref name="interactive"/> <c>false</c> (unattended scan): return a valid
    /// <see cref="SessionReady"/> iff a persisted session is already logged in; otherwise a
    /// <c>Failure</c> <b>without</b> launching a window — so a scheduled scan never hangs (feature 008,
    /// ADR-3). <paramref name="interactive"/> <c>true</c> (attended scan): if not already logged in,
    /// launch the headed browser and wait (bounded) for the user to finish, then return
    /// <see cref="SessionReady"/>, else a <c>Failure</c>.
    /// </para>
    /// </summary>
    Task<Result<SessionReady>> EnsureLoggedInAsync(SourceId source, bool interactive, CancellationToken ct);
}

/// <summary>A confirmed, reusable logged-in session for a source.</summary>
public sealed record SessionReady(SourceId Source, DateTimeOffset ReadyAt);
