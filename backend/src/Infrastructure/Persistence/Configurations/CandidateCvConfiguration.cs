using JobOfferMatcher.Domain.Cv;
using JobOfferMatcher.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

internal sealed class CandidateCvConfiguration : IEntityTypeConfiguration<CandidateCv>
{
    public void Configure(EntityTypeBuilder<CandidateCv> builder)
    {
        builder.ToTable("candidate_cv");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(c => c.FileName).HasColumnName("file_name").HasMaxLength(400);
        builder.Property(c => c.ExtractedAt).HasColumnName("extracted_at");
        builder.Property(c => c.IsReadable).HasColumnName("is_readable");

        // AI profile (ADR-2): the keyword `derived_profile` is dropped; the worker-produced CvProfile
        // is stored as a single jsonb column (the established profile-as-jsonb convention).
        builder.Property(c => c.ProfileState).HasColumnName("profile_state").HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.ProfileAttempts).HasColumnName("profile_attempts");
        builder.Property(c => c.Profile).HasColumnName("profile").HasJsonbConversion<CvProfile>();
        builder.Property(c => c.EnrichmentInputHash).HasColumnName("enrichment_input_hash").HasMaxLength(80);
        builder.Property(c => c.ProfileProducedAt).HasColumnName("profile_produced_at");
    }
}
