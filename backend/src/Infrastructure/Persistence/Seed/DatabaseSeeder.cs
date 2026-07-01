using JobOfferMatcher.Domain.Applications;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobOfferMatcher.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds CONFIG ONLY (never offer data — Principle III/FR-035): the default collection sources holding
/// the user's saved searches. Idempotent — each source is keyed by a stable id so repeated startups
/// don't duplicate it, and a newly-added source is back-filled on the next startup without disturbing
/// existing ones (or any user edits to them).
/// </summary>
public static class DatabaseSeeder
{
    // Stable ids so the seed is idempotent across restarts and so the factory can route by id.
    public static readonly SourceId DefaultJustJoinItSourceId =
        SourceId.From(new Guid("11111111-1111-1111-1111-111111111111"));

    public static readonly SourceId DefaultTheProtocolSourceId =
        SourceId.From(new Guid("22222222-2222-2222-2222-222222222222"));

    public static readonly SourceId DefaultNoFluffJobsSourceId =
        SourceId.From(new Guid("33333333-3333-3333-3333-333333333333"));

    /// <summary>
    /// Default interview pipeline stages, seeded (in order) only when the table is empty (data-model §8,
    /// FR-019). A reasonable single-user default derived from Constitution Principle III; fully editable.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultStageNames = ["Applied", "Screening", "Interviewing", "Offer"];

    public static async Task SeedAsync(AppDbContext db, TimeProvider time, ILogger logger, CancellationToken ct = default)
    {
        await SeedPipelineStagesAsync(db, time, logger, ct);


        await SeedSourceAsync(db, logger, DefaultJustJoinItSourceId, "justjoin.it", new JobSourceSearch
        {
            Categories = ["7"], // justjoin.it ".NET" category id (key "net")
            ExperienceLevels = ["mid", "senior"],
            EmploymentTypes = ["b2b", "permanent"],
            WorkingTimes = ["full_time"],
            WithSalary = true,
            SortBy = "salary",
            OrderBy = "DESC",
            WorkplaceKeep = ["remote", "hybrid"],
        }, ct);

        // theprotocol.it: technologies=C# (path "c%23;t"), sorted by salary. No workplace filter (matches
        // the user's listing URL); editable later via the source editor (FR-002).
        await SeedSourceAsync(db, logger, DefaultTheProtocolSourceId, "theprotocol.it", new JobSourceSearch
        {
            Categories = ["C#"],
            WithSalary = true,
            SortBy = "salary",
            OrderBy = "DESC",
            WorkplaceKeep = [],
        }, ct);

        // nofluffjobs.com: requirement=C#,.NET, sorted by salary. No workplace filter (matches the URL).
        await SeedSourceAsync(db, logger, DefaultNoFluffJobsSourceId, "nofluffjobs.com", new JobSourceSearch
        {
            Categories = ["C#", ".NET"],
            WithSalary = true,
            SortBy = "salary",
            OrderBy = "DESC",
            WorkplaceKeep = [],
        }, ct);
    }

    /// <summary>
    /// Seed the default pipeline stages when the table is empty (seed-if-empty at the TABLE level, not
    /// per-id — a user who deletes a default stage must not have it resurrected on restart). Public so
    /// the no-data-loss backfill can ensure stages exist before reconstructing applications (data-model §8).
    /// </summary>
    public static async Task SeedPipelineStagesAsync(AppDbContext db, TimeProvider time, ILogger logger, CancellationToken ct = default)
    {
        if (await db.PipelineStages.AnyAsync(ct))
        {
            return;
        }

        var now = time.GetUtcNow();
        for (var position = 0; position < DefaultStageNames.Count; position++)
        {
            var created = PipelineStage.Create(DefaultStageNames[position], position, now);
            if (created.IsFailure)
            {
                // Should never happen for a hard-coded default; log rather than crash startup.
                logger.LogError("Failed to seed default pipeline stage {Name}: {Error}", DefaultStageNames[position], created.Error);
                return;
            }

            db.PipelineStages.Add(created.Value);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} default pipeline stages.", DefaultStageNames.Count);
    }

    private static async Task SeedSourceAsync(
        AppDbContext db, ILogger logger, SourceId id, string name, JobSourceSearch search, CancellationToken ct)
    {
        if (await db.JobSources.AnyAsync(s => s.Id == id, ct))
        {
            return;
        }

        var created = JobSource.Create(id, name, SourceKind.DirectApi, search, requiresLogin: false, enabled: true);
        if (created.IsFailure)
        {
            // Should never happen for a hard-coded default; log rather than crash startup.
            logger.LogError("Failed to seed default source {Name}: {Error}", name, created.Error);
            return;
        }

        db.JobSources.Add(created.Value);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded default job source {SourceId} ({Name}).", id, name);
    }
}
