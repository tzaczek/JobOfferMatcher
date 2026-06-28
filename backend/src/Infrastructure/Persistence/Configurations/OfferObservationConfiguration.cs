using JobOfferMatcher.Domain.Offers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

internal sealed class OfferObservationConfiguration : IEntityTypeConfiguration<OfferObservation>
{
    public void Configure(EntityTypeBuilder<OfferObservation> builder)
    {
        builder.ToTable("offer_observation");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(o => o.OfferId).HasColumnName("offer_id");
        builder.Property(o => o.ScanRunId).HasColumnName("scan_run_id");
        builder.Property(o => o.ObservedAt).HasColumnName("observed_at");

        builder.OwnsOne(o => o.Fingerprint, fp =>
        {
            fp.Property(f => f.Algorithm).HasColumnName("fingerprint_algo").HasMaxLength(20);
            fp.Property(f => f.Version).HasColumnName("fingerprint_version");
            fp.Property(f => f.Hash).HasColumnName("fingerprint").HasMaxLength(64);
        });
        builder.Navigation(o => o.Fingerprint).IsRequired();

        builder.HasIndex(o => o.ScanRunId);
        builder.HasIndex(o => o.OfferId);

        builder.HasOne<Offer>().WithMany().HasForeignKey(o => o.OfferId).OnDelete(DeleteBehavior.Cascade);
    }
}
