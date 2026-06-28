using JobOfferMatcher.Application.Abstractions;
using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Application.Export;
using JobOfferMatcher.Application.Offers;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Application.Scheduling;
using JobOfferMatcher.Application.Settings;
using JobOfferMatcher.Application.Sources;
using JobOfferMatcher.Infrastructure.Cv;
using JobOfferMatcher.Infrastructure.Persistence;
using JobOfferMatcher.Infrastructure.Persistence.Repositories;
using JobOfferMatcher.Infrastructure.Scheduling;
using JobOfferMatcher.Infrastructure.Sources;
using JobOfferMatcher.Infrastructure.Sources.Browser;
using JobOfferMatcher.Infrastructure.Sources.JustJoinIt;
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

        // HTML sanitizer (default safe allow-list) for source-supplied offer descriptions (XSS, T068).
        services.AddSingleton<IHtmlSanitizer>(_ => new HtmlSanitizer());

        // CV pipeline: PdfPig extraction, the editable skill catalog (loaded once), profile derivation.
        services.AddSingleton(_ => SkillCatalogLoader.Load());
        services.AddScoped<ICvTextExtractor, PdfPigCvTextExtractor>();
        services.AddScoped<ICvProfileBuilder, CvProfileBuilder>();
        services.AddSingleton<ICvFileStore, LocalCvFileStore>();

        // Scheduling: Cronos cron math + the poll-tick BackgroundService (research §3).
        services.AddSingleton<ICronEvaluator, CronosCronEvaluator>();
        if (configuration.GetValue("Scheduler:Enabled", true))
        {
            services.AddHostedService<ScanSchedulerService>();
        }

        // Source adapters
        services.Configure<JustJoinItOptions>(configuration.GetSection(JustJoinItOptions.SectionName));
        services.AddHttpClient<IJustJoinItClient, HttpJustJoinItClient>();
        services.AddScoped<IJobSourceFactory, JobSourceFactory>();

        // Deferred manual-login path (port wired; Playwright adapter not built — FR-040).
        services.AddSingleton<IInteractiveBrowserSession, NotConfiguredInteractiveBrowserSession>();

        return services;
    }
}
