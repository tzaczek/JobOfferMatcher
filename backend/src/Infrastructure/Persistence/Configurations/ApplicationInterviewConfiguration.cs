using JobOfferMatcher.Domain.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping for <see cref="ApplicationInterview"/> (data-model §4) — mutable interview, FK → application.</summary>
internal sealed class ApplicationInterviewConfiguration : IEntityTypeConfiguration<ApplicationInterview>
{
    public void Configure(EntityTypeBuilder<ApplicationInterview> builder)
    {
        builder.ToTable("application_interview");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(i => i.OfferId).HasColumnName("offer_id");
        builder.Property(i => i.Kind).HasColumnName("kind").HasMaxLength(ApplicationInterview.MaxKindLength);
        builder.Property(i => i.ScheduledAt).HasColumnName("scheduled_at");
        builder.Property(i => i.Interviewer).HasColumnName("interviewer").HasMaxLength(ApplicationInterview.MaxInterviewerLength);
        builder.Property(i => i.Outcome).HasColumnName("outcome").HasColumnType("text");
        builder.Property(i => i.Notes).HasColumnName("notes").HasColumnType("text");
        builder.Property(i => i.CreatedAt).HasColumnName("created_at");

        builder.HasOne<JobApplication>()
            .WithMany()
            .HasForeignKey(i => i.OfferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.OfferId);
        builder.HasIndex(i => i.ScheduledAt);
    }
}
