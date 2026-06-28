using JobOfferMatcher.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

internal sealed class ScheduleConfigConfiguration : IEntityTypeConfiguration<ScheduleConfig>
{
    public void Configure(EntityTypeBuilder<ScheduleConfig> builder)
    {
        builder.ToTable("schedule_config");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(s => s.Cron).HasColumnName("cron").HasMaxLength(120);
        builder.Property(s => s.TimeZone).HasColumnName("time_zone").HasMaxLength(120);
        builder.Property(s => s.Enabled).HasColumnName("enabled");
        builder.Property(s => s.LastRunUtc).HasColumnName("last_run_utc");
    }
}
