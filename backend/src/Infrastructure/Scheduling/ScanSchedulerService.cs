using JobOfferMatcher.Application.Abstractions;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Application.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JobOfferMatcher.Infrastructure.Scheduling;

/// <summary>
/// The scan scheduler (research §3): a short poll-tick (~45 s) that computes the previous cron
/// occurrence and runs ONE catch-up if we haven't run it yet — robust to laptop sleep/resume, not
/// just process restart. Injected <see cref="TimeProvider"/> makes the policy deterministic;
/// single-flight + <c>UNIQUE(window_utc, trigger)</c> make it idempotent. Runs UI-independently.
/// </summary>
public sealed class ScanSchedulerService(
    IServiceScopeFactory scopeFactory,
    ICronEvaluator cron,
    MaintenanceGate maintenance,
    TimeProvider time,
    ILogger<ScanSchedulerService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(45);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval, time);
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // A bad tick must never kill the scheduler loop.
                logger.LogError(ex, "Scheduler tick failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        // Pause scheduled scans while a restore is wiping/reloading the DB (FR-020) — retry next tick.
        if (maintenance.IsMaintenanceActive)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var schedules = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
        var runner = scope.ServiceProvider.GetRequiredService<IScanRunner>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var config = await schedules.GetAsync(ct);
        var now = time.GetUtcNow();
        var previous = cron.GetPreviousOccurrence(config.Cron, config.TimeZone, now);
        var decision = CatchUpPolicy.Decide(config.Enabled, config.LastRunUtc, previous);

        if (!decision.ShouldRun || decision.AdvanceTo is not { } window)
        {
            return;
        }

        var result = await runner.RunAsync(new ScanRequest(null, decision.Trigger, window), ct);
        if (result.IsSuccess)
        {
            // Collapse all missed windows into the most-recent occurrence (idempotent catch-up).
            config.AdvanceLastRun(window);
            await schedules.SaveAsync(config, ct);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("Scheduled scan ({Trigger}) ran for window {Window}.", decision.Trigger, window);
        }
        else
        {
            // e.g. another scan already running — try again next tick (no advance).
            logger.LogInformation("Scheduled scan deferred: {Error}", result.Error);
        }
    }
}
