using System.Diagnostics;
using System.Net;
using JobOfferMatcher.Application.Abstractions;
using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Application.Enrichment;
using JobOfferMatcher.Application.Export;
using JobOfferMatcher.Application.Offers;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Application.Scheduling;
using JobOfferMatcher.Application.Settings;
using JobOfferMatcher.Application.Sources;
using JobOfferMatcher.Application.Backup;
using JobOfferMatcher.Infrastructure.Backup;
using JobOfferMatcher.Infrastructure.Cv;
using JobOfferMatcher.Infrastructure.Persistence;
using JobOfferMatcher.Infrastructure.Persistence.Repositories;
using JobOfferMatcher.Infrastructure.Scheduling;
using JobOfferMatcher.Infrastructure.Sources;
using JobOfferMatcher.Infrastructure.Sources.Browser;
using JobOfferMatcher.Infrastructure.Sources.JustJoinIt;
using JobOfferMatcher.Infrastructure.Sources.NoFluffJobs;
using JobOfferMatcher.Infrastructure.Sources.TheProtocol;
using Ganss.Xss;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobOfferMatcher.Infrastructure;

/// <summary>Composition root for the Infrastructure layer (persistence, sources, scheduling).</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString("AppDb")
            ?? throw new InvalidOperationException(
                "Connection string 'AppDb' is not configured. Set it in user-secrets (see quickstart.md).");

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npg =>
                npg.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name)));

        services.AddSingleton(TimeProvider.System);

        // Persistence ports
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IJobSourceRepository, JobSourceRepository>();
        services.AddScoped<IOfferRepository, OfferRepository>();
        services.AddScoped<IScanRunRepository, ScanRunRepository>();
        services.AddScoped<IOfferReadService, OfferReadService>();
        services.AddScoped<IScheduleRepository, ScheduleRepository>();
        services.AddScoped<ICvRepository, CvRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<IRoleGroupRepository, RoleGroupRepository>();
        services.AddScoped<IExportReader, ExportReader>();
        services.AddScoped<IEnrichmentRepository, EnrichmentRepository>();

        // Backup/restore ports (003 ADR-1): in-process Npgsql COPY snapshot, zip archive, EF migration
        // inspector, safety pre-backup. The backups dir is resolved via Backup:StoragePath (BackupStorage).
        services.AddScoped<IMigrationInspector, EfMigrationInspector>();
        services.AddScoped<IDatabaseSnapshotStore, PostgresSnapshotStore>();
        services.AddScoped<IBackupArchiveStore, ZipBackupArchiveStore>();
        services.AddScoped<ISafetyBackupStore, LocalSafetyBackupStore>();
        services.AddScoped<IEnrichmentBackfill, EnrichmentBackfillRunner>();

        // HTML sanitizer (default safe allow-list) for source-supplied offer descriptions (XSS, T068).
        services.AddSingleton<IHtmlSanitizer>(_ => new HtmlSanitizer());

        // CV pipeline: PdfPig is retained only as a readability gauge + text fallback (ADR-2). The
        // keyword profiler/skill catalog were removed (FR-005) — Claude is the sole profiler now.
        services.AddScoped<ICvTextExtractor, PdfPigCvTextExtractor>();
        services.AddSingleton<ICvFileStore, LocalCvFileStore>();

        // Scheduling: Cronos cron math + the poll-tick BackgroundService (research §3).
        services.AddSingleton<ICronEvaluator, CronosCronEvaluator>();
        if (configuration.GetValue("Scheduler:Enabled", true))
        {
            services.AddHostedService<ScanSchedulerService>();
        }

        // Source adapters (each well-known DirectApi source = options + typed HttpClient; FR-003).
        services.Configure<JustJoinItOptions>(configuration.GetSection(JustJoinItOptions.SectionName));
        services.AddHttpClient<IJustJoinItClient, HttpJustJoinItClient>();

        services.Configure<TheProtocolOptions>(configuration.GetSection(TheProtocolOptions.SectionName));
        if (configuration.GetValue($"{TheProtocolOptions.SectionName}:UseBrowser", true))
        {
            // theprotocol fronts its listing with a Cloudflare JS challenge no plain HTTP client can pass,
            // so collection runs through a real headless browser that clears it (FR-040). Singleton: the
            // browser is launched lazily and reused across the single-flight scans.
            services.AddSingleton<ITheProtocolClient, PlaywrightTheProtocolClient>();
        }
        else
        {
            // Lighter fallback if the block ever lifts: suppress the auto-injected W3C trace headers
            // (no browser sends them) and accept compressed responses so the GET looks browser-shaped.
            services.AddHttpClient<ITheProtocolClient, HttpTheProtocolClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    AutomaticDecompression = DecompressionMethods.All,
                    ActivityHeadersPropagator = DistributedContextPropagator.CreateNoOutputPropagator(),
                });
        }

        services.Configure<NoFluffJobsOptions>(configuration.GetSection(NoFluffJobsOptions.SectionName));
        services.AddHttpClient<INoFluffJobsClient, HttpNoFluffJobsClient>();

        services.AddScoped<IJobSourceFactory, JobSourceFactory>();

        // Deferred manual-login path (port wired; Playwright adapter not built — FR-040).
        services.AddSingleton<IInteractiveBrowserSession, NotConfiguredInteractiveBrowserSession>();

        return services;
    }
}
