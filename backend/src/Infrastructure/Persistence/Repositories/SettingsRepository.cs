using JobOfferMatcher.Application.Settings;
using JobOfferMatcher.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Persistence.Repositories;

internal sealed class SettingsRepository(AppDbContext db) : ISettingsRepository
{
    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        // FindAsync checks the change-tracker first, so repeated calls in one scope return the same
        // instance instead of adding a second singleton (which would break EF's key tracking).
        var settings = await db.AppSettings.FindAsync([AppSettings.SingletonId], ct);
        if (settings is not null)
        {
            return settings;
        }

        var created = AppSettings.CreateDefault();
        await db.AppSettings.AddAsync(created, ct);
        return created;
    }
}
