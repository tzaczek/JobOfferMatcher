using JobOfferMatcher.Domain.Common;

namespace JobOfferMatcher.Domain.Scheduling;

/// <summary>
/// The single-row scan schedule (FR-019): a cron string, IANA/Windows time zone, an enabled flag,
/// and the persisted <see cref="LastRunUtc"/> the poll-tick uses for catch-up (research §3). Cron
/// SYNTAX is validated at the Application boundary against Cronos (Domain stays framework-free).
/// </summary>
public sealed class ScheduleConfig
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;
    public string Cron { get; private set; } = "0 6,13,20 * * *";
    public string TimeZone { get; private set; } = "Europe/Warsaw";
    public bool Enabled { get; private set; } = true;
    public DateTimeOffset? LastRunUtc { get; private set; }

    private ScheduleConfig()
    {
        // EF Core materialization.
    }

    public static Result<ScheduleConfig> Create(string cron, string timeZone, bool enabled)
    {
        var config = new ScheduleConfig();
        var update = config.Update(cron, timeZone, enabled);
        return update.IsFailure ? update.Error : config;
    }

    public Result Update(string cron, string timeZone, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            return new Error("InvalidCron", "Cron expression is required.");
        }

        if (string.IsNullOrWhiteSpace(timeZone))
        {
            return new Error("InvalidTimeZone", "Time zone is required.");
        }

        Cron = cron.Trim();
        TimeZone = timeZone.Trim();
        Enabled = enabled;
        return Result.Success();
    }

    /// <summary>Collapse all missed windows into the most-recent occurrence (idempotent catch-up).</summary>
    public void AdvanceLastRun(DateTimeOffset windowUtc) => LastRunUtc = windowUtc;
}
