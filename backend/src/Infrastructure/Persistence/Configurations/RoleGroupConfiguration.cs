using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.RoleGroups;
using JobOfferMatcher.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

internal sealed class RoleGroupConfiguration : IEntityTypeConfiguration<RoleGroup>
{
    public void Configure(EntityTypeBuilder<RoleGroup> builder)
    {
        builder.ToTable("role_group");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(g => g.MemberOfferIds)
            .HasColumnName("member_offer_ids")
            .HasField("_members")
            .HasJsonbListConversion<OfferId>();

        builder.Property(g => g.Confidence)
            .HasColumnName("confidence")
            .HasConversion(c => c.Value, v => new MatchConfidence(v));

        builder.Property(g => g.UserOverride)
            .HasColumnName("user_override")
            .HasConversion<string?>()
            .HasMaxLength(20);
    }
}
