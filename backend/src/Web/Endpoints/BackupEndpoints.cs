using JobOfferMatcher.Application.Backup;
using JobOfferMatcher.Web.Infrastructure;

namespace JobOfferMatcher.Web.Endpoints;

/// <summary>
/// Backup &amp; restore surface (003 contracts/backup-api.md): <c>GET /api/backup</c> streams a full
/// archive, <c>POST /api/backup/inspect</c> validates an upload without touching live data, and
/// <c>POST /api/backup/restore</c> performs the guarded all-or-nothing restore. The whole group is
/// <b>loopback-only</b> (<see cref="LoopbackOnlyFilter"/>) — the payload is the full DB + CV PII
/// (Principle IV). Endpoints are added by user story (US1 backup, US2 restore, US3 inspect).
/// </summary>
internal static class BackupEndpoints
{
    public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/backup").AddEndpointFilter<LoopbackOnlyFilter>();

        // US1 — create & download a complete backup. Built to a server-side temp file first, then
        // streamed; DeleteOnClose removes the temp once the response (or a disconnect) completes.
        group.MapGet("/", async (BackupService backup, CancellationToken ct) =>
        {
            var result = await backup.CreateAsync(ct);
            return result.ToHttp(artifact =>
            {
                var stream = new FileStream(
                    artifact.TempPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None,
                    bufferSize: 81920,
                    FileOptions.Asynchronous | FileOptions.DeleteOnClose);
                return Results.File(stream, "application/zip", artifact.FileName);
            });
        });

        // US2 — guarded, all-or-nothing restore from an uploaded archive. The UI obtains explicit
        // confirmation before calling this; the destructive action is the POST itself. The body-size
        // limit is raised globally in Program (a real archive exceeds the default Kestrel cap).
        group.MapPost("/restore", async (IFormFile? file, RestoreService restore, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.Json(
                    new { error = new { code = "InvalidArchive", message = "A backup .zip file is required." } },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            await using var stream = file.OpenReadStream();
            var result = await restore.RestoreAsync(stream, ct);
            return result.ToHttp(report => Results.Ok(ToRestoreDto(report)));
        }).DisableAntiforgery();

        // US3 — verify an uploaded backup without restoring (read-only; never touches live data).
        group.MapPost("/inspect", async (IFormFile? file, BackupService backup, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.Json(
                    new { error = new { code = "InvalidArchive", message = "A backup .zip file is required." } },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            await using var stream = file.OpenReadStream();
            var result = await backup.InspectAsync(stream, ct);
            return result.ToHttp(inspection => Results.Ok(ToInspectionDto(inspection)));
        }).DisableAntiforgery();

        return api;
    }

    private static object ToInspectionDto(BackupInspection inspection) => new
    {
        valid = inspection.Valid,
        createdAtUtc = inspection.CreatedAtUtc,
        appProductVersion = inspection.AppProductVersion,
        migrationTip = inspection.MigrationTip,
        compatibility = inspection.Compatibility.ToString(),
        tableCounts = inspection.TableCounts,
        cvFileCount = inspection.CvFileCount,
        totalCvBytes = inspection.TotalCvBytes,
        warnings = inspection.Warnings,
    };

    private static object ToRestoreDto(RestoreReport report) => new
    {
        restoredAtUtc = report.RestoredAtUtc,
        compatibility = report.Compatibility.ToString(),
        tableCounts = report.TableCounts,
        cvFileCount = report.CvFileCount,
        safetyBackupPath = report.SafetyBackupPath,
        backfillApplied = report.BackfillApplied,
    };
}
