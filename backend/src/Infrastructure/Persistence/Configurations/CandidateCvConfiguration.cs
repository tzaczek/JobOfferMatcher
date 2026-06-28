using JobOfferMatcher.Domain.Cv;
using JobOfferMatcher.Domain.Matching;
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
        builder.Property(c => c.DerivedProfile).HasColumnName("derived_profile").HasJsonbConversion<CandidateProfile>();
    }
}
