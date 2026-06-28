namespace JobOfferMatcher.Application.Abstractions;

/// <summary>
/// Commits the current set of tracked changes. Lets Application use cases persist without
/// referencing EF Core (dependency direction stays inward — Principle I).
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
