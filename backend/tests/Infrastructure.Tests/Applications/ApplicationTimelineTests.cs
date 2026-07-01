using System.Net;
using System.Net.Http.Json;
using JobOfferMatcher.Infrastructure.Tests.Sources;

namespace JobOfferMatcher.Infrastructure.Tests.Applications;

/// <summary>
/// Integration tests (real Postgres) for the notes journal + derived timeline (US2, T045) and
/// communications/interviews (US5, T059): notes append (never overwrite), the migrated legacy note is the
/// first journal entry, and stage changes / notes / communications / interviews interleave chronologically;
/// an interview scheduled in the future is <c>upcoming</c> and its outcome can be recorded.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ApplicationTimelineTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Notes_append_and_the_migrated_note_is_first_in_the_journal()
    {
        await using var factory = await StartAsync("journal");
        var http = factory.CreateClient();
        var offerId = await MarkAppliedAsync(http, "Role journal", appliedAt: "2026-06-01", note: "applied via referral");

        // Add two later notes — neither overwrites the migrated one or each other.
        (await http.PostAsJsonAsync($"/api/applications/{offerId}/notes", new { body = "recruiter called" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await http.PostAsJsonAsync($"/api/applications/{offerId}/notes", new { body = "sent portfolio" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var detail = await GetDetailAsync(http, offerId);
        detail.Notes.Count.ShouldBe(3);
        detail.Notes[0].Body.ShouldBe("applied via referral"); // the migrated note, dated at apply time, sorts first
        detail.Notes.Select(n => n.Body).ShouldBe(["applied via referral", "recruiter called", "sent portfolio"]);

        // The timeline includes all three notes in chronological order.
        var noteEntries = detail.Timeline.Where(e => e.Kind == "note").Select(e => e.Detail).ToList();
        noteEntries.ShouldBe(["applied via referral", "recruiter called", "sent portfolio"]);
    }

    [Fact]
    public async Task Stage_changes_notes_communications_and_interviews_interleave_in_the_timeline()
    {
        await using var factory = await StartAsync("interleave");
        var http = factory.CreateClient();
        var offerId = await MarkAppliedAsync(http, "Role interleave", appliedAt: "2026-06-01", note: null);

        var stages = await http.GetFromJsonAsync<List<StageDto>>("/api/applications/stages");
        (await http.PostAsJsonAsync($"/api/applications/{offerId}/stage", new { stageId = stages![1].Id }))
            .EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync($"/api/applications/{offerId}/notes", new { body = "prep done" }))
            .EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync(
            $"/api/applications/{offerId}/communications",
            new { occurredAt = "2026-06-05T10:00:00Z", direction = "inbound", channel = "email", summary = "recruiter intro" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // A future interview → upcoming.
        var interviewResponse = await http.PostAsJsonAsync(
            $"/api/applications/{offerId}/interviews",
            new { kind = "phone screen", scheduledAt = "2099-01-01T09:00:00Z", interviewer = "Anna" });
        interviewResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var interview = (await interviewResponse.Content.ReadFromJsonAsync<InterviewDto>())!;
        interview.Upcoming.ShouldBeTrue();

        var detail = await GetDetailAsync(http, offerId);
        var kinds = detail.Timeline.Select(e => e.Kind).ToList();
        kinds.ShouldContain("stageChanged");
        kinds.ShouldContain("note");
        kinds.ShouldContain("communication");
        kinds.ShouldContain("interview");
        // Ordered by time (non-decreasing).
        detail.Timeline.Select(e => e.OccurredAt).ShouldBe(detail.Timeline.Select(e => e.OccurredAt).OrderBy(t => t));

        // Record the interview outcome — it updates and stays retrievable.
        (await http.PutAsJsonAsync(
            $"/api/applications/{offerId}/interviews/{interview.Id}",
            new { outcome = "advanced to on-site" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var updated = (await GetDetailAsync(http, offerId)).Interviews.Single();
        updated.Outcome.ShouldBe("advanced to on-site");
        updated.Upcoming.ShouldBeTrue(); // still future-scheduled
    }

    private async Task<JobApiFactory> StartAsync(string slug)
    {
        await postgres.ResetAsync();
        var client = new MutableJustJoinItClient();
        var factory = new JobApiFactory(postgres.ConnectionString, client);
        client.SetOffers((slug, 22000m));
        (await factory.CreateClient().PostAsJsonAsync("/api/scans/run", new { sourceIds = (string[]?)null })).EnsureSuccessStatusCode();
        return factory;
    }

    private static async Task<string> MarkAppliedAsync(HttpClient http, string title, string appliedAt, string? note)
    {
        var envelope = await http.GetFromJsonAsync<OffersEnvelope>("/api/offers?status=all&availability=all");
        var offer = envelope!.Data.Single(o => o.Title == title);
        (await http.PutAsJsonAsync($"/api/offers/{offer.OfferId}/application", new { appliedAt, note })).EnsureSuccessStatusCode();
        return offer.OfferId;
    }

    private static async Task<DetailDto> GetDetailAsync(HttpClient http, string offerId) =>
        (await http.GetFromJsonAsync<DetailDto>($"/api/applications/{offerId}"))!;

    private sealed record OffersEnvelope(List<OfferItem> Data);

    private sealed record OfferItem(string OfferId, string Title);

    private sealed record StageDto(string Id, string Name, int Position);

    private sealed record DetailDto(string OfferId, List<NoteDto> Notes, List<InterviewDto> Interviews, List<TimelineItem> Timeline);

    private sealed record NoteDto(string Id, string Body, DateTimeOffset CreatedAt);

    private sealed record InterviewDto(string Id, string Kind, DateTimeOffset? ScheduledAt, string? Outcome, bool Upcoming);

    private sealed record TimelineItem(DateTimeOffset OccurredAt, string Kind, string Title, string? Detail);
}
