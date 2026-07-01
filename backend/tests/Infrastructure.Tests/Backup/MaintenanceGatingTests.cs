using System.Net;
using System.Net.Http.Json;
using JobOfferMatcher.Application.Applications;
using JobOfferMatcher.Application.Enrichment;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Application.TailoredCvs;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Infrastructure.Tests.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace JobOfferMatcher.Infrastructure.Tests.Backup;

/// <summary>
/// US2 real-host gating test (003 T029, FR-020): while a restore holds the <see cref="MaintenanceGate"/>,
/// a manual scan is rejected (the orchestrator consult point) and an enrichment write-back defers until
/// the restore ends (the enrichment consult point).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MaintenanceGatingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_scan_is_rejected_and_an_enrichment_write_defers_during_maintenance()
    {
        await postgres.ResetAsync();
        await using var factory = new JobApiFactory(postgres.ConnectionString, new MutableJustJoinItClient());
        var client = factory.CreateClient();
        var gate = factory.Services.GetRequiredService<MaintenanceGate>();

        (await gate.TryBeginRestoreAsync(TimeSpan.FromSeconds(1))).ShouldBeTrue();
        try
        {
            // (1) Scan orchestrator consult point — a manual scan is refused while maintenance is active.
            var scan = await client.PostAsJsonAsync("/api/scans/run", new { sourceIds = (string[]?)null });
            scan.StatusCode.ShouldBe(HttpStatusCode.Conflict);

            // (2) Enrichment write-back consult point — SubmitResultsAsync defers until the restore ends.
            using var scope = factory.Services.CreateScope();
            var enrichment = scope.ServiceProvider.GetRequiredService<EnrichmentService>();
            var submit = enrichment.SubmitResultsAsync(new SubmitResultsRequest([]));

            await Task.Delay(150);
            submit.IsCompleted.ShouldBeFalse("the enrichment write should defer while a restore is active");

            gate.EndRestore();
            await submit.WaitAsync(TimeSpan.FromSeconds(5)); // resumes once the restore ends
        }
        finally
        {
            if (gate.IsMaintenanceActive)
            {
                gate.EndRestore();
            }
        }
    }

    [Fact]
    public async Task A_tailored_cv_writeback_defers_during_maintenance()
    {
        await postgres.ResetAsync();
        await using var factory = new JobApiFactory(postgres.ConnectionString, new MutableJustJoinItClient());
        var gate = factory.Services.GetRequiredService<MaintenanceGate>();

        (await gate.TryBeginRestoreAsync(TimeSpan.FromSeconds(1))).ShouldBeTrue();
        try
        {
            // Tailored-CV write-back consult point (004 T049) — SubmitResultsAsync defers until the restore ends.
            using var scope = factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TailoredCvService>();
            var submit = service.SubmitResultsAsync(new TailoredCvSubmitRequest([]));

            await Task.Delay(150);
            submit.IsCompleted.ShouldBeFalse("the tailored-CV write should defer while a restore is active");

            gate.EndRestore();
            await submit.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (gate.IsMaintenanceActive)
            {
                gate.EndRestore();
            }
        }
    }

    [Fact]
    public async Task An_application_write_defers_during_maintenance()
    {
        await postgres.ResetAsync();
        await using var factory = new JobApiFactory(postgres.ConnectionString, new MutableJustJoinItClient());
        var gate = factory.Services.GetRequiredService<MaintenanceGate>();

        (await gate.TryBeginRestoreAsync(TimeSpan.FromSeconds(1))).ShouldBeTrue();
        try
        {
            // Application write consult point (005 T062) — a write path (AddNoteAsync) awaits
            // WaitWhileActiveAsync as its first line, so it defers until the restore ends.
            using var scope = factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ApplicationTrackingService>();
            var write = service.AddNoteAsync(OfferId.New(), "note during restore");

            await Task.Delay(150);
            write.IsCompleted.ShouldBeFalse("the application write should defer while a restore is active");

            gate.EndRestore();
            await write.WaitAsync(TimeSpan.FromSeconds(5)); // resumes once the restore ends
        }
        finally
        {
            if (gate.IsMaintenanceActive)
            {
                gate.EndRestore();
            }
        }
    }
}
