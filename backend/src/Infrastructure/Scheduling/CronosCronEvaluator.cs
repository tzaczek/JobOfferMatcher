using Cronos;
using JobOfferMatcher.Application.Scheduling;
using JobOfferMatcher.Domain.Common;

namespace JobOfferMatcher.Infrastructure.Scheduling;

/// <summary>
/// Cronos-backed <see cref="ICronEvaluator"/>. Wraps Cronos's <c>FormatException</c> /
/// time-zone errors into a <see cref="Result"/> so a bad expression is an expected failure, never a
/// worker crash (research §3). Supports both 5-field and 6-field (with seconds) cron.
/// </summary>
public sealed class CronosCronEvaluator : ICronEvaluator
{
    public Result Validate(string cron, string timeZone)
    {
        if (TryParse(cron) is null)
        {
            return new Error("InvalidCron", $"'{cron}' is not a valid cron expression.");
        }

        if (!TryGetTimeZone(timeZone, out _))
        {
            return new Error("InvalidTimeZone", $"'{timeZone}' is not a recognized time zone.");
        }

        return Result.Success();
    }

    public DateTimeOffset? GetPreviousOccurrence(string cron, string timeZone, DateTimeOffset nowUtc)
    {
        var expression = TryParse(cron);
        if (expression is null || !TryGetTimeZone(timeZone, out var tz))
        {
            return null;
        }

        // Cronos has GetOccurrences but no direct "previous"; walk back from now within a window.
        // Find the latest occurrence in [now - 2 days, now].
        var fromUtc = nowUtc.AddDays(-2).UtcDateTime;
        var toUtc = nowUtc.UtcDateTime;
        DateTime? last = null;
        foreach (var occurrence in expression.GetOccurrences(fromUtc, toUtc, tz!, fromInclusive: true, toInclusive: true))
        {
            last = occurrence;
        }

        return last is null ? null : new DateTimeOffset(last.Value, TimeSpan.Zero);
    }

    private static CronExpression? TryParse(string cron)
    {
        try
        {
            var fields = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return CronExpression.Parse(cron, fields.Length >= 6 ? CronFormat.IncludeSeconds : CronFormat.Standard);
        }
        catch (CronFormatException)
        {
            return null;
        }
    }

    private static bool TryGetTimeZone(string timeZone, out TimeZoneInfo? tz)
    {
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            tz = null;
            return false;
        }
    }
}
