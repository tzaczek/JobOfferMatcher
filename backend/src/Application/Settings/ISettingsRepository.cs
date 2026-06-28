using JobOfferMatcher.Domain.Settings;

namespace JobOfferMatcher.Application.Settings;

/// <summary>Persistence port for the single-row <see cref="AppSettings"/> (data-model §Settings).</summary>
public interface ISettingsRepository
{
    /// <summary>Get the settings, creating the default single row if absent.</summary>
    Task<AppSettings> GetAsync(CancellationToken ct = default);
}
