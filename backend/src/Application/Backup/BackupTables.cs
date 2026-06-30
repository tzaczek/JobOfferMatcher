namespace JobOfferMatcher.Application.Backup;

/// <summary>
/// The canonical, ordered inventory of the 12 application tables a backup transports (003
/// data-model §3) — the single source consumed by snapshot, restore, and the guard tests.
/// <para>
/// <see cref="InsertOrder"/> is dependency order (parents before children): a restore replays
/// <c>COPY … FROM STDIN</c> in this order so foreign keys resolve without disabling triggers.
/// <c>__EFMigrationsHistory</c> is deliberately absent — the running binary's HEAD history is
/// authoritative and is never truncated or reloaded; only its tip is recorded in the manifest.
/// </para>
/// </summary>
public static class BackupTables
{
    /// <summary>The 12 data tables in dependency/insert order (load parents first on restore).</summary>
    public static readonly IReadOnlyList<string> InsertOrder =
    [
        // Tier 1 — independent roots.
        "job_source",
        "scan_run",
        "role_group",
        "schedule_config",
        "app_settings",
        "candidate_cv",
        // Tier 2 — references tier 1.
        "offers",
        // Tier 3 — reference offers (hard FK, CASCADE).
        "offer_observation",
        "offer_version",
        "offer_event",
        "offer_enrichment",
        "offer_fit",
    ];

    /// <summary>The schema table that is never part of the data set (history is the binary's, not the backup's).</summary>
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";
}
