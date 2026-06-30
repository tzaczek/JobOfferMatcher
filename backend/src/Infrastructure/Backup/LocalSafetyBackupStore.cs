using JobOfferMatcher.Application.Backup;
using Microsoft.Extensions.Configuration;

namespace JobOfferMatcher.Infrastructure.Backup;

/// <summary>
/// Files an already-built backup archive under the gitignored backups dir as
/// <c>jobs-safety-&lt;ts&gt;.zip</c> (003 ADR-4). The archive is produced by <c>BackupService.BuildAsync</c>
/// into the same dir, so persisting it is a rename (atomic, same volume). Returns the final path so the
/// restore report can name the recovery point.
/// </summary>
public sealed class LocalSafetyBackupStore(IConfiguration configuration) : ISafetyBackupStore
{
    public Task<string> PersistAsync(string builtArchivePath, DateTimeOffset takenAt, CancellationToken ct = default)
    {
        var dir = BackupStorage.ResolveDirectory(configuration);
        var target = Path.Combine(dir, $"jobs-safety-{takenAt.UtcDateTime:yyyy-MM-dd-HHmmss}.zip");

        // Guard against a same-second collision (a second restore within one second).
        if (File.Exists(target))
        {
            target = Path.Combine(dir, $"jobs-safety-{takenAt.UtcDateTime:yyyy-MM-dd-HHmmss}-{Guid.NewGuid():N}.zip");
        }

        File.Move(builtArchivePath, target);
        return Task.FromResult(target);
    }
}
