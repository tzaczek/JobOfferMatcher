namespace JobOfferMatcher.Application.Backup;

/// <summary>How a backup's source schema relates to the running build (003 data-model §5, FR-017).</summary>
public enum BackupCompatibility
{
    /// <summary>Manifest tip == HEAD — restore directly.</summary>
    Same,

    /// <summary>Manifest tip is an earlier migration this build knows — load into HEAD then backfill.</summary>
    Older,

    /// <summary>Manifest tip is unknown to this build (a newer app version) — refuse.</summary>
    Newer,
}

/// <summary>
/// Pure cross-version policy (003 data-model §5): compare a backup's applied-migration tip to the
/// ordered set of migrations <i>this build</i> knows. Unit-tested in isolation (no DB). Restore never
/// runs <c>Down</c> — an <see cref="BackupCompatibility.Older"/> snapshot loads into HEAD and a
/// <see cref="BackupCompatibility.Newer"/> one is refused.
/// </summary>
public static class BackupCompatibilityPolicy
{
    public static BackupCompatibility Decide(string backupTip, IReadOnlyList<string> knownMigrations)
    {
        if (knownMigrations.Count == 0 || !knownMigrations.Contains(backupTip))
        {
            return BackupCompatibility.Newer; // unknown to this build → refuse
        }

        return backupTip == knownMigrations[^1] ? BackupCompatibility.Same : BackupCompatibility.Older;
    }
}
