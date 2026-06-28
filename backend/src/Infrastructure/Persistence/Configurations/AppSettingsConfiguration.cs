using JobOfferMatcher.Domain.Matching;
using JobOfferMatcher.Domain.Salary;
using JobOfferMatcher.Domain.Settings;
using JobOfferMatcher.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobOfferMatcher.Infrastructure.Persistence.Configurations;

internal sealed class AppSettingsConfiguration : IEntityTypeConfiguration<AppSettings>
{
    public void Configure(EntityTypeBuilder<AppSettings> builder)
    {
        builder.ToTable("app_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(s => s.Normalization).HasColumnName("salary_norm").HasJsonbConversion<SalaryNormalizationSettings>();
        builder.Property(s => s.Weights).HasColumnName("scoring_weights").HasJsonbConversion<ScoringWeights>();
        builder.Property(s => s.Preferences).HasColumnName("profile_prefs").HasJsonbConversion<ProfilePreferences>();
    }
}
