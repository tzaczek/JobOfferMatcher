using JobOfferMatcher.Application.Scheduling;
using JobOfferMatcher.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Persistence.Repositories;

internal sealed class ScheduleRepository(AppDbContext db) : IScheduleRepository
{
    public async Task<ScheduleConfig> GetAsync(CancellationToken ct = default)
    {
        // FindAsync checks the change-tracker first → no duplicate singleton across repeated calls.
        var config = await db.ScheduleConfigs.FindAsync([ScheduleConfig.SingletonId], ct);
        if (config is not null)
        {
            return config;
        }

        // Materialize the default single row on first access (idempotent).
        var created = ScheduleConfig.Create("0 6,13,20 * * *", "Europe/Warsaw", enabled: true).Value;
        await db.ScheduleConfigs.AddAsync(created, ct);
        return created;
    }

    public Task SaveAsync(ScheduleConfig config, CancellationToken ct = default)
    {
        // Tracked entity — SaveChanges (via IUnitOfWork) persists it. Ensure new rows are tracked.
        if (db.Entry(config).State == EntityState.Detached)
        {
            db.ScheduleConfigs.Update(config);
        }

        return Task.CompletedTask;
    }
}
