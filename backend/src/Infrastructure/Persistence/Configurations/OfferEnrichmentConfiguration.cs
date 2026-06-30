using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

internal sealed class OfferEnrichmentConfiguration : IEntityTypeConfiguration<OfferEnrichment>
{
    public void Configure(EntityTypeBuilder<OfferEnrichment> builder)
    {
        builder.ToTable("offer_enrichment");
        builder.HasKey(e => e.OfferId);
        builder.Property(e => e.OfferId).HasColumnName("offer_id").ValueGeneratedNever();

        builder.Property(e => e.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Attempts).HasColumnName("attempts");
        builder.Property(e => e.Summary).HasColumnName("summary");
        builder.Property(e => e.KeySkills).HasColumnName("key_skills").HasJsonbListConversion<string>();
        builder.Property(e => e.InputsHash).HasColumnName("inputs_hash").HasMaxLength(80);
        builder.Property(e => e.ProducedAt).HasColumnName("produced_at");
        builder.Property(e => e.LastError).HasColumnName("last_error");

        // 1:1 with the offer; deleting an offer cascades its derived caches.
        builder.HasOne<Offer>()
            .WithOne()
            .HasForeignKey<OfferEnrichment>(e => e.OfferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.State);
    }
}
