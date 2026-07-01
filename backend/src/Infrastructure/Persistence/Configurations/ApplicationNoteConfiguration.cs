using JobOfferMatcher.Domain.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping for <see cref="ApplicationNote"/> (data-model §4) — append-only journal, FK → application.</summary>
internal sealed class ApplicationNoteConfiguration : IEntityTypeConfiguration<ApplicationNote>
{
    public void Configure(EntityTypeBuilder<ApplicationNote> builder)
    {
        builder.ToTable("application_note");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(n => n.OfferId).HasColumnName("offer_id");
        builder.Property(n => n.Body).HasColumnName("body").HasColumnType("text");
        builder.Property(n => n.CreatedAt).HasColumnName("created_at");

        builder.HasOne<JobApplication>()
            .WithMany()
            .HasForeignKey(n => n.OfferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(n => n.OfferId);
    }
}
