using System.Text.Json;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>Loads recorded JSON fixtures copied next to the test assembly (offline, deterministic).</summary>
internal static class FixtureLoader
{
    public static JsonDocument Load(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath);
        var json = File.ReadAllText(path);
        return JsonDocument.Parse(json);
    }
}
