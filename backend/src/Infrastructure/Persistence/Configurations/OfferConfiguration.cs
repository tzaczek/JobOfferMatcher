using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Salary;
using JobOfferMatcher.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

internal sealed class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        builder.ToTable("offers");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();

        // The source-stable identity (FR-011): UNIQUE(source_id, native_key).
        builder.OwnsOne(o => o.ExternalRef, ext =>
        {
            ext.Property(e => e.SourceId).HasColumnName("source_id");
            ext.Property(e => e.NativeKey).HasColumnName("native_key").HasMaxLength(400);
            ext.Property(e => e.Kind).HasColumnName("identity_kind").HasConversion<string>().HasMaxLength(40);
            ext.HasIndex(e => new { e.SourceId, e.NativeKey }).IsUnique();
        });
        builder.Navigation(o => o.ExternalRef).IsRequired();
        builder.Ignore(o => o.SourceId);

        builder.Property(o => o.Title).HasColumnName("title").HasMaxLength(500);
        builder.Property(o => o.Company).HasColumnName("company").HasMaxLength(300);
        builder.Property(o => o.Location).HasColumnName("location").HasMaxLength(300);
        builder.Property(o => o.WorkMode).HasColumnName("work_mode").HasConversion<string>().HasMaxLength(40);
        builder.Property(o => o.EmploymentType).HasColumnName("employment_type").HasMaxLength(120);
        builder.Property(o => o.Seniority).HasColumnName("seniority").HasMaxLength(80);
        builder.Property(o => o.DescriptionHtml).HasColumnName("description_html");
        builder.Property(o => o.CanonicalUrl).HasColumnName("canonical_url").HasMaxLength(1000);

        builder.Property(o => o.SalaryBands).HasColumnName("salary_bands").HasJsonbListConversion<SalaryBand>();
        builder.Property(o => o.RequiredSkills).HasColumnName("required_skills").HasJsonbListConversion<string>();
        builder.Property(o => o.NiceToHaveSkills).HasColumnName("nice_skills").HasJsonbListConversion<string>();

        builder.Property(o => o.PublishedAt).HasColumnName("published_at");
        builder.Property(o => o.LastPublishedAt).HasColumnName("last_published_at");
        builder.Property(o => o.ExpiredAt).HasColumnName("expired_at");

        // Major-tier content fingerprint (research §6).
        builder.OwnsOne(o => o.CurrentFingerprint, fp =>
        {
            fp.Property(f => f.Algorithm).HasColumnName("fingerprint_algo").HasMaxLength(20);
            fp.Property(f => f.Version).HasColumnName("fingerprint_version");
            fp.Property(f => f.Hash).HasColumnName("current_fingerprint").HasMaxLength(64);
        });
        builder.Navigation(o => o.CurrentFingerprint).IsRequired();
        builder.Ignore(o => o.FingerprintVersion);

        builder.Property(o => o.FirstSeenAt).HasColumnName("first_seen_at");
        builder.Property(o => o.LastSeenAt).HasColumnName("last_seen_at");
        builder.Property(o => o.FirstSuggestedAt).HasColumnName("first_suggested_at");

        builder.Property(o => o.Availability).HasColumnName("availability").HasConversion<string>().HasMaxLength(40);
        builder.Property(o => o.DisappearedAt).HasColumnName("disappeared_at");
        builder.Property(o => o.RoleGroupId).HasColumnName("role_group_id");
        builder.Property(o => o.UserStatus).HasColumnName("user_status").HasConversion<string>().HasMaxLength(40);
        builder.Property(o => o.HasUnseenUpdate).HasColumnName("has_unseen_update");

        builder.Property(o => o.Applied).HasColumnName("applied");
        builder.Property(o => o.AppliedAt).HasColumnName("applied_at");
        builder.Property(o => o.ApplicationNote).HasColumnName("application_note").HasMaxLength(Domain.Offers.Offer.MaxApplicationNoteLength);

        builder.HasIndex(o => o.LastSeenAt);
        builder.HasIndex(o => o.UserStatus);
        builder.HasIndex(o => o.Applied);
    }
}
