using System.Net;
using System.Net.Http.Json;
using JobOfferMatcher.Infrastructure.Tests.Sources;

namespace JobOfferMatcher.Infrastructure.Tests.Applications;

/// <summary>
/// Integration tests (real Postgres) for US1 (T034/T035): marking an offer applied creates an application
/// on the pipeline board; free stage movement; close-with-outcome + reopen (append <c>offer_event</c>);
/// the stage-delete reassign guard; the clear-prefers-closing guard; confirmed permanent delete; and an
/// application whose offer is no-longer-available still appears on the board and is retrievable (FR-012).
/// Offers are matched by title (seeded non-faked sources may also contribute offers).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ApplicationFlowTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Applying_creates_a_board_card_and_the_lifecycle_moves_close_and_reopen()
    {
        await using var ctx = await StartAsync("lifecycle");
        var offerId = await MarkAppliedAsync(ctx.Http, "Role lifecycle", note: null);

        // It appears under the first stage (Applied) with the offer's title/company.
        var board = await GetBoardAsync(ctx.Http);
        var applied = board.Stages.First(s => s.Name == "Applied");
        applied.Applications.ShouldContain(c => c.OfferId == offerId);
        board.Stages.First().Position.ShouldBe(0);

        // Move to Screening — the card changes column and the stage persists; a stage-changed event is logged.
        var screening = board.Stages.First(s => s.Name == "Screening");
        (await ctx.Http.PostAsJsonAsync($"/api/applications/{offerId}/stage", new { stageId = screening.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterMove = await GetBoardAsync(ctx.Http);
        afterMove.Stages.First(s => s.Name == "Screening").Applications.ShouldContain(c => c.OfferId == offerId);
        afterMove.Stages.First(s => s.Name == "Applied").Applications.ShouldNotContain(c => c.OfferId == offerId);

        var detail = await GetDetailAsync(ctx.Http, offerId);
        detail.StageId.ShouldBe(screening.Id);
        detail.Status.ShouldBe("active");
        detail.Timeline.ShouldContain(e => e.Kind == "stageChanged");

        // Close (Rejected): it leaves the active columns and shows in the closed section tagged with its outcome.
        (await ctx.Http.PostAsJsonAsync($"/api/applications/{offerId}/close", new { outcome = "rejected" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var closedBoard = await GetBoardAsync(ctx.Http);
        closedBoard.Stages.SelectMany(s => s.Applications).ShouldNotContain(c => c.OfferId == offerId);
        closedBoard.Closed.ShouldContain(c => c.OfferId == offerId && c.Status == "closed" && c.Outcome == "rejected");

        // Reopen → back to the active pipeline (stage retained).
        (await ctx.Http.PostAsync($"/api/applications/{offerId}/reopen", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var reopened = await GetBoardAsync(ctx.Http);
        reopened.Closed.ShouldNotContain(c => c.OfferId == offerId);
        reopened.Stages.First(s => s.Name == "Screening").Applications.ShouldContain(c => c.OfferId == offerId);

        var timelineKinds = (await GetDetailAsync(ctx.Http, offerId)).Timeline.Select(e => e.Kind).ToList();
        timelineKinds.ShouldContain("stageChanged");
        timelineKinds.ShouldContain("closed");
        timelineKinds.ShouldContain("reopened");
    }

    [Fact]
    public async Task Invalid_lifecycle_transitions_are_rejected_with_409()
    {
        await using var ctx = await StartAsync("invalid");
        var offerId = await MarkAppliedAsync(ctx.Http, "Role invalid", note: null);

        // Reopen while active → 409.
        (await ctx.Http.PostAsync($"/api/applications/{offerId}/reopen", null))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await ctx.Http.PostAsJsonAsync($"/api/applications/{offerId}/close", new { outcome = "withdrawn" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Close again → 409; move while closed → 409.
        (await ctx.Http.PostAsJsonAsync($"/api/applications/{offerId}/close", new { outcome = "rejected" }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var stages = await GetStagesAsync(ctx.Http);
        (await ctx.Http.PostAsJsonAsync($"/api/applications/{offerId}/stage", new { stageId = stages[1].Id }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Removing_an_occupied_stage_needs_a_reassignment_target()
    {
        await using var ctx = await StartAsync("stagedelete");
        var offerId = await MarkAppliedAsync(ctx.Http, "Role stagedelete", note: null);

        var stages = await GetStagesAsync(ctx.Http);
        var applied = stages.First(s => s.Name == "Applied");
        var screening = stages.First(s => s.Name == "Screening");

        // Removing the occupied "Applied" stage without a target → 409 StageInUse.
        var blocked = await ctx.Http.DeleteAsync($"/api/applications/stages/{applied.Id}");
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(blocked)).ShouldBe("StageInUse");

        // With a valid reassignment target → 204, and the application moves to that stage.
        (await ctx.Http.DeleteAsync($"/api/applications/stages/{applied.Id}?reassignTo={screening.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await GetStagesAsync(ctx.Http)).ShouldNotContain(s => s.Id == applied.Id);
        (await GetDetailAsync(ctx.Http, offerId)).StageId.ShouldBe(screening.Id);
    }

    [Fact]
    public async Task Clearing_prefers_closing_when_the_application_has_history()
    {
        await using var ctx = await StartAsync("clearguard", "Role clearguard-history", "Role clearguard-plain");

        // Applied and then tracked (a task) → real process history → clearing is steered to close (409).
        var withHistory = await MarkAppliedAsync(ctx.Http, "Role clearguard-history", note: "referred by Anna");
        (await ctx.Http.PostAsJsonAsync($"/api/applications/{withHistory}/tasks", new { title = "prep interview" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var blocked = await ctx.Http.DeleteAsync($"/api/offers/{withHistory}/application");
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(blocked)).ShouldBe("ApplicationHasHistory");
        (await GetDetailAsync(ctx.Http, withHistory)).OfferId.ShouldBe(withHistory); // still present

        // Applied but only a journal note (no process history) → clearing succeeds and removes the application
        // (the pre-005 un-apply flow is preserved — FR-016).
        var plain = await MarkAppliedAsync(ctx.Http, "Role clearguard-plain", note: "just a note");
        (await ctx.Http.DeleteAsync($"/api/offers/{plain}/application")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ctx.Http.GetAsync($"/api/applications/{plain}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Permanent_delete_requires_confirmation_and_un_applies_the_offer()
    {
        await using var ctx = await StartAsync("delete");
        var offerId = await MarkAppliedAsync(ctx.Http, "Role delete", note: "keep me");

        // Without confirm → 409 ConfirmationRequired.
        var unconfirmed = await ctx.Http.DeleteAsync($"/api/applications/{offerId}");
        unconfirmed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(unconfirmed)).ShouldBe("ConfirmationRequired");

        // With confirm → 204; the application is gone AND the offer is no longer applied (so backfill won't resurrect it).
        (await ctx.Http.DeleteAsync($"/api/applications/{offerId}?confirm=true")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ctx.Http.GetAsync($"/api/applications/{offerId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var offer = (await GetOffersAsync(ctx.Http)).Single(o => o.OfferId == offerId);
        offer.Applied.ShouldBeFalse();
    }

    [Fact]
    public async Task An_application_whose_offer_is_no_longer_available_stays_on_the_board()
    {
        await using var ctx = await StartAsync("gone", "Role gone", "Role stays");
        var offerId = await MarkAppliedAsync(ctx.Http, "Role gone", note: null);

        // Re-scan WITHOUT the applied offer → it becomes no-longer-available.
        ctx.Client.SetOffers(("stays", 20000));
        (await ctx.Http.PostAsJsonAsync("/api/scans/run", new { sourceIds = (string[]?)null })).EnsureSuccessStatusCode();

        // FR-012: it still appears on the board and its detail is retrievable.
        var board = await GetBoardAsync(ctx.Http);
        board.Stages.SelectMany(s => s.Applications).ShouldContain(c => c.OfferId == offerId);
        (await ctx.Http.GetAsync($"/api/applications/{offerId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- Harness ---

    private async Task<TestContext> StartAsync(string slug, params string[] roleTitles)
    {
        await postgres.ResetAsync();
        var client = new MutableJustJoinItClient();
        var factory = new JobApiFactory(postgres.ConnectionString, client);
        var http = factory.CreateClient();

        (string, decimal)[] offers = roleTitles.Length > 0
            ? roleTitles.Select(t => (t.Replace("Role ", string.Empty), 22000m)).ToArray()
            : [(slug, 22000m)];
        client.SetOffers(offers);
        (await http.PostAsJsonAsync("/api/scans/run", new { sourceIds = (string[]?)null })).EnsureSuccessStatusCode();

        return new TestContext(factory, http, client);
    }

    private static async Task<string> MarkAppliedAsync(HttpClient http, string title, string? note)
    {
        var offer = (await GetOffersAsync(http)).Single(o => o.Title == title);
        var response = await http.PutAsJsonAsync(
            $"/api/offers/{offer.OfferId}/application",
            new { appliedAt = "2026-06-20", note });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return offer.OfferId;
    }

    private static async Task<List<OfferItem>> GetOffersAsync(HttpClient http)
    {
        var envelope = await http.GetFromJsonAsync<OffersEnvelope>("/api/offers?status=all&availability=all");
        return envelope!.Data;
    }

    private static async Task<BoardDto> GetBoardAsync(HttpClient http) =>
        (await http.GetFromJsonAsync<BoardDto>("/api/applications"))!;

    private static async Task<List<StageDto>> GetStagesAsync(HttpClient http) =>
        (await http.GetFromJsonAsync<List<StageDto>>("/api/applications/stages"))!;

    private static async Task<DetailDto> GetDetailAsync(HttpClient http, string offerId) =>
        (await http.GetFromJsonAsync<DetailDto>($"/api/applications/{offerId}"))!;

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemEnvelope>())?.Error?.Code;

    private sealed record TestContext(JobApiFactory Factory, HttpClient Http, MutableJustJoinItClient Client) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }

    private sealed record OffersEnvelope(List<OfferItem> Data);

    private sealed record OfferItem(string OfferId, string Title, bool Applied);

    private sealed record BoardDto(List<BoardStage> Stages, List<CardDto> Closed);

    private sealed record BoardStage(string Id, string Name, int Position, List<CardDto> Applications);

    private sealed record CardDto(
        string OfferId, string Title, string Company, string StageId, string Status, string? Outcome);

    private sealed record StageDto(string Id, string Name, int Position);

    private sealed record DetailDto(string OfferId, string StageId, string Status, string? Outcome, List<TimelineItem> Timeline);

    private sealed record TimelineItem(DateTimeOffset OccurredAt, string Kind, string Title, string? Detail);

    private sealed record ProblemEnvelope(ProblemError? Error);

    private sealed record ProblemError(string Code, string Message);
}
