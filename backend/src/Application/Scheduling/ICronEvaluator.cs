using JobOfferMatcher.Domain.Common;

namespace JobOfferMatcher.Application.Scheduling;

/// <summary>
/// Cron parsing/occurrence math behind a port — the Cronos dependency lives in Infrastructure, so
/// the Application/Domain stay framework-free. Validation wraps Cronos's <c>FormatException</c> into
/// a <see cref="Result"/> at the boundary (research §3).
/// </summary>
public interface ICronEvaluator
{
    /// <summary>Validate cron syntax + time zone → expected failure as a Result (never throws).</summary>
    Result Validate(string cron, string timeZone);

    /// <summary>The most recent occurrence at/just before <paramref name="nowUtc"/>, in the given zone.</summary>
    DateTimeOffset? GetPreviousOccurrence(string cron, string timeZone, DateTimeOffset nowUtc);
}
