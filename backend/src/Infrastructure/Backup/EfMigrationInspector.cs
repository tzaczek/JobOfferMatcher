using JobOfferMatcher.Application.Backup;
using JobOfferMatcher.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Backup;

/// <summary>
/// EF-backed <see cref="IMigrationInspector"/> (003 data-model §4): the applied tip is the last id
/// from <c>GetAppliedMigrationsAsync</c> (the running DB's HEAD), and the known set is
/// <c>GetMigrations</c> (every migration id this build can represent). Drives the cross-version
/// decision (FR-017) without leaking EF into the Application layer.
/// </summary>
public sealed class EfMigrationInspector(AppDbContext db) : IMigrationInspector
{
    public async Task<string> AppliedTipAsync(CancellationToken ct = default)
    {
        var applied = await db.Database.GetAppliedMigrationsAsync(ct);
        return applied.LastOrDefault() ?? string.Empty;
    }

    public IReadOnlyList<string> KnownMigrations() => db.Database.GetMigrations().ToList();
}
