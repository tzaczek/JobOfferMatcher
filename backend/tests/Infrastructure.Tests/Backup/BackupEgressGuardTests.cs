namespace JobOfferMatcher.Infrastructure.Tests.Backup;

/// <summary>
/// Egress guard (003 T049, SC-007/FR-015, Principle IV): the backup/restore code path must be fully
/// local — it talks only to the app's own Postgres and the local filesystem, never an outbound HTTP /
/// external client. This scans the backup source files for any networking surface so a stray
/// <c>HttpClient</c> can never slip in (complements the loopback test).
/// </summary>
public sealed class BackupEgressGuardTests
{
    private static readonly string[] ForbiddenTokens =
    [
        "HttpClient", "HttpRequestMessage", "WebClient", "WebRequest", "Socket", "Dns.",
        "http://", "https://",
    ];

    [Fact]
    public void Backup_and_restore_source_makes_no_outbound_network_call()
    {
        var root = FindBackendRoot();
        var folders = new[]
        {
            Path.Combine(root, "src", "Application", "Backup"),
            Path.Combine(root, "src", "Infrastructure", "Backup"),
        };

        var files = folders
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .ToList();

        files.ShouldNotBeEmpty(); // sanity: we found the backup sources

        var offenders = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var token in ForbiddenTokens)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {token}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            $"Backup/restore must be fully local (no outbound HTTP). Found: {string.Join(", ", offenders)}");
    }

    private static string FindBackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the backend root (Directory.Packages.props).");
    }
}
