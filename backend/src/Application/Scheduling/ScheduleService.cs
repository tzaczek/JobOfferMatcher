using JobOfferMatcher.Application.Abstractions;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Scheduling;

namespace JobOfferMatcher.Application.Scheduling;

/// <summary>Read/update the scan schedule (FR-019). Cron is validated before persisting (research §3).</summary>
public sealed class ScheduleService(
    IScheduleRepository schedules,
    ICronEvaluator cron,
    IUnitOfWork unitOfWork)
{
    public Task<ScheduleConfig> GetAsync(CancellationToken ct = default) => schedules.GetAsync(ct);

    public async Task<Result<ScheduleConfig>> UpdateAsync(string cronExpression, string timeZone, bool enabled, CancellationToken ct = default)
    {
        var validation = cron.Validate(cronExpression, timeZone);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        var config = await schedules.GetAsync(ct);
        var update = config.Update(cronExpression, timeZone, enabled);
        if (update.IsFailure)
        {
            return update.Error;
        }

        await schedules.SaveAsync(config, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return config;
    }
}
