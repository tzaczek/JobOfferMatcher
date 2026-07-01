using System.Net;
using System.Net.Http.Json;
using JobOfferMatcher.Infrastructure.Tests.Sources;

namespace JobOfferMatcher.Infrastructure.Tests.Applications;

/// <summary>
/// Integration tests (real Postgres) for interview tasks + deadlines (US3, T051): the board card surfaces
/// outstanding/overdue counts derived on read (without opening the application); completing a task drops it
/// from the outstanding count; a past-due, not-done task is counted overdue.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ApplicationTaskFlowTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Board_card_surfaces_outstanding_and_overdue_task_counts()
    {
        await postgres.ResetAsync();
        var client = new MutableJustJoinItClient();
        await using var factory = new JobApiFactory(postgres.ConnectionString, client);
        var http = factory.CreateClient();

        client.SetOffers(("tasks", 22000m));
        (await http.PostAsJsonAsync("/api/scans/run", new { sourceIds = (string[]?)null })).EnsureSuccessStatusCode();

        var offer = (await http.GetFromJsonAsync<OffersEnvelope>("/api/offers?status=all&availability=all"))!
            .Data.Single(o => o.Title == "Role tasks");
        (await http.PutAsJsonAsync($"/api/offers/{offer.OfferId}/application", new { appliedAt = "2026-06-01", note = (string?)null }))
            .EnsureSuccessStatusCode();

        // One past-due (overdue) task + one future-due task.
        (await http.PostAsJsonAsync($"/api/applications/{offer.OfferId}/tasks", new { title = "send thank-you", dueAt = "2020-01-01T00:00:00Z" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var futureResponse = await http.PostAsJsonAsync(
            $"/api/applications/{offer.OfferId}/tasks", new { title = "prep system design", dueAt = "2099-01-01T00:00:00Z" });
        futureResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var futureTask = (await futureResponse.Content.ReadFromJsonAsync<TaskDto>())!;

        // The board card shows 2 outstanding, 1 overdue — without opening the application.
        var card = await FindCardAsync(http, offer.OfferId);
        card.OutstandingTaskCount.ShouldBe(2);
        card.OverdueTaskCount.ShouldBe(1);

        // Completing the future task drops the outstanding count; the overdue one remains.
        (await http.PutAsJsonAsync($"/api/applications/{offer.OfferId}/tasks/{futureTask.Id}", new { completed = true }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await FindCardAsync(http, offer.OfferId);
        after.OutstandingTaskCount.ShouldBe(1);
        after.OverdueTaskCount.ShouldBe(1);

        // The overdue flag is derived server-side on the detail task too.
        var detail = await http.GetFromJsonAsync<DetailDto>($"/api/applications/{offer.OfferId}");
        detail!.Tasks.Single(t => t.Title == "send thank-you").Overdue.ShouldBeTrue();
    }

    private static async Task<CardDto> FindCardAsync(HttpClient http, string offerId)
    {
        var board = (await http.GetFromJsonAsync<BoardDto>("/api/applications"))!;
        return board.Stages.SelectMany(s => s.Applications).Single(c => c.OfferId == offerId);
    }

    private sealed record OffersEnvelope(List<OfferItem> Data);

    private sealed record OfferItem(string OfferId, string Title);

    private sealed record TaskDto(string Id, string Title, bool Overdue);

    private sealed record DetailDto(List<TaskDto> Tasks);

    private sealed record BoardDto(List<BoardStage> Stages);

    private sealed record BoardStage(string Id, List<CardDto> Applications);

    private sealed record CardDto(string OfferId, int OutstandingTaskCount, int OverdueTaskCount);
}
