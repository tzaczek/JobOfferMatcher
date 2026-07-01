using System.Text.Json;
using System.Text.Json.Nodes;
using JobOfferMatcher.Domain.Sources;
using JobOfferMatcher.Infrastructure.Sources.JustJoinIt;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>
/// A fake transport whose returned offers can be changed between scans — drives the
/// disappearance/reappearance and user-status integration tests.
/// </summary>
public sealed class MutableJustJoinItClient : IJustJoinItClient
{
    public List<JsonElement> Items { get; private set; } = [];
    public FetchStatus Status { get; set; } = FetchStatus.Ok;

    /// <summary>DETAIL bodies keyed by slug (feature 006, US2) — <c>FetchDetailAsync</c> serves these.</summary>
    private readonly Dictionary<string, string?> _bodies = new(StringComparer.Ordinal);

    /// <summary>Register a DETAIL body for an offer slug (the list item's slug is <c>"{guid}-slug"</c>).</summary>
    public void SetBody(string slug, string? bodyHtml) => _bodies[slug] = bodyHtml;

    /// <summary>The search criteria the most recent scan passed to the adapter (FR-002 verification).</summary>
    public JobSourceSearch? LastSearch { get; private set; }

    public void SetOffers(params (string Guid, decimal SalaryMax)[] offers) =>
        Items = offers.Select(o => MakeItem(o.Guid, o.SalaryMax)).ToList();

    public void SetDetailedOffers(params (string Guid, string Company, string Title, string City, string Workplace)[] offers) =>
        Items = offers.Select(o => MakeDetailedItem(o.Guid, o.Company, o.Title, o.City, o.Workplace)).ToList();

    public Task<(FetchStatus Status, JustJoinItPage? Page)> FetchListAsync(JobSourceSearch search, int from, CancellationToken ct)
    {
        LastSearch = search;

        if (Status != FetchStatus.Ok)
        {
            return Task.FromResult<(FetchStatus, JustJoinItPage?)>((Status, null));
        }

        var page = from == 0
            ? new JustJoinItPage(Items, Items.Count, null)
            : new JustJoinItPage([], Items.Count, null);
        return Task.FromResult<(FetchStatus, JustJoinItPage?)>((FetchStatus.Ok, page));
    }

    public Task<JsonElement?> FetchDetailAsync(string slug, CancellationToken ct)
    {
        if (!_bodies.TryGetValue(slug, out var body) || body is null)
        {
            return Task.FromResult<JsonElement?>(null);
        }

        var json = new JsonObject { ["body"] = body }.ToJsonString();
        return Task.FromResult<JsonElement?>(JsonDocument.Parse(json).RootElement.Clone());
    }

    private static JsonElement MakeItem(string guid, decimal salaryMax) =>
        JsonDocument.Parse(
            $$"""
            {
              "guid": "{{guid}}",
              "slug": "{{guid}}-slug",
              "title": "Role {{guid}}",
              "companyName": "Co {{guid}}",
              "workplaceType": "remote",
              "experienceLevel": "senior",
              "requiredSkills": ["C#", ".NET"],
              "niceToHaveSkills": [],
              "employmentTypes": [
                { "type": "b2b", "from": 18000, "to": {{salaryMax}}, "currency": "pln", "unit": "month", "gross": false }
              ],
              "multilocation": [{ "city": "Remote", "slug": "remote" }],
              "publishedAt": "2026-06-20T08:00:00.000Z"
            }
            """).RootElement.Clone();

    private static JsonElement MakeDetailedItem(string guid, string company, string title, string city, string workplace) =>
        JsonDocument.Parse(
            $$"""
            {
              "guid": "{{guid}}",
              "slug": "{{guid}}-slug",
              "title": "{{title}}",
              "companyName": "{{company}}",
              "workplaceType": "{{workplace}}",
              "experienceLevel": "senior",
              "requiredSkills": ["C#", ".NET"],
              "niceToHaveSkills": [],
              "employmentTypes": [
                { "type": "b2b", "from": 18000, "to": 22000, "currency": "pln", "unit": "month", "gross": false }
              ],
              "multilocation": [{ "city": "{{city}}", "slug": "{{city}}" }],
              "publishedAt": "2026-06-20T08:00:00.000Z"
            }
            """).RootElement.Clone();
}
