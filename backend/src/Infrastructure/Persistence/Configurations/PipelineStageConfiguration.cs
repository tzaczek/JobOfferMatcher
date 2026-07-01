using JobOfferMatcher.Domain.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping for <see cref="PipelineStage"/> (data-model §4) — the ordered pipeline columns.</summary>
internal sealed class PipelineStageConfiguration : IEntityTypeConfiguration<PipelineStage>
{
    public void Configure(EntityTypeBuilder<PipelineStage> builder)
    {
        builder.ToTable("pipeline_stage");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(PipelineStage.MaxNameLength);
        builder.Property(s => s.Position).HasColumnName("position");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(s => s.Position);
    }
}
