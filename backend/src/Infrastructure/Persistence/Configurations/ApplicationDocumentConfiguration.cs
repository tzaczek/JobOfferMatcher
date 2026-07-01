using JobOfferMatcher.Domain.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="ApplicationDocument"/> (data-model §4) — attachment metadata, FK → application.
/// The bytes live flat in <c>cv-data</c> (§6); this row only records names/type/size.
/// </summary>
internal sealed class ApplicationDocumentConfiguration : IEntityTypeConfiguration<ApplicationDocument>
{
    public void Configure(EntityTypeBuilder<ApplicationDocument> builder)
    {
        builder.ToTable("application_document");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(d => d.OfferId).HasColumnName("offer_id");
        builder.Property(d => d.StoredFileName).HasColumnName("stored_file_name").HasMaxLength(200);
        builder.Property(d => d.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(400);
        builder.Property(d => d.ContentType).HasColumnName("content_type").HasMaxLength(200);
        builder.Property(d => d.SizeBytes).HasColumnName("size_bytes");
        builder.Property(d => d.AddedAt).HasColumnName("added_at");

        builder.HasOne<JobApplication>()
            .WithMany()
            .HasForeignKey(d => d.OfferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => d.OfferId);
    }
}
