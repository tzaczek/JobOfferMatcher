using JobOfferMatcher.Domain.Sources;
using JobOfferMatcher.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

internal sealed class JobSourceConfiguration : IEntityTypeConfiguration<JobSource>
{
    public void Configure(EntityTypeBuilder<JobSource> builder)
    {
        builder.ToTable("job_source");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(s => s.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(40);
        builder.Property(s => s.Search).HasColumnName("search_criteria").HasJsonbConversion();
        builder.Property(s => s.RequiresLogin).HasColumnName("requires_login");
        builder.Property(s => s.Enabled).HasColumnName("enabled");

        builder.HasIndex(s => s.Name);
    }
}
