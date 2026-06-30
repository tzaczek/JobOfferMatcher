namespace JobOfferMatcher.Application.Backup;

/// <summary>
/// Port over EF's migration metadata (003 data-model §4) so the Application layer can decide
/// cross-version compatibility (FR-017) without referencing EF Core. Wraps
/// <c>GetAppliedMigrationsAsync</c> / <c>GetMigrations</c>.
/// </summary>
public interface IMigrationInspector
{
    /// <summary>The last applied migration id (the running database's HEAD) — recorded as the manifest tip.</summary>
    Task<string> AppliedTipAsync(CancellationToken ct = default);

    /// <summary>Every migration id <i>this build</i> knows, in order; the last element is HEAD.</summary>
    IReadOnlyList<string> KnownMigrations();
}
