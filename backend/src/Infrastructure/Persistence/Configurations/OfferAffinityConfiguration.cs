using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

internal sealed class OfferAffinityConfiguration : IEntityTypeConfiguration<OfferAffinity>
{
    public void Configure(EntityTypeBuilder<OfferAffinity> builder)
    {
        builder.ToTable("offer_affinity");
        builder.HasKey(a => a.OfferId);
        builder.Property(a => a.OfferId).HasColumnName("offer_id").ValueGeneratedNever();

        builder.Property(a => a.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Attempts).HasColumnName("attempts");
        builder.Property(a => a.Score).HasColumnName("score");
        builder.Property(a => a.Resembles).HasColumnName("resembles").HasJsonbListConversion<string>();
        builder.Property(a => a.Rationale).HasColumnName("rationale");
        builder.Property(a => a.InputsHash).HasColumnName("inputs_hash").HasMaxLength(80);
        builder.Property(a => a.ProducedAt).HasColumnName("produced_at");
        builder.Property(a => a.LastError).HasColumnName("last_error");

        builder.HasOne<Offer>()
            .WithOne()
            .HasForeignKey<OfferAffinity>(a => a.OfferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.State);
    }
}
