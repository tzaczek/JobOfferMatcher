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

    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
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
