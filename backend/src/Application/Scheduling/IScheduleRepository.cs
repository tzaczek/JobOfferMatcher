using JobOfferMatcher.Domain.Scheduling;

namespace JobOfferMatcher.Application.Scheduling;

/// <summary>Persistence port for the single-row <see cref="ScheduleConfig"/> (FR-019).</summary>
public interface IScheduleRepository
{
    /// <summary>Get the schedule, creating the default single row if absent.</summary>
    Task<ScheduleConfig> GetAsync(CancellationToken ct = default);

    Task SaveAsync(ScheduleConfig config, CancellationToken ct = default);
}
