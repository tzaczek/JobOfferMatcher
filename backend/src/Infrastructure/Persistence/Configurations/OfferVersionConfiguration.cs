using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

internal sealed class OfferVersionConfiguration : IEntityTypeConfiguration<OfferVersion>
{
    public void Configure(EntityTypeBuilder<OfferVersion> builder)
    {
        builder.ToTable("offer_version");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(v => v.OfferId).HasColumnName("offer_id");
        builder.Property(v => v.CreatedAt).HasColumnName("created_at");
        builder.Property(v => v.ChangeTier).HasColumnName("change_tier").HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Snapshot).HasColumnName("snapshot").HasJsonbConversion();

        builder.OwnsOne(v => v.Fingerprint, fp =>
        {
            fp.Property(f => f.Algorithm).HasColumnName("fingerprint_algo").HasMaxLength(20);
            fp.Property(f => f.Version).HasColumnName("fingerprint_version");
            fp.Property(f => f.Hash).HasColumnName("fingerprint").HasMaxLength(64);
        });
        builder.Navigation(v => v.Fingerprint).IsRequired();

        builder.HasIndex(v => v.OfferId);
        builder.HasOne<Offer>().WithMany().HasForeignKey(v => v.OfferId).OnDelete(DeleteBehavior.Cascade);
    }
}
