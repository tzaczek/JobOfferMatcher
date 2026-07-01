using JobOfferMatcher.Domain.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping for <see cref="ApplicationTask"/> (data-model §4) — mutable to-do, FK → application.</summary>
internal sealed class ApplicationTaskConfiguration : IEntityTypeConfiguration<ApplicationTask>
{
    public void Configure(EntityTypeBuilder<ApplicationTask> builder)
    {
        builder.ToTable("application_task");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(t => t.OfferId).HasColumnName("offer_id");
        builder.Property(t => t.Title).HasColumnName("title").HasMaxLength(ApplicationTask.MaxTitleLength);
        builder.Property(t => t.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(t => t.DueAt).HasColumnName("due_at");
        builder.Property(t => t.CompletedAt).HasColumnName("completed_at");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");

        builder.HasOne<JobApplication>()
            .WithMany()
            .HasForeignKey(t => t.OfferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.OfferId);
        builder.HasIndex(t => new { t.OfferId, t.CompletedAt });
    }
}
