using JobOfferMatcher.Domain.Offers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

internal sealed class OfferEventConfiguration : IEntityTypeConfiguration<OfferEvent>
{
    public void Configure(EntityTypeBuilder<OfferEvent> builder)
    {
        builder.ToTable("offer_event");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(e => e.OfferId).HasColumnName("offer_id");
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at");
        builder.Property(e => e.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(40);
        builder.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb");

        builder.HasIndex(e => e.OfferId);
        builder.HasOne<Offer>().WithMany().HasForeignKey(e => e.OfferId).OnDelete(DeleteBehavior.Cascade);
    }
}
