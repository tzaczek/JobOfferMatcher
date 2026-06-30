using System.Text.RegularExpressions;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Architecture guard (FR-012 / SC-005, Principle IV): the backend must reference <b>no</b> AI SDK.
/// Claude is the worker (the user's Max plan), never a backend dependency — so the backend makes no
/// outbound AI call and transmits 0 records externally. This asserts the invariant mechanically over
/// every <c>*.csproj</c> + <c>Directory.Packages.props</c>, so a stray <c>@anthropic-ai</c>/OpenAI/
/// SemanticKernel package can never slip in unnoticed.
/// </summary>
public sealed class NoAiDependencyTests
{
    // Package-name fragments that would indicate an AI SDK crept into the build (case-insensitive).
    private static readonly string[] ForbiddenFragments =
    [
        "anthropic", "openai", "azure.ai", "semantickernel", "semantic-kernel",
        "llamasharp", "betalgo", "langchain", "mistral", "cohere", "google.cloud.aiplatform",
    ];

    [Fact]
    public void No_project_or_package_manifest_references_an_ai_sdk()
    {
        var backendRoot = FindBackendRoot();
        var manifests = Directory
            .EnumerateFiles(backendRoot, "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(backendRoot, "Directory.Packages.props", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        manifests.ShouldNotBeEmpty(); // sanity: we actually found the manifests to scan

        var offenders = new List<string>();
        foreach (var manifest in manifests)
        {
            // Only the package-reference lines matter — a comment mentioning "Anthropic" is fine.
            var packageLines = File.ReadLines(manifest)
                .Where(l => l.Contains("Include=", StringComparison.OrdinalIgnoreCase)
                         && (l.Contains("PackageReference", StringComparison.Ordinal)
                          || l.Contains("PackageVersion", StringComparison.Ordinal)));

            foreach (var line in packageLines)
            {
                var include = Regex.Match(line, "Include=\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (!include.Success)
                {
                    continue;
                }

                var packageName = include.Groups[1].Value;
                if (ForbiddenFragments.Any(f => packageName.Contains(f, StringComparison.OrdinalIgnoreCase)))
                {
                    offenders.Add($"{Path.GetFileName(manifest)}: {packageName}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            $"The backend must reference no AI SDK (FR-012/SC-005). Found: {string.Join(", ", offenders)}");
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
