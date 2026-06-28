using JobOfferMatcher.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobOfferMatcher.Infrastructure.Persistence;

/// <summary>
/// Applies append-only EF migrations and seeds config at host startup (research §5; Principle IX:
/// <c>MigrateAsync</c>, never <c>EnsureCreated</c>). Called from the Web host's Program.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DatabaseInitializer));

        logger.LogInformation("Applying database migrations...");
        await db.Database.MigrateAsync(ct);

        await DatabaseSeeder.SeedAsync(db, logger, ct);
    }
}
