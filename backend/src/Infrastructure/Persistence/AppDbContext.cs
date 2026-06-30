using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Cv;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.RoleGroups;
using JobOfferMatcher.Domain.Scans;
using JobOfferMatcher.Domain.Scheduling;
using JobOfferMatcher.Domain.Settings;
using JobOfferMatcher.Domain.Sources;
using JobOfferMatcher.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;

namespace JobOfferMatcher.Infrastructure.Persistence;

/// <summary>
/// The application's EF Core context (PostgreSQL). Append-only migrations are applied at host
/// startup via <c>MigrateAsync()</c> (research §5, Principle IX). Strongly-typed ids are mapped
/// globally so no raw <see cref="Guid"/> appears in the model (Principle II).
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<JobSource> JobSources => Set<JobSource>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<OfferObservation> OfferObservations => Set<OfferObservation>();
    public DbSet<OfferVersion> OfferVersions => Set<OfferVersion>();
    public DbSet<OfferEvent> OfferEvents => Set<OfferEvent>();
    public DbSet<ScanRun> ScanRuns => Set<ScanRun>();
    public DbSet<ScheduleConfig> ScheduleConfigs => Set<ScheduleConfig>();
    public DbSet<CandidateCv> CandidateCvs => Set<CandidateCv>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<RoleGroup> RoleGroups => Set<RoleGroup>();
    public DbSet<OfferEnrichment> OfferEnrichments => Set<OfferEnrichment>();
    public DbSet<OfferFit> OfferFits => Set<OfferFit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<OfferId>().HaveConversion<OfferIdConverter>();
        configurationBuilder.Properties<SourceId>().HaveConversion<SourceIdConverter>();
        configurationBuilder.Properties<ScanRunId>().HaveConversion<ScanRunIdConverter>();
        configurationBuilder.Properties<OfferVersionId>().HaveConversion<OfferVersionIdConverter>();
        configurationBuilder.Properties<OfferObservationId>().HaveConversion<OfferObservationIdConverter>();
        configurationBuilder.Properties<OfferEventId>().HaveConversion<OfferEventIdConverter>();
        configurationBuilder.Properties<RoleGroupId>().HaveConversion<RoleGroupIdConverter>();
        configurationBuilder.Properties<CvId>().HaveConversion<CvIdConverter>();
    }
}
