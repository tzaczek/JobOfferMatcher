using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.TailoredCvs;
using JobOfferMatcher.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

internal sealed class TailoredCvConfiguration : IEntityTypeConfiguration<TailoredCv>
{
    public void Configure(EntityTypeBuilder<TailoredCv> builder)
    {
        builder.ToTable("tailored_cv");
        builder.HasKey(t => t.OfferId);
        builder.Property(t => t.OfferId).HasColumnName("offer_id").ValueGeneratedNever();

        // No enforced FK on source_cv_id (provenance only): the tailored CV survives the source CV being
        // replaced/deleted. The CvId global converter maps it to uuid (no ConfigureConventions line needed).
        builder.Property(t => t.SourceCvId).HasColumnName("source_cv_id");

        builder.Property(t => t.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Attempts).HasColumnName("attempts");
        builder.Property(t => t.GenerationVersion).HasColumnName("generation_version");
        builder.Property(t => t.Prompt).HasColumnName("prompt");
        builder.Property(t => t.EmphasisedSkills).HasColumnName("emphasised_skills").HasJsonbListConversion<string>();
        builder.Property(t => t.HtmlFileName).HasColumnName("html_file_name").HasMaxLength(120);
        builder.Property(t => t.PdfFileName).HasColumnName("pdf_file_name").HasMaxLength(120);
        builder.Property(t => t.GeneratedAt).HasColumnName("generated_at");
        builder.Property(t => t.LastError).HasColumnName("last_error");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");

        // 1:1 with the offer; cascade keeps referential integrity under the 003 restore's TRUNCATE … CASCADE.
        // Offers are only ever soft-marked unavailable (never hard-deleted), so a delisted offer keeps its
        // tailored CV (FR-018, data-model §2).
        builder.HasOne<Offer>()
            .WithOne()
            .HasForeignKey<TailoredCv>(t => t.OfferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.State);
    }
}
