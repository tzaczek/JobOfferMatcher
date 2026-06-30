using Microsoft.Extensions.Configuration;

namespace JobOfferMatcher.Infrastructure.Backup;

/// <summary>
/// Resolves the local, gitignored directory for server-side backup artifacts (the automatic
/// safety pre-backups + temp archives) — <c>Backup:StoragePath</c> or, by default,
/// <c>{AppContext.BaseDirectory}/backups</c>. Mirrors <c>LocalCvFileStore</c>'s <c>Cv:StoragePath</c>
/// logic so the path differs correctly between host-dev (<c>{bin}/backups</c>) and the container volume,
/// and is never a hard-coded repo-root path. Created on first use (003 plan / Principle IV).
/// </summary>
public static class BackupStorage
{
    public static string ResolveDirectory(IConfiguration configuration)
    {
        var dir = configuration["Backup:StoragePath"] ?? Path.Combine(AppContext.BaseDirectory, "backups");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
