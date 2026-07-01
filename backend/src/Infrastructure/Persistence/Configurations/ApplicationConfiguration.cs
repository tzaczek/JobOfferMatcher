using JobOfferMatcher.Domain.Applications;
using JobOfferMatcher.Domain.Offers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="JobApplication"/> (data-model §3/§4). A satellite keyed by <c>offer_id</c>
/// (FK → <c>offers</c> cascade, like <c>tailored_cv</c>); <c>current_stage_id</c> → <c>pipeline_stage</c>
/// RESTRICT so a stage in use can't be dropped without reassignment. Enums stored as strings.
/// </summary>
internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> builder)
    {
        builder.ToTable("application");
        builder.HasKey(a => a.OfferId);
        builder.Property(a => a.OfferId).HasColumnName("offer_id").ValueGeneratedNever();
        builder.Property(a => a.CurrentStageId).HasColumnName("current_stage_id");
        builder.Property(a => a.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.AppliedAt).HasColumnName("applied_at");
        builder.Property(a => a.ClosedAt).HasColumnName("closed_at");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        // 1:1 with the offer; cascade keeps referential integrity under 003's TRUNCATE … CASCADE.
        builder.HasOne<Offer>()
            .WithOne()
            .HasForeignKey<JobApplication>(a => a.OfferId)
            .OnDelete(DeleteBehavior.Cascade);

        // The current stage must exist; RESTRICT so a stage still holding applications can't be deleted
        // without reassignment (enforced + steered to a 409 in the service — never orphan an application).
        builder.HasOne<PipelineStage>()
            .WithMany()
            .HasForeignKey(a => a.CurrentStageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.CurrentStageId);
        builder.HasIndex(a => a.Status);
    }
}
