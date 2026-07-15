using JobOfferMatcher.Domain.Sources;
using JobOfferMatcher.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>
/// Feature 008 (T016, real Postgres): the seeder creates the LinkedIn source (InteractiveBrowser,
/// requiresLogin) with its recommended-on + starter-search config in the existing <c>search_criteria</c>
/// jsonb column, and seeding again (a restart) does not duplicate it. This is the end-to-end proof of
/// ADR-5 (no new table/migration — the config rides jsonb). The source is seeded <b>disabled</b>: a
/// login-gated source that fails every unattended scan would drag the run outcome to Failed and disable
/// the reconciliation sanity guard for all other sources (FR-015) — the user enables it when ready to log
/// in. The <c>NoAiDependencyTests</c> and <c>BackupTablesCompletenessTests</c> guards run alongside.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LinkedInSeedTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Seeds_the_linkedin_source_idempotently_with_its_jsonb_search_config()
    {
        await postgres.ResetAsync();

        // Seed twice to simulate two startups (idempotency by stable id).
        await using (var db = postgres.CreateContext())
        {
            await DatabaseSeeder.SeedAsync(db, TimeProvider.System, NullLogger.Instance);
        }

        await using (var db = postgres.CreateContext())
        {
            await DatabaseSeeder.SeedAsync(db, TimeProvider.System, NullLogger.Instance);
        }

        await using var verify = postgres.CreateContext();
        var linkedInRows = await verify.JobSources
            .Where(s => s.Id == DatabaseSeeder.DefaultLinkedInSourceId)
            .ToListAsync();

        linkedInRows.Count.ShouldBe(1); // seeded once, never duplicated across restarts

        var source = linkedInRows[0];
        source.Name.ShouldBe("LinkedIn");
        source.Kind.ShouldBe(SourceKind.InteractiveBrowser);
        source.RequiresLogin.ShouldBeTrue();
        // Seeded disabled so it doesn't fail every unattended scan-all (which would break the FR-015
        // sanity guard for the other sources); the user enables it when ready to log in.
        source.Enabled.ShouldBeFalse();

        // The additive jsonb search config round-trips through real Postgres — no migration (ADR-5).
        source.Search.IncludeRecommended.ShouldBeTrue();
        source.Search.LinkedInSearches.Count.ShouldBe(1);
        source.Search.LinkedInSearches[0].Keywords.ShouldBe("Senior .NET Software Engineer");
    }
}
