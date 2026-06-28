using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Scans;
using JobOfferMatcher.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

internal sealed class ScanRunConfiguration : IEntityTypeConfiguration<ScanRun>
{
    public void Configure(EntityTypeBuilder<ScanRun> builder)
    {
        builder.ToTable("scan_run");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(s => s.StartedAt).HasColumnName("started_at");
        builder.Property(s => s.FinishedAt).HasColumnName("finished_at");
        builder.Property(s => s.WindowUtc).HasColumnName("window_utc");
        builder.Property(s => s.Trigger).HasColumnName("trigger").HasConversion<string>().HasMaxLength(40);
        builder.Property(s => s.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(40);
        builder.Property(s => s.IncompleteReason).HasColumnName("incomplete_reason").HasConversion<string?>().HasMaxLength(60);

        builder.Property(s => s.SourceIds)
            .HasColumnName("source_ids")
            .HasJsonbListConversion<SourceId>();

        // Result counts (FR-020) as owned columns.
        builder.OwnsOne(s => s.Counts, c =>
        {
            c.Property(x => x.Collected).HasColumnName("count_collected");
            c.Property(x => x.New).HasColumnName("count_new");
            c.Property(x => x.Updated).HasColumnName("count_updated");
            c.Property(x => x.Unavailable).HasColumnName("count_unavailable");
            c.Property(x => x.Failed).HasColumnName("count_failed");
        });
        builder.Navigation(s => s.Counts).IsRequired();
        builder.Ignore(s => s.IsFinished);

        // Idempotent catch-up: a window+trigger fires at most once (research §3).
        builder.HasIndex(s => new { s.WindowUtc, s.Trigger }).IsUnique();
    }
}
