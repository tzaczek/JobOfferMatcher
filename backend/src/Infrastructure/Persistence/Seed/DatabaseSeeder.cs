using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobOfferMatcher.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds CONFIG ONLY (never offer data — Principle III/FR-035): the default justjoin.it source
/// holding the user's saved .NET search (contracts/justjoinit-payload.md). Idempotent — keyed by
/// a stable id so repeated startups don't duplicate it.
/// </summary>
public static class DatabaseSeeder
{
    // Stable id for the default source so the seed is idempotent across restarts.
    public static readonly SourceId DefaultJustJoinItSourceId =
        SourceId.From(new Guid("11111111-1111-1111-1111-111111111111"));

    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        if (await db.JobSources.AnyAsync(s => s.Id == DefaultJustJoinItSourceId, ct))
        {
            return;
        }

        var search = new JobSourceSearch
        {
            Categories = ["7"], // justjoin.it ".NET" category id (key "net")
            ExperienceLevels = ["mid", "senior"],
            EmploymentTypes = ["b2b", "permanent"],
            WorkingTimes = ["full_time"],
            WithSalary = true,
            SortBy = "salary",
            OrderBy = "DESC",
            WorkplaceKeep = ["remote", "hybrid"],
        };

        var created = JobSource.Create(
            DefaultJustJoinItSourceId,
            name: "justjoin.it",
            kind: SourceKind.DirectApi,
            search: search,
            requiresLogin: false,
            enabled: true);

        if (created.IsFailure)
        {
            // Should never happen for the hard-coded default; log rather than crash startup.
            logger.LogError("Failed to seed default justjoin.it source: {Error}", created.Error);
            return;
        }

        db.JobSources.Add(created.Value);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded default job source {SourceId} (justjoin.it).", DefaultJustJoinItSourceId);
    }
}
