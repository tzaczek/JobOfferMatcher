using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

internal sealed class OfferFitConfiguration : IEntityTypeConfiguration<OfferFit>
{
    public void Configure(EntityTypeBuilder<OfferFit> builder)
    {
        builder.ToTable("offer_fit");
        builder.HasKey(f => f.OfferId);
        builder.Property(f => f.OfferId).HasColumnName("offer_id").ValueGeneratedNever();

        builder.Property(f => f.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20);
        builder.Property(f => f.Attempts).HasColumnName("attempts");
        builder.Property(f => f.Score).HasColumnName("score");
        builder.Property(f => f.Matched).HasColumnName("matched").HasJsonbListConversion<string>();
        builder.Property(f => f.Missing).HasColumnName("missing").HasJsonbListConversion<string>();
        builder.Property(f => f.Rationale).HasColumnName("rationale");
        builder.Property(f => f.InputsHash).HasColumnName("inputs_hash").HasMaxLength(80);
        builder.Property(f => f.ProducedAt).HasColumnName("produced_at");
        builder.Property(f => f.LastError).HasColumnName("last_error");

        builder.HasOne<Offer>()
            .WithOne()
            .HasForeignKey<OfferFit>(f => f.OfferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => f.State);
    }
}
