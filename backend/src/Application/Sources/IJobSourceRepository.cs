using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Sources;

namespace JobOfferMatcher.Application.Sources;

/// <summary>Persistence port for configured <see cref="JobSource"/>s (FR-002/003).</summary>
public interface IJobSourceRepository
{
    Task<IReadOnlyList<JobSource>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<JobSource>> GetEnabledAsync(CancellationToken ct = default);
    Task<JobSource?> GetByIdAsync(SourceId id, CancellationToken ct = default);
    Task AddAsync(JobSource source, CancellationToken ct = default);
}
