using JobOfferMatcher.Domain.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping for <see cref="ApplicationCommunication"/> (data-model §4) — logged interaction, FK → application.</summary>
internal sealed class ApplicationCommunicationConfiguration : IEntityTypeConfiguration<ApplicationCommunication>
{
    public void Configure(EntityTypeBuilder<ApplicationCommunication> builder)
    {
        builder.ToTable("application_communication");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(c => c.OfferId).HasColumnName("offer_id");
        builder.Property(c => c.OccurredAt).HasColumnName("occurred_at");
        builder.Property(c => c.Direction).HasColumnName("direction").HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Channel).HasColumnName("channel").HasMaxLength(ApplicationCommunication.MaxChannelLength);
        builder.Property(c => c.Summary).HasColumnName("summary").HasColumnType("text");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");

        builder.HasOne<JobApplication>()
            .WithMany()
            .HasForeignKey(c => c.OfferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.OfferId);
    }
}
