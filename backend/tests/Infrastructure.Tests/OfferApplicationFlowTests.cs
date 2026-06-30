using System.Net;
using System.Net.Http.Json;
using JobOfferMatcher.Infrastructure.Tests.Sources;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Integration test (real Postgres): the "applied" flag + optional date/note round-trips through the
/// PUT/DELETE endpoints, persists across scans, drives the <c>applied</c> feed filter, and clears.
/// Offers are matched by title (like <see cref="UserStatusTests"/>) since the seeded non-faked sources
/// may also contribute offers.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OfferApplicationFlowTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Applied_flag_with_date_and_note_persists_filters_and_clears()
    {
        await postgres.ResetAsync();
        var client = new MutableJustJoinItClient();
        await using var factory = new JobApiFactory(postgres.ConnectionString, client);
        var http = factory.CreateClient();

        client.SetOffers(("applied-me", 22000), ("untouched", 20000));
        await RunScanAsync(http);

        var target = (await GetOffersAsync(http, "all")).Single(o => o.Title == "Role applied-me");
        target.Applied.ShouldBeFalse();

        // Mark applied with a date + note.
        var mark = await http.PutAsJsonAsync(
            $"/api/offers/{target.OfferId}/application",
            new { appliedAt = "2026-06-20", note = "  referred by Anna  " });
        mark.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // It appears under the applied filter, with the trimmed note + date; the other fake offer does not.
        var applied = await GetOffersAsync(http, "all", applied: true);
        var marked = applied.Single(o => o.Title == "Role applied-me");
        marked.Applied.ShouldBeTrue();
        marked.ApplicationNote.ShouldBe("referred by Anna");
        marked.AppliedAt!.Value.UtcDateTime.Date.ShouldBe(new DateTime(2026, 6, 20));
        applied.ShouldNotContain(o => o.Title == "Role untouched");

        // The applied=false branch is the complement: excludes the marked offer, keeps the rest.
        var notApplied = await GetOffersAsync(http, "all", applied: false);
        notApplied.ShouldNotContain(o => o.Title == "Role applied-me");
        notApplied.ShouldContain(o => o.Title == "Role untouched");

        // An `Applied` event was appended to the append-only timeline (FR-009/034).
        (await GetEventTypesAsync(http, target.OfferId)).ShouldContain("Applied");

        // Re-scan: the applied flag + metadata survive.
        await RunScanAsync(http);
        var afterRescan = (await GetOffersAsync(http, "all", applied: true)).Single(o => o.Title == "Role applied-me");
        afterRescan.Applied.ShouldBeTrue();
        afterRescan.ApplicationNote.ShouldBe("referred by Anna");

        // Unmark: it leaves the applied filter and reads back as not-applied, with a cleared event.
        var clear = await http.DeleteAsync($"/api/offers/{target.OfferId}/application");
        clear.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await GetOffersAsync(http, "all", applied: true)).ShouldNotContain(o => o.Title == "Role applied-me");
        var cleared = (await GetOffersAsync(http, "all")).Single(o => o.Title == "Role applied-me");
        cleared.Applied.ShouldBeFalse();
        cleared.AppliedAt.ShouldBeNull();
        cleared.ApplicationNote.ShouldBeNull();
        (await GetEventTypesAsync(http, target.OfferId)).ShouldContain("ApplicationCleared");
    }

    [Fact]
    public async Task Validation_and_not_found_paths_return_the_documented_4xx()
    {
        await postgres.ResetAsync();
        var client = new MutableJustJoinItClient();
        await using var factory = new JobApiFactory(postgres.ConnectionString, client);
        var http = factory.CreateClient();

        client.SetOffers(("v", 22000));
        await RunScanAsync(http);
        var offer = (await GetOffersAsync(http, "all")).Single(o => o.Title == "Role v");

        // Unparseable appliedAt → 400 InvalidDate.
        var badDate = await http.PutAsJsonAsync(
            $"/api/offers/{offer.OfferId}/application", new { appliedAt = "not-a-date", note = (string?)null });
        badDate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(badDate)).ShouldBe("InvalidDate");

        // Note over the 2000-char cap → 400 ApplicationNoteTooLong (Domain Result → problem envelope).
        var longNote = await http.PutAsJsonAsync(
            $"/api/offers/{offer.OfferId}/application", new { appliedAt = (string?)null, note = new string('x', 2001) });
        longNote.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(longNote)).ShouldBe("ApplicationNoteTooLong");

        // Unknown offer id → 404 on both verbs.
        var missing = Guid.NewGuid();
        (await http.PutAsJsonAsync($"/api/offers/{missing}/application", new { appliedAt = (string?)null, note = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await http.DeleteAsync($"/api/offers/{missing}/application")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The over-long note never partially applied the offer.
        (await GetOffersAsync(http, "all")).Single(o => o.Title == "Role v").Applied.ShouldBeFalse();
    }

    [Fact]
    public async Task Clearing_a_never_applied_offer_succeeds_idempotently()
    {
        await postgres.ResetAsync();
        var client = new MutableJustJoinItClient();
        await using var factory = new JobApiFactory(postgres.ConnectionString, client);
        var http = factory.CreateClient();

        client.SetOffers(("never", 22000));
        await RunScanAsync(http);
        var offer = (await GetOffersAsync(http, "all")).Single(o => o.Title == "Role never");

        // DELETE on a never-applied offer → 204, stays not-applied, and logs NO ApplicationCleared event.
        (await http.DeleteAsync($"/api/offers/{offer.OfferId}/application")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetOffersAsync(http, "all")).Single(o => o.Title == "Role never").Applied.ShouldBeFalse();
        (await GetEventTypesAsync(http, offer.OfferId)).ShouldNotContain("ApplicationCleared");
    }

    [Fact]
    public async Task Mark_applied_without_a_date_keeps_the_flag_but_no_date()
    {
        await postgres.ResetAsync();
        var client = new MutableJustJoinItClient();
        await using var factory = new JobApiFactory(postgres.ConnectionString, client);
        var http = factory.CreateClient();

        client.SetOffers(("nodate", 22000));
        await RunScanAsync(http);
        var offer = (await GetOffersAsync(http, "all")).Single(o => o.Title == "Role nodate");

        var mark = await http.PutAsJsonAsync(
            $"/api/offers/{offer.OfferId}/application",
            new { appliedAt = (string?)null, note = (string?)null });
        mark.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var applied = (await GetOffersAsync(http, "all", applied: true)).Single(o => o.Title == "Role nodate");
        applied.Applied.ShouldBeTrue();
        applied.AppliedAt.ShouldBeNull();
        applied.ApplicationNote.ShouldBeNull();
    }

    private static async Task RunScanAsync(HttpClient http)
    {
        var response = await http.PostAsJsonAsync("/api/scans/run", new { sourceIds = (string[]?)null });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<List<OfferItem>> GetOffersAsync(HttpClient http, string status, bool? applied = null)
    {
        var url = $"/api/offers?status={status}&availability=all" + (applied is { } a ? $"&applied={a.ToString().ToLowerInvariant()}" : string.Empty);
        var envelope = await http.GetFromJsonAsync<OffersEnvelope>(url);
        return envelope!.Data;
    }

    private static async Task<List<string>> GetEventTypesAsync(HttpClient http, string offerId)
    {
        var detail = await http.GetFromJsonAsync<OfferDetailEnvelope>($"/api/offers/{offerId}");
        return detail!.Events.Select(e => e.Type).ToList();
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemEnvelope>();
        return problem?.Error?.Code;
    }

    private sealed record OffersEnvelope(List<OfferItem> Data);

    private sealed record OfferItem(
        string OfferId, string Title, bool Applied, DateTimeOffset? AppliedAt, string? ApplicationNote);

    private sealed record OfferDetailEnvelope(List<OfferEventItem> Events);

    private sealed record OfferEventItem(string Type);

    private sealed record ProblemEnvelope(ProblemError? Error);

    private sealed record ProblemError(string Code, string Message);
}
