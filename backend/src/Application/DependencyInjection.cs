using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Application.Export;
using JobOfferMatcher.Application.Offers;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Application.Scheduling;
using JobOfferMatcher.Application.Settings;
using JobOfferMatcher.Application.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace JobOfferMatcher.Application;

/// <summary>Composition root for the Application layer (use cases, orchestration).</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Single-flight guard is process-wide → singleton (a DI singleton, not a static).
        services.AddSingleton<ScanConcurrencyGuard>();
        services.AddScoped<RoleGroupingService>();
        services.AddScoped<IScanRunner, ScanOrchestrator>();
        services.AddScoped<SetUserOfferStatus>();
        services.AddScoped<ScheduleService>();
        services.AddScoped<CvService>();
        services.AddScoped<ProfileService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<SourceService>();
        services.AddScoped<RoleGroupService>();
        services.AddScoped<ExportService>();

        return services;
    }
}
