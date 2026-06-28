using JobOfferMatcher.Domain.Sources;

namespace JobOfferMatcher.Application.Scanning;

/// <summary>
/// Resolves the <see cref="IJobSource"/> adapter that collects a given configured source
/// (by <see cref="JobSource.Kind"/>). Keeps the orchestrator source-agnostic (FR-003).
/// </summary>
public interface IJobSourceFactory
{
    IJobSource Create(JobSource source);
}
